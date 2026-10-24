# Byta 宣传片（电影感版）

纯黑、像素字、碎裂魔方。全部由 **Blender 5.1 + Python** 程序化生成，没有手工摆放任何关键帧。

```
promo/cinematic/
├── build_cinematic.py      # 场景构建（几何/材质/灯光/镜头/动画全在脚本里）
├── soundtrack.py           # OverWorld-Infinity 合成（加法合成钢琴 + FFT 卷积混响）
├── render.py               # 渲染 PNG 序列
├── make_cinematic.sh       # PNG + 音轨 → mp4
├── byta-cinematic.blend    # 可编辑源文件（音轨已在 VSE 里）
├── OverWorld-Infinity.wav  # 11.5s / 44.1kHz / 立体声
└── byta-cinematic.mp4      # 成片 1920×1080 / 24fps / 11s
```

## 分镜

| 帧 | 时间 | 画面 |
|---|---|---|
| 1–47 | 0–1.96s | 纯黑。一行 Unifont 像素字逐字符打出：`Zero-Overhead. OpenGL 4.6. .NET 10.`；打完停留一拍后整行熄灭 |
| 48 | 2.00s | 突然亮起：64 块碎片从画面外爆现，核心光爆闪 |
| 48–74 | 2.0–3.1s | 碎片极速收拢，拼回立方体轮廓（缝隙保留、表面参差） |
| 74–204 | 3.1–8.5s | 魔方在屏幕中心极速旋转（三轴倾斜，约 3.4 圈），冷暖光对撞，运动模糊 |
| 84–180 | 3.5–7.5s | 字幕：`No Chromium. No Sluggish UI. Just native speed.` |
| 204–216 | 8.5–9.0s | 魔方收缩进中心消失 |
| 212–235 | 8.8–9.8s | 纯黑背景上逐字符打出 `$ dotnet add package Byta.GL`，光标闪烁 |
| 246–264 | 10.25–11s | 黑屏 |

## 重新生成

```bash
cd promo/cinematic

# 1. 音乐（用 Blender 自带的 python，里面有 numpy）
/Applications/Blender.app/Contents/Resources/5.1/python/bin/python3.13 soundtrack.py

# 2. 场景
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build_cinematic.py

# 3. 渲染（264 帧，1080p，约 5 分钟）
/Applications/Blender.app/Contents/MacOS/Blender -b byta-cinematic.blend --python render.py -a

# 4. 合成
./make_cinematic.sh
```

`frames/` 约 200MB，不进 git（`.gitignore` 已排除），随时可重建。

## 《OverWorld-Infinity》

没有任何采样库，全部是代码算出来的：

- **音色**：加法合成。10 个泛音，按钢琴的非谐性 `f_n = n·f0·√(1+B·n²)`（B=0.00042）偏移 ——
  少了这个，听起来就是电子琴而不是钢琴。高次泛音衰减更快（`1.55 + 0.95n`），
  再加 2ms 的带限噪声当琴槌声。
- **编曲**：A 小调五声，五句，每句 2.2s。前 2 秒只有两声极轻的高音，
  第 2.00 秒正好是画面亮起的位置，一记低音 + 和弦块砸下去。
- **混响**：指数衰减噪声做 impulse response，在频域按 `1/(1+(f/2600)^1.35)` 衰减高频
  （空气与墙面吸收），再用 FFT 卷积。左右声道用不同随机种子，混响才有宽度。
- **母带**：34Hz 高通 + 8kHz 以上 +0.55 的空气感提升，归一化到 -1.2 dBFS。
  成品 -13.5 LUFS，动态范围 2.8 LU。

## 实现里踩到的坑（都写在代码注释里）

1. **`Text.data.body` 在 Blender 5.1 不可打关键帧**（`property body not animatable`）。
   打字机效果只能把每个字符做成独立对象、各自控制发光。
2. **Principled 的默认镜面反射会让"关灯"失效**。Base Color 刷成纯黑、emission 调 0，
   物体仍会被灯照出轮廓 —— 字幕就会在成片里隐隐浮出来。改用纯 Emission 节点。
3. **`emission=0` 不等于"不存在"**。不透明几何体照样在魔方前面挡出一块黑字。
   必须再叠一个 scale 可见窗口（`set_visible_window`）。
4. **BEZIER 在长间隔上会拱起来**。从 `(1, 0)` 直接跳 `(238, 3.0)`，第 140 帧已经爬到 1.89。
   要在第一个非零键的前一帧插一个 0 把它压平。
5. **16:9 下限制构图的是垂直视野**，不是水平。52mm/36mm 传感器垂直半角只有 10.8°，
   距离 7.4 时可视高度只有 2.82 —— 正好被 2.8 的立方体贴满，看起来像贴脸。
6. **碎片收拢后必须把各自旋转归零**。否则 64 块各转各的，立方体轮廓散成"一堆骰子"。
   极速旋转交给父级空物体。
7. **屏幕空间反射会把字幕"印"到金属面上**。看起来像脏点，放大才发现是字。直接关 raytracing。
8. **Blender 5.1 的引擎标识又改回 `BLENDER_EEVEE`**（4.2 是 `BLENDER_EEVEE_NEXT`）。
   写死会直接抛 `enum not found`，要动态探测。
9. **相机手算 euler 容易差几度**，表现就是魔方偏在画面一角。用 Track To 约束对准空物体。
