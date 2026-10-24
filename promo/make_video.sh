#!/usr/bin/env bash
# 把 frames/ 里的 PNG 序列合成 mp4。
set -euo pipefail

cd "$(dirname "$0")"

FRAMES=${FRAMES:-24}
CRF=${CRF:-18}

count=$(ls frames/frame_*.png 2>/dev/null | wc -l | tr -d ' ')
if [ "$count" -eq 0 ]; then
  echo "frames/ 里没有 PNG，先跑渲染：" >&2
  echo "  /Applications/Blender.app/Contents/MacOS/Blender -b byta-promo.blend --python render.py -a" >&2
  exit 1
fi

echo "共 $count 帧，按 ${FRAMES}fps 合成…"

ffmpeg -y -framerate "$FRAMES" -i frames/frame_%04d.png \
  -c:v libx264 -pix_fmt yuv420p -crf "$CRF" -preset slow \
  -movflags +faststart \
  byta-promo.mp4 2>&1 | tail -3

ls -lh byta-promo.mp4
