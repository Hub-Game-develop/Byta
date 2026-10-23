# Byta

现代、零分配开销的 .NET OpenGL 封装，带可插拔的 GL 实现。

```csharp
using var session = BytaRuntime.Initialize();   // 自动挑最高版本：内置 Mesa 4.6 → 系统 4.1
using var program = GlProgram.FromSource(vertexGlsl, fragmentGlsl);
using var vao = GlVertexArray.Create();
vao.Bind();
buffer.Bind();
GlVertexArray.Attribute(0, 3, VertexAttribType.Float, strideBytes: 32);

program.Use();
program.GetUniform("uViewProjection").Set(viewProjection);
GlDraw.ElementsInstanced(PrimitiveType.Triangles, 36, IndexType.UnsignedInt, 0, 1000);
```

![示例渲染](https://raw.githubusercontent.com/Hub-Game-develop/Byta/main/assets/render-gl46.png)

## 特性

- **零开销绑定**：2651 条 GL 命令编译成 `delegate* unmanaged<...>` 函数指针字段，没有 DllImport stub。
- **零托管分配热路径**：`readonly struct` 值句柄 + `Span` 上传 + 线程局部状态缓存（实测 20000 次绘制分配 0 字节）。
- **实现可插拔**：内置 Mesa(EGL) 提供 **GL 4.6 core**；没有则回退系统实现（macOS 上是 GL 4.1）。
- **跨平台**：macOS 已实测（GL 4.6 / GL 4.1 双路径）；Windows / Linux 的入口点解析已实现。
- 目标框架 `net10.0`，AOT 友好（无反射）。

## 本包包含什么

- `lib/net10.0/Byta.dll` —— 对象层（Buffer / VertexArray / Shader / Program / Uniform / Texture2D / Framebuffer / Query）
- `runtimes/osx-arm64/native/` —— macOS Apple Silicon 的内置 GL 实现（Mesa 26.2.5 + llvmpipe，约 208 MB）

不需要内置实现的话删掉 `runtimes/` 即可自动回退系统实现。

## 相关包

- `Byta.Bindings` —— 裸绑定层（本包会自动带上，通常不用单独引用）

## 文档

完整说明、性能数据与已知限制：[github.com/Hub-Game-develop/Byta](https://github.com/Hub-Game-develop/Byta)

## 许可证

MIT。随包分发的原生组件各自保留原许可证：Mesa(MIT)、LLVM(Apache-2.0 WITH LLVM-exception)、
MoltenVK(Apache-2.0)、Vulkan-Headers(Apache-2.0)、zstd(BSD-3-Clause)、z3(MIT)、SPIRV-Tools(Apache-2.0)。
