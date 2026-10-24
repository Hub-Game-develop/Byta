# Byta.Browser

把 **Byta 的同一套 API** 转发给浏览器的 **WebGL2**。不需要 OpenGL 驱动、不需要 Chromium 的
额外配置 —— 任何有浏览器的平台（Windows / macOS / Linux）都能渲染。

```bash
dotnet add package Byta.Browser
```

```csharp
// Program.cs（Blazor WebAssembly）
var capabilities = await BytaBrowserRuntime.InitializeAsync("byta-canvas");
Console.WriteLine(capabilities);   // GL 3.0 core | GLSL 3.00 | WebGL 2.0

// 之后就是普通的 Byta 代码，和桌面版完全一致
using var program = GlProgram.FromSource(vertexGlsl, fragmentGlsl);
using var vao = GlVertexArray.Create();
vao.Bind();
GlDraw.ElementsInstanced(PrimitiveType.Triangles, 36, IndexType.UnsignedInt, 0, 64);
```

页面里放一个 canvas：

```html
<canvas id="byta-canvas" width="1280" height="720"></canvas>
```

## 包做了什么

- 转发 `GlApi` 的门面调用给 `byta-gl.v9.js`（WebGL2 + 句柄表）
- 自动把 JS 复制到使用方的 `wwwroot/_framework/`（`buildTransitive/Byta.Browser.targets`）
  —— Blazor 的模块导入只认这条路径，放在 `_content/` 里拿不到
- 默认把 `BytaIncludeBundledNative` 设为 `false`：浏览器后端用不到 `Byta.GL` 里那份
  208MB 的 Mesa 原生实现，不必白拷

## 能覆盖多少

WebGL2 =  **GLES 3.0 核心**。桌面 GL 专有的东西用不了，会抛 `GlNotSupportedException`：

| 命令 | 状态 |
| --- | --- |
| `glBufferStorage`（4.4 不可变存储） | 不支持 |
| `glMapBuffer` / `glMapBufferRange` | 不支持 |
| `glDebugMessageCallback`（KHR_debug） | 不支持 |
| `glTexStorage2D`（4.2） | 不支持 |
| `glDispatchCompute`（4.3 计算着色器） | 不支持 |
| `glProgramUniform*`（DSA） | **自动退化**为 `glUseProgram` + `glUniform*` |

用 `GlApi.IsAvailable("glBufferStorage")` 可以提前判断，不必等异常。

## 已知限制（实验性）

- **实例化场景的画面构图仍有问题**：上下文、着色器、缓冲、VAO、绘制调用都已跑通，
  `uViewProjection` 回读校验也通过，但顶点变换这条链路的最终画面不正确。
  非实例化 + 无投影的最小场景可用。介意的话先别用在生产。
- 只测过 Blazor WebAssembly（单线程）。没有验证过 MAUI Blazor Hybrid。
- 着色器版本自动切成 `#version 300 es` 并补 `precision`；`uniformMatrix4fv` 的
  `transpose` 参数会被强制为 `false`（WebGL 规范要求）。

## 相关包

- `Byta.GL` —— 本包会自动带上（对象层与绑定）
- `Byta.Bindings` —— 裸绑定层，通常不用单独引用

## 许可证

MIT。
