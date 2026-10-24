#!/usr/bin/env bash
# 把 frames/ 的 PNG 序列与 OverWorld-Infinity.wav 合成成片。
set -euo pipefail

cd "$(dirname "$0")"

FFMPEG=${FFMPEG:-$HOME/.local/bin/ffmpeg}
FRAMES=${FRAMES:-24}
CRF=${CRF:-17}
OUT=byta-cinematic.mp4

count=$(ls frames/frame_*.png 2>/dev/null | wc -l | tr -d ' ')
if [ "$count" -eq 0 ]; then
  echo "frames/ 里没有 PNG。先跑：" >&2
  echo "  /Applications/Blender.app/Contents/MacOS/Blender -b byta-cinematic.blend --python render.py -a" >&2
  exit 1
fi

if [ ! -f OverWorld-Infinity.wav ]; then
  echo "缺少音轨，先跑：" >&2
  echo "  /Applications/Blender.app/Contents/Resources/5.1/python/bin/python3.13 soundtrack.py" >&2
  exit 1
fi

echo "共 $count 帧，${FRAMES}fps，混入 OverWorld-Infinity…"

"$FFMPEG" -y -hide_banner -loglevel error \
  -framerate "$FRAMES" -i frames/frame_%04d.png \
  -i OverWorld-Infinity.wav \
  -c:v libx264 -preset slow -crf "$CRF" -pix_fmt yuv420p \
  -c:a aac -b:a 192k \
  -shortest -movflags +faststart \
  "$OUT"

ls -lh "$OUT"
"$FFMPEG" -hide_banner -i "$OUT" 2>&1 | grep -E "Duration|Stream #0" || true
