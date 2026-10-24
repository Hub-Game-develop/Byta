# Byta 宣传动画

全流程用 **Blender 5.1 + Python（bpy）** 生成，不依赖手工操作——场景、动画、材质、
渲染参数全部写在脚本里，改一行重跑就能出新的版本。

| 文件 | 说明 |
| --- | --- |
| `build_promo.py` | 构建整个场景并保存 `byta-promo.blend` |
| `byta-promo.blend` | **可交付的源文件**，双击用 Blender 打开，直接 Render Animation |
| `render.py` | 渲染 PNG 序列（可中断续渲） |
| `frames/` | 渲染出的 240 张 PNG |
| `byta-promo.mp4` | 最终成片（H.264，1920×1080，24fps，10 秒） |

## 重新生成

```bash
# 1. 重建场景（会覆盖 .blend）
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build_promo.py

# 2. 渲染 PNG 序列（约 5s/帧 × 240 帧 ≈ 20 分钟）
/Applications/Blender.app/Contents/MacOS/Blender -b byta-promo.blend --python render.py -a

# 3. 合成 mp4
ffmpeg -framerate 24 -i frames/frame_%04d.png \
       -c:v libx264 -pix_fmt yuv420p -crf 18 -preset slow \
       -movflags +faststart byta-promo.mp4
```

想直接要视频也可以用 Blender 自己输出：场景里已经把
`image_settings.media_type` 设为 `VIDEO`、`file_format` 设为 `FFMPEG`（H.264），
`.blend` 打开后按 Render Animation 会直接吐 mp4。

## 视觉设计

刻意和项目本身对齐，不是随便找个模板：

- **背景** `#0B0D13`（和 WebGL demo 页面同色），靠地面镜面反射出层次
- **立方体阵列** 9×9，间距 3.05、边长 2.3——就是 demo 里实例化渲染用的那套参数。
  中心高、外围低，形成一块"晶体地貌"；升起顺序按离中心的距离延迟，是一道扩散的波浪
- **配色** 深蓝 → 青蓝（`#2B6CFF` → `#22D3EE`），中心几个立方体更亮，作为视觉焦点
- **字体** 标题 Arial Bold，中文用 Hiragino Sans GB（`/System/Library/Fonts/`）
- **光晕** EEVEE 4.2 之后移除了内置 bloom，所以在合成器里加了 Glare（Fog Glow）

## 分镜（24fps / 240 帧 / 10 秒）

| 帧 | 内容 |
| --- | --- |
| 1–45 | 立方体阵列从地下升起，按距离扩散；相机低角度推进 |
| 58–120 | "Byta" 升起 + 轻微放大，"MODERN .NET OPENGL" 跟随 |
| 80–160 | 三行技术要点依次浮出 |
| 110–205 | 相机环绕到侧后方并拉高，露出整片阵列 |
| 205–240 | 缓慢回落定格 |

## 改的时候注意这几个坑

1. **Blender 5.x 的 Action 变了**：`action.fcurves` 不存在了，要沿
   `layers → strips → channelbags` 取曲线（`iter_fcurves()` 里做了兼容）。
2. **视频输出要先切 `media_type`**：`image_settings.media_type = 'VIDEO'` 之后，
   `file_format` 的枚举里才会出现 `FFMPEG`，否则报 `enum "FFMPEG" not found`。
3. **合成器也换了 API**：不是 `scene.node_tree`，而是
   `scene.compositing_node_group`；输出节点用 `NodeGroupOutput`，
   并且要先在 `group.interface` 里声明一个输出 socket。
4. **Text 对象默认是平躺的**（法线朝 +Z），要 `rotation=(radians(90), 0, 0)` 才立起来面向相机。
5. **文字和立方体的 z 区间要留开**，否则升起过程中会互相穿插。
