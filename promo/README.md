# Byta 宣传动画

全流程用 **Blender 5.1 + Python（bpy）** 生成，不依赖手工操作——场景、动画、材质、
渲染参数全部写在脚本里，改一行重跑就能出新的版本。

| 文件 | 说明 |
| --- | --- |
| `build_promo.py` | 构建整个场景并保存 `byta-promo.blend` |
| `byta-promo.blend` | **可交付的源文件**，双击用 Blender 打开，直接 Render Animation |
| `render.py` | 渲染 PNG 序列（可中断续渲） |
| `make_video.sh` | 把 PNG 序列合成 mp4 |
| `frames/` | 渲染出的 336 张 PNG（不进 git） |
| `byta-promo.mp4` | 最终成片（H.264，1920×1080，24fps，14 秒） |

## 重新生成

```bash
# 1. 重建场景（会覆盖 .blend）
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build_promo.py

# 2. 渲染 PNG 序列（约 4.5s/帧 × 336 帧 ≈ 25 分钟）
/Applications/Blender.app/Contents/MacOS/Blender -b byta-promo.blend --python render.py -a

# 3. 合成 mp4
./make_video.sh
```

想直接要视频也可以用 Blender 自己输出：场景里已经把
`image_settings.media_type` 设为 `VIDEO`、`file_format` 设为 `FFMPEG`（H.264），
`.blend` 打开后按 Render Animation 会直接吐 mp4。

## 分镜（24fps / 336 帧 / 14 秒）

| 帧 | 章节 | 内容 |
| --- | --- | --- |
| 1–45 | 开场 | 立方体阵列从地下升起，按离中心的距离形成扩散波浪 |
| 46–132 | 标题 | "Byta" + "MODERN .NET OPENGL" + 定位句"一套 API，本机与浏览器都能渲染" |
| 140–200 | API ① | `GlBuffer`（值句柄 / Span 直传 / 零分配）、`GlProgram`（编译失败带驱动日志） |
| 204–262 | API ② | `GlState`（线程局部状态缓存）、`GlApi`（原生与 WebGL2 的后端门面） |
| 268–306 | 代码 | 四行真实调用逐行出现：Initialize → GlBuffer → Allocate → ElementsInstanced |
| 310–336 | 落版 | "Byta" + **hub-game-develop.github.io/Byta/api/** + "API 参考" |

结尾链接指向由 `tools/gen_api_docs.py` 从源码注释生成的 API 参考页
（51 个公开类型 / 235 个成员）。

## 视觉设计

刻意和项目本身对齐，不是随便找个模板：

- **背景** `#0B0D13`（和 WebGL demo 页面同色），靠地面镜面反射出层次
- **立方体阵列** 9×9，间距 3.05、边长 2.3——就是 demo 里实例化渲染用的那套参数。
  中心高、外围低，形成一块"晶体地貌"
- **配色** 深蓝 → 青蓝（`#2B6CFF` → `#22D3EE`），中心几个立方体更亮，作为视觉焦点
- **字体** 标题 Arial Bold，正文 Hiragino Sans GB，代码 SF Mono（都在 `/System/Library/Fonts/`）
- **光晕** EEVEE 4.2 之后移除了内置 bloom，所以在合成器里加了 Glare（Fog Glow）

## 改的时候注意这几个坑（都踩过）

1. **Blender 5.x 的 Action 变了**：`action.fcurves` 不存在了，要沿
   `layers → strips → channelbags` 取曲线（`iter_fcurves()` 里做了兼容）。
2. **视频输出要先切 `media_type`**：`image_settings.media_type = 'VIDEO'` 之后，
   `file_format` 的枚举里才会出现 `FFMPEG`，否则报 `enum "FFMPEG" not found`。
3. **合成器也换了 API**：不是 `scene.node_tree`，而是
   `scene.compositing_node_group`；输出节点用 `NodeGroupOutput`，
   并且要先在 `group.interface` 里声明一个输出 socket。
4. **不要用 Track To 做文字 billboard**：相机接近水平时 up 轴与视线平行，
   会退化成万向锁，把文字压成一条线。文字固定朝 -Y、相机全程待在 -Y 半空间即可。
5. **文字在世界空间，字号必须匹配相机距离**：同一个 size 在 20 和 45 的距离下
   差一倍多，每换一段镜头都要重新核字号。
6. **淡出关键帧别和"保持到末帧"撞在同一帧**：Bezier 会在结束前十来帧就把字缩没。
   `animate_window(..., fade_out=False)` 就是给落版用的。
7. **等宽字体不含中文**：代码注释里写中文会渲染成方块，注释要用英文。
