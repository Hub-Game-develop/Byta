#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
OverWorld-Infinity —— 程序合成的空灵钢琴曲。

不依赖任何采样库：钢琴音色用加法合成（含钢琴特有的非谐性泛音 + 锤击噪声），
混响用 FFT 卷积（指数衰减噪声做 impulse response，并让高频衰减更快）。

需要 numpy（Blender 自带的 python 就有）：
    /Applications/Blender.app/Contents/Resources/5.1/python/bin/python3.13 soundtrack.py

输出：OverWorld-Infinity.wav（44.1kHz / 16bit / 立体声）
"""

from __future__ import annotations

import math
import pathlib
import wave

import numpy as np

SR = 44100
DURATION = 11.5

HERE = pathlib.Path(__file__).resolve().parent
OUTPUT = HERE / "OverWorld-Infinity.wav"

# 音符名 → MIDI
_NOTE_OFFSETS = {"C": 0, "C#": 1, "D": 2, "D#": 3, "E": 4, "F": 5,
                 "F#": 6, "G": 7, "G#": 8, "A": 9, "A#": 10, "B": 11}


def midi_to_freq(midi: float) -> float:
    return 440.0 * 2.0 ** ((midi - 69.0) / 12.0)


def note(name: str) -> int:
    """'A4' / 'C#5' → MIDI 号。"""
    letter = name[0].upper()
    rest = name[1:]
    accidental = 1 if rest.startswith("#") else -1 if rest.startswith("b") else 0
    octave = int(rest.lstrip("#b"))
    return 12 * (octave + 1) + _NOTE_OFFSETS[letter] + accidental


# ---------------------------------------------------------------- 钢琴音色
def piano(freq: float, duration: float, velocity: float,
          brightness: float = 1.0, detune: float = 0.0) -> np.ndarray:
    """
    加法合成的钢琴单音。

    几个刻意的细节：
    * 非谐性：真实钢琴的泛音不是严格整数倍，f_n = n·f0·sqrt(1 + B·n²)。
      少了这个，听起来就是电子琴而不是钢琴。
    * 高次泛音衰减更快，模拟琴弦能量向高频耗散。
    * 泛音相位错开，避免起音瞬间出现方波般的"咔"。
    """
    n = int(duration * SR)
    t = np.arange(n, dtype=np.float64) / SR

    fundamental = freq * (1.0 + detune)
    inharmonicity = 0.00042
    # (泛音序号, 相对幅度)
    partials = (
        (1, 1.00), (2, 0.46), (3, 0.24), (4, 0.145), (5, 0.085),
        (6, 0.052), (7, 0.032), (8, 0.020), (9, 0.012), (10, 0.008),
    )

    signal = np.zeros(n, dtype=np.float64)
    for index, amplitude in partials:
        partial_freq = fundamental * index * math.sqrt(1.0 + inharmonicity * index * index)
        if partial_freq > SR * 0.45:
            continue
        # 高音区本身泛音就少；brightness 用来压暗低音区的金属感
        gain = amplitude * (brightness ** (index - 1))
        decay = 1.55 + index * 0.95          # 越高越快
        phase = 0.37 * index * index         # 固定错开，可复现
        signal += gain * np.exp(-t * decay) * np.sin(2 * math.pi * partial_freq * t + phase)

    # 起音：极短的键噪 + 指数逼近（不是硬起）
    attack = 1.0 - np.exp(-t * 620.0)
    # 琴槌噪声：2ms 的带限噪声，让起音有"实感"
    hammer_len = min(n, int(0.0022 * SR))
    if hammer_len > 4:
        rng = np.random.default_rng(int(freq) & 0xFFFF)
        hammer = rng.standard_normal(hammer_len) * np.exp(
            -np.arange(hammer_len) / (0.0007 * SR))
        signal[:hammer_len] += hammer * 0.06

    # 尾音渐隐，防止截断爆音
    tail = min(n, int(0.12 * SR))
    envelope = np.ones(n)
    envelope[-tail:] = np.linspace(1.0, 0.0, tail)

    return signal * attack * envelope * velocity


# ---------------------------------------------------------------- 混响
def make_impulse_response(seconds: float, seed: int, decay: float = 2.15) -> np.ndarray:
    """
    指数衰减噪声 + 频域高频染色 = 一个还不错的厅堂 IR。

    IR 的左右声道用不同随机种子生成，混响才有宽度；共用一个种子会听成单声道。
    """
    n = int(seconds * SR)
    t = np.arange(n, dtype=np.float64) / SR
    rng = np.random.default_rng(seed)

    impulse = rng.standard_normal(n) * np.exp(-t * decay)

    # 频域染色：高频衰减更快（空气吸收 + 墙面吸收）
    spectrum = np.fft.rfft(impulse)
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    spectrum /= 1.0 + (freqs / 2600.0) ** 1.35
    impulse = np.fft.irfft(spectrum, n)

    # 去掉 0 时刻的突变（否则会有一个"啪"）
    fade_in = int(0.014 * SR)
    impulse[:fade_in] *= np.linspace(0.0, 1.0, fade_in)
    impulse *= np.exp(-t * decay * 0.35)

    energy = math.sqrt(float(np.sum(impulse * impulse)))
    return impulse / energy if energy > 0 else impulse


def convolve(signal: np.ndarray, impulse: np.ndarray) -> np.ndarray:
    """FFT 卷积，只保留与输入等长的部分。"""
    length = len(signal) + len(impulse) - 1
    size = 1 << (length - 1).bit_length()
    spectrum = np.fft.rfft(signal, size) * np.fft.rfft(impulse, size)
    return np.fft.irfft(spectrum, size)[: len(signal)]


# ---------------------------------------------------------------- 编曲
# 结构与画面严格对齐：
#   0.00–2.00  只有两声极轻的高音 —— 对应纯黑背景 + 一行像素字
#   2.00       画面亮起的同一时刻，一记低音 + 和弦冲击
#   2.00–11.5  五句琶音，最后一句收成单音
INTRO = [(0.00, "A5", 0.15), (1.10, "E5", 0.11)]

CHORDS = [
    # (起始秒, 低音 MIDI, 琶音音级)
    (2.00, note("A1"), [note("A3"), note("E4"), note("A4"), note("C5"), note("E5")]),
    (4.20, note("F1"), [note("F3"), note("C4"), note("F4"), note("A4"), note("C5")]),
    (6.40, note("C2"), [note("C3"), note("G3"), note("C4"), note("E4"), note("G4")]),
    (8.60, note("G1"), [note("G3"), note("D4"), note("G4"), note("B4"), note("D5")]),
    (10.80, note("A1"), [note("A3"), note("E4"), note("A4"), note("C5"), note("E5")]),
]

# 高音闪烁层：稀疏、极轻，制造"星空"感
SPARKLES = [
    (3.25, "A6"), (4.00, "E6"), (5.40, "C6"), (6.15, "G6"),
    (7.55, "D6"), (8.30, "A6"), (9.75, "E6"), (10.55, "A6"),
]


def arrange() -> tuple[np.ndarray, np.ndarray]:
    """返回 (左, 右) 两条干声。"""
    total = int(DURATION * SR)
    left = np.zeros(total)
    right = np.zeros(total)

    def place(signal: np.ndarray, at: float, pan: float, gain: float):
        """pan: -1 左 … +1 右（等功率）。"""
        start = int(at * SR)
        if start >= total:
            return
        chunk = signal[: total - start] * gain
        angle = (pan + 1.0) * math.pi / 4.0
        left[start: start + len(chunk)] += chunk * math.cos(angle)
        right[start: start + len(chunk)] += chunk * math.sin(angle)

    # ---- 前奏：两声极轻的高音
    for at, name, velocity in INTRO:
        place(piano(midi_to_freq(note(name)), 3.2, velocity, brightness=0.9), at,
              -0.2 if at < 0.5 else 0.25, 1.0)

    for index, (start, bass, arpeggio) in enumerate(CHORDS):
        is_last = index == len(CHORDS) - 1

        # 低音：长音、压暗、音量刻意低于琶音（否则整首会轰）
        place(piano(midi_to_freq(bass), 4.6, 0.20, brightness=0.5), start, 0.0, 1.0)

        # 第一句的起始同时是"画面亮起"的冲击：加一层八度低音 + 和弦块
        if index == 0:
            place(piano(midi_to_freq(bass + 12), 3.8, 0.26, brightness=0.6), start, 0.0, 1.0)
            for offset, midi in enumerate(arpeggio[1:4]):
                place(piano(midi_to_freq(midi), 3.6, 0.17, brightness=0.9),
                      start + 0.012 * offset, -0.5 + 0.5 * offset, 1.0)

        if is_last:
            # 收尾：只留一个高音，让最后 0.7 秒自然衰减
            place(piano(midi_to_freq(arpeggio[-1] + 12), 2.8, 0.16, brightness=1.0),
                  start + 0.05, 0.3, 1.0)
            continue

        # 琶音：上行 5 音再回落 2 音，间隔 0.30 秒
        pattern = arpeggio + arpeggio[-2::-1][:2]
        for step, midi in enumerate(pattern):
            offset = start + 0.06 + step * 0.30
            velocity = 0.30 * (1.0 - 0.06 * step)
            pan = -0.4 + 0.2 * (step % 4)
            place(piano(midi_to_freq(midi), 3.2, velocity, brightness=0.95,
                        detune=0.0009 * (step % 3)),
                  offset, pan, 1.0)

        # 每句第二拍补一个五度，让和声不至于太空
        place(piano(midi_to_freq(arpeggio[1] + 12), 2.8, 0.14, brightness=0.85),
              start + 1.20, 0.35, 1.0)

    for at, name in SPARKLES:
        place(piano(midi_to_freq(note(name)), 2.4, 0.09, brightness=1.0), at,
              -0.6 if int(at * 10) % 2 else 0.6, 1.0)

    return left, right


def main():
    dry_left, dry_right = arrange()

    # 立体声混响：左右不同 IR
    ir_left = make_impulse_response(2.9, seed=11)
    ir_right = make_impulse_response(2.9, seed=29)
    wet_left = convolve(dry_left, ir_left)
    wet_right = convolve(dry_right, ir_right)

    # 频域整形：高通掉 34Hz 以下的隆隆声，8kHz 以上补一点空气感
    def shape(channel: np.ndarray) -> np.ndarray:
        spectrum = np.fft.rfft(channel)
        freqs = np.fft.rfftfreq(len(channel), 1.0 / SR)
        high_pass = 1.0 / (1.0 + (34.0 / np.maximum(freqs, 1.0)) ** 4)
        air = 1.0 + 0.55 * np.clip((freqs - 8000.0) / 8000.0, 0.0, 1.0)
        return np.fft.irfft(spectrum * high_pass * air, len(channel))

    dry_left, dry_right = shape(dry_left), shape(dry_right)
    wet_left, wet_right = shape(wet_left), shape(wet_right)

    # 干湿比：湿声 0.40，够空灵但不糊
    dry_mix, wet_mix = 0.74, 0.40
    left = dry_left * dry_mix + wet_left * wet_mix
    right = dry_right * dry_mix + wet_right * wet_mix

    # 整体淡入 / 淡出
    fade_in = int(0.02 * SR)
    left[:fade_in] *= np.linspace(0.0, 1.0, fade_in)
    right[:fade_in] *= np.linspace(0.0, 1.0, fade_in)

    fade_out = int(2.4 * SR)
    tail = np.linspace(1.0, 0.0, fade_out) ** 1.6
    left[-fade_out:] *= tail
    right[-fade_out:] *= tail

    # 归一到 -1.2 dBFS 附近
    peak = max(float(np.max(np.abs(left))), float(np.max(np.abs(right))), 1e-9)
    target = 10 ** (-1.2 / 20.0)
    left *= target / peak
    right *= target / peak

    stereo = np.stack([left, right], axis=1)
    pcm = np.clip(stereo * 32767.0, -32768, 32767).astype("<i2")

    with wave.open(str(OUTPUT), "wb") as handle:
        handle.setnchannels(2)
        handle.setsampwidth(2)
        handle.setframerate(SR)
        handle.writeframes(pcm.tobytes())

    print(f"[soundtrack] {OUTPUT}")
    print(f"[soundtrack] {DURATION}s / {SR}Hz / 立体声 / {OUTPUT.stat().st_size} 字节")
    rms = float(np.sqrt(np.mean(stereo ** 2)))
    print(f"[soundtrack] 峰值 {20 * math.log10(peak * target / peak):.1f} dBFS，RMS {20 * math.log10(rms):.1f} dBFS")


if __name__ == "__main__":
    main()
