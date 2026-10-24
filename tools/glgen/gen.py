#!/usr/bin/env python3
"""glgen — Khronos gl.xml  ->  Byta.Bindings (C#, zero-overhead function-pointer table).

设计目标
--------
1. 零 DllImport stub：所有入口都编译成 `delegate* unmanaged<...>` 字段，
   调用即间接跳转，没有 marshal stub、没有 GC transition stub（blittable 签名）。
2. 可复现：输入是官方 gl.xml，输出是纯文本 .cs，随时可重新生成。
3. 覆盖广：GL 核心 profile 全版本 + 全部扩展，怪类型（CL/EGL/Vulkan 互操作）自动跳过并报告。

用法:
    python3 tools/glgen/gen.py --xml registry/gl.xml --out src/Byta.Bindings/Generated
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field

MAX_CORE_VERSION = (4, 6)

# ---------------------------------------------------------------- 类型映射
# 基础类型 -> C# 标量类型
SCALARS = {
    "GLenum": "uint",
    "GLbitfield": "uint",
    "GLboolean": "byte",
    "GLbyte": "sbyte",
    "GLshort": "short",
    "GLint": "int",
    "GLsizei": "int",
    "GLfixed": "int",
    "GLclampx": "int",
    "GLubyte": "byte",
    "GLushort": "ushort",
    "GLuint": "uint",
    "GLfloat": "float",
    "GLclampf": "float",
    "GLdouble": "double",
    "GLclampd": "double",
    "GLintptr": "nint",
    "GLsizeiptr": "nint",
    "GLintptrARB": "nint",
    "GLsizeiptrARB": "nint",
    "GLint64": "long",
    "GLuint64": "ulong",
    "GLint64EXT": "long",
    "GLuint64EXT": "ulong",
    "GLsync": "nint",
    "GLchar": "byte",
    "GLcharARB": "byte",
    "GLhandleARB": "uint",
    "GLhalf": "ushort",
    "GLhalfARB": "ushort",
    "GLhalfNV": "ushort",
    "void": "void",
}

# 函数指针 typedef（GL 调试回调，签名一致）
CALLBACKS = {
    "GLDEBUGPROC": "delegate* unmanaged<uint, uint, uint, int, byte*, void*, void>",
    "GLDEBUGPROCARB": "delegate* unmanaged<uint, uint, uint, int, byte*, void*, void>",
    "GLDEBUGPROCKHR": "delegate* unmanaged<uint, uint, uint, int, byte*, void*, void>",
    "GLDEBUGPROCAMD": "delegate* unmanaged<uint, uint, uint, int, byte*, void*, void>",
}

# 明确不支持的互操作类型（生成时跳过整条命令）
UNSUPPORTED = {
    "GLeglImageOES",
    "GLeglClientBufferEXT",
    "GLeglImageOES_",
    "GLVULKANPROCNV",
    "GLDEBUGPROCKHR_",
    "struct _cl_context",
    "struct _cl_event",
    "GLcl_event",
    "GLcl_context",
    "_cl_context",
    "_cl_event",
}

CS_KEYWORDS = {
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
    "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
    "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
    "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
    "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
    "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
    "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
    "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
    "void", "volatile", "while", "ref", "record", "var", "dynamic",
}


@dataclass
class Param:
    name: str
    cs_type: str
    raw: str


@dataclass
class Command:
    name: str
    cs_name: str
    ret_cs: str
    ret_raw: str
    raw: str
    params: list[Param] = field(default_factory=list)
    version: str = ""          # 引入该命令的 feature，如 GL_VERSION_3_0

    @property
    def signature(self) -> str:
        """delegate* unmanaged<...> 的完整类型。"""
        if not self.params:
            return f"delegate* unmanaged<{self.ret_cs}>"
        return f"delegate* unmanaged<{', '.join(p.cs_type for p in self.params)}, {self.ret_cs}>"

    @property
    def c_decl(self) -> str:
        ps = ", ".join(p.raw + p.name for p in self.params)
        return f"{self.ret_raw}{self.name}({ps})"


def pascalize(name: str) -> str:
    """glGenBuffers -> GenBuffers"""
    core = name[2:] if name.startswith("gl") else name
    # 处理 2D/3D/xRGB 这类后缀，保持原样大写
    core = re.sub(r"^([A-Z])", lambda m: m.group(1), core)
    out = core[0].upper() + core[1:]
    if out in CS_KEYWORDS:
        out = "@" + out
    return out


def strip_name(raw: str, name: str) -> str:
    """只移除最后一次出现的标识符，避免 GLsizeiptr 里含 size 这类误伤。"""
    i = raw.rfind(name)
    if i < 0:
        return raw
    return raw[:i] + raw[i + len(name):]


def parse_decl(raw: str) -> tuple[str, int]:
    """从 C 声明里抽出 (基础类型, 指针层数)。raw 已去掉变量名。"""
    s = raw.replace("const", " ").replace("struct", " ").strip()
    base = s.strip()
    depth = 0
    while True:
        stripped = base.rstrip()
        if stripped.endswith("*"):
            depth += 1
            base = stripped[:-1]
        else:
            break
    base = base.strip()
    return base, depth


def map_type(raw: str) -> str | None:
    base, depth = parse_decl(raw)
    base = base.strip()
    if base in UNSUPPORTED:
        return None
    if base in CALLBACKS and depth == 0:
        return CALLBACKS[base]
    if base not in SCALARS:
        return None
    cs = SCALARS[base]
    if depth == 0:
        return cs
    if cs == "void":
        # void* / void** 统一用 void*
        return "void*" if depth == 1 else "void**" if depth == 2 else "void*"
    if cs == "nint" and depth == 1:
        return "nint*"
    if cs == "nint" and depth == 2:
        return "nint**"
    stars = "*" * depth
    return f"{cs}{stars}"


def collect_commands(root: ET.Element) -> tuple[dict[str, Command], list[str], list[str]]:
    raw_cmds = {}
    for c in root.find("commands"):
        proto = c.find("proto")
        if proto is None:
            continue
        nm = proto.find("name")
        if nm is None:
            continue
        raw_cmds[nm.text] = c

    # ---- 收集需要的命令名（核心 profile <= 4.6 + 全部扩展）
    needed: dict[str, str] = {}  # name -> feature

    def want(name: str, feature: str):
        if name not in needed:
            needed[name] = feature

    features = [f for f in root.findall("feature") if f.get("api") == "gl"]
    features.sort(key=lambda f: tuple(int(x) for x in f.get("number").split(".")))
    for feat in features:
        num = tuple(int(x) for x in feat.get("number").split("."))
        if num > MAX_CORE_VERSION:
            continue
        fname = feat.get("name")
        for req in feat.findall("require"):
            for cmd in req.findall("command"):
                want(cmd.get("name"), fname)
        for rem in feat.findall("remove"):
            # 只针对 core profile 的移除；compatibility 我们不管
            if rem.get("profile") in (None, "core"):
                for cmd in rem.findall("command"):
                    needed.pop(cmd.get("name"), None)

    for ext in root.findall("./extensions/extension"):
        # supported 是 '|' 分隔的多值字段，如 "gl|glcore|gles2"
        if "gl" not in (ext.get("supported") or "").split("|"):
            continue
        ename = ext.get("name")
        for req in ext.findall("require"):
            for cmd in req.findall("command"):
                want(cmd.get("name"), ename)

    # ---- 解析签名
    cmds: dict[str, Command] = {}
    skipped: list[str] = []
    for name, feature in needed.items():
        node = raw_cmds.get(name)
        if node is None:
            skipped.append(f"{name} (registry 缺失定义)")
            continue
        proto = node.find("proto")
        ret_raw = strip_name("".join(proto.itertext()), name).strip()
        ret_cs = map_type(ret_raw)
        if ret_cs is None:
            skipped.append(f"{name} (返回类型 {ret_raw!r})")
            continue

        params: list[Param] = []
        bad = None
        for p in node.findall("param"):
            pn = p.find("name")
            if pn is None:
                bad = f"{name} (无名参数)"
                break
            praw = strip_name("".join(p.itertext()), pn.text)
            ct = map_type(praw)
            if ct is None:
                bad = f"{name} (参数类型 {praw.strip()!r})"
                break
            pname = pn.text
            if pname in CS_KEYWORDS:
                pname = "@" + pname
            params.append(Param(pname, ct, praw.rstrip() + " "))
        if bad:
            skipped.append(bad)
            continue

        cs_name = pascalize(name)
        cmds[name] = Command(
            name=name,
            cs_name=cs_name,
            ret_cs=ret_cs,
            ret_raw=(ret_raw + " ") if ret_raw else "void ",
            raw="",
            params=params,
            version=feature,
        )
    return cmds, skipped, sorted(needed)


def collect_constants(root: ET.Element) -> tuple[dict[str, tuple[str, str, str]], list[str]]:
    """返回 name -> (C# 字面量, 原值, C# 类型)。冲突项会报告。"""
    out: dict[str, tuple[str, str, str]] = {}
    conflicts: list[str] = []
    for enums in root.findall("enums"):
        for e in enums.findall("enum"):
            name = e.get("name")
            val = e.get("value")
            if not name or val is None:
                continue
            if not name.startswith("GL_"):
                continue
            v = val.strip()
            # 处理别名（value 指向另一个枚举名）和表达式
            if not v.startswith("0x") and not v.lstrip("-").isdigit():
                m = re.search(r"(\w+)$", v)
                if m and m.group(1) in out:
                    v = out[m.group(1)][1]
                else:
                    continue
            try:
                iv = int(v, 0)
            except ValueError:
                continue
            if iv < 0:
                lit, cs = f"-0x{-iv:X}", "int"
            elif iv <= 0xFFFFFFFF:
                lit, cs = f"0x{iv:04X}", "uint"
            else:
                lit, cs = f"0x{iv:X}", "ulong"
            if name in out:
                if out[name][1].lower() != v.lower():
                    conflicts.append(f"{name}: {out[name][1]} vs {v}")
                continue
            out[name] = (lit, v, cs)
    return out, conflicts


def emit_constants(consts, out_dir: str):
    """常量放进 GL 类，并去掉 GL_ 前缀（GL_ARRAY_BUFFER -> ARRAY_BUFFER）。
    以数字开头的名字（GL_2D 等）无法去掉前缀，保留原名。与命令字段重名时回退到全名。"""
    lines = [
        "// SPDX-License-Identifier: MIT",
        "// <auto-generated> 由 tools/glgen/gen.py 从 Khronos gl.xml 生成，请勿手改。 </auto-generated>",
        "#pragma warning disable CS1591",
        "#nullable enable",
        "namespace Byta.Bindings;",
        "",
        "public static unsafe partial class GL",
        "{",
    ]
    used: set[str] = {c for c in ("MissingCount", "BindingsRevision", "IsLoaded", "ResolveAll")}
    renamed = 0
    for name in sorted(consts):
        lit, raw, cs = consts[name]
        short = name[3:]
        if not short or not (short[0].isalpha() or short[0] == "_") or short in used:
            short = name
            renamed += 1
        used.add(short)
        lines.append(f"    public const {cs} {short} = {lit};")
    lines.append("}")
    path = os.path.join(out_dir, "Gl.Constants.g.cs")
    write(path, lines)
    return path


def emit_commands(cmds: dict[str, Command], out_dir: str):
    # ---- 命名冲突自检
    seen: dict[str, str] = {}
    clashes = []
    for c in cmds.values():
        if c.cs_name in seen:
            clashes.append(f"{c.cs_name}: {seen[c.cs_name]} vs {c.name}")
        seen[c.cs_name] = c.name
    if clashes:
        print("!! 命令名冲突: " + "; ".join(clashes[:10]), file=sys.stderr)
        sys.exit(1)

    L: list[str] = [
        "// SPDX-License-Identifier: MIT",
        "// <auto-generated> 由 tools/glgen/gen.py 从 Khronos gl.xml 生成，请勿手改。 </auto-generated>",
        "#pragma warning disable CS1591",
        "#nullable enable",
        "using System.Collections.Generic;",
        "",
        "namespace Byta.Bindings;",
        "",
        "public static unsafe partial class GL",
        "{",
        "    /// <summary>入口点表版本，与生成时使用的注册表对应。</summary>",
        '    public const string BindingsRevision = "gl.xml@khronos/main (GL 4.6 core + all extensions)";',
        "",
        "    /// <summary>加载失败（当前上下文不提供）的入口点数量。</summary>",
        "    public static int MissingCount { get; private set; }",
        "",
    ]

    # 入口点字段
    for c in sorted(cmds.values(), key=lambda x: x.cs_name):
        L.append(f"    /// <summary>{c.c_decl}  [{c.version}]</summary>")
        L.append(f"    public static {c.signature} {c.cs_name};")
    L.append("")

    # 加载器
    L.append("    private static readonly Dictionary<string, nint> s_byName = new(3000);")
    L.append("")
    L.append("    /// <summary>按 GL 原名（如 <c>glGenBuffers</c>）取入口点；当前上下文不提供时返回 0。</summary>")
    L.append("    public static nint Resolve(string name) => s_byName.TryGetValue(name, out nint p) ? p : 0;")
    L.append("")
    L.append("    static void ResolveAll(GlProcResolver resolver, List<string>? missing)")
    L.append("    {")
    L.append("        int missingCount = 0;")
    for c in sorted(cmds.values(), key=lambda x: x.cs_name):
        L.append(f'        {c.cs_name} = ({c.signature})resolver("{c.name}");')
        L.append(f'        s_byName["{c.name}"] = (nint){c.cs_name};')
        L.append(f"        if ({c.cs_name} is null) {{ missingCount++; missing?.Add(\"{c.name}\"); }}")
    L.append("        MissingCount = missingCount;")
    L.append("    }")
    L.append("}")

    path = os.path.join(out_dir, "Gl.Commands.g.cs")
    write(path, L)
    return path


# ---------------------------------------------------------------------------
# 后端抽象：WebGL2 能实现的命令子集
#
# WebGL2 == OpenGL ES 3.0（加上少量扩展）。所以这里列的是 **GLES 3.0 核心**里有的命令。
# 桌面 GL 专有的东西（DSA 的 glProgramUniform*、glBufferStorage、glMapBuffer、
# glDebugMessageControl、glDrawElementsBaseVertex/Indirect）都不在内。
# ---------------------------------------------------------------------------
WEBGL_COMMANDS: set[str] = {
    # 状态
    "glEnable", "glDisable", "glDepthFunc", "glDepthMask", "glDepthRangef",
    "glCullFace", "glFrontFace", "glBlendFunc", "glBlendFuncSeparate",
    "glBlendEquation", "glBlendEquationSeparate", "glColorMask",
    "glScissor", "glStencilFunc", "glStencilMask", "glStencilOp",
    "glViewport", "glClear", "glClearColor", "glClearDepth", "glClearStencil",
    "glLineWidth", "glPolygonOffset", "glPixelStorei",
    # 缓冲
    "glGenBuffers", "glDeleteBuffers", "glBindBuffer",
    "glBufferData", "glBufferSubData", "glCopyBufferSubData",
    "glUnmapBuffer", "glMapBufferRange",
    "glBindBufferBase", "glBindBufferRange",
    # VAO / 顶点属性
    "glGenVertexArrays", "glDeleteVertexArrays", "glBindVertexArray",
    "glEnableVertexAttribArray", "glDisableVertexAttribArray",
    "glVertexAttribPointer", "glVertexAttribIPointer", "glVertexAttribDivisor",
    # 着色器与程序
    "glCreateShader", "glShaderSource", "glCompileShader", "glGetShaderiv",
    "glGetShaderInfoLog", "glDeleteShader",
    "glCreateProgram", "glAttachShader", "glDetachShader", "glLinkProgram",
    "glValidateProgram", "glGetProgramiv", "glGetProgramInfoLog", "glDeleteProgram",
    "glUseProgram", "glGetAttribLocation", "glGetUniformLocation",
    "glBindAttribLocation", "glGetUniformBlockIndex", "glUniformBlockBinding",
    # uniform（非 DSA）
    "glUniform1f", "glUniform2f", "glUniform3f", "glUniform4f",
    "glUniform1i", "glUniform2i", "glUniform3i", "glUniform4i",
    "glUniform1ui", "glUniform2ui", "glUniform3ui", "glUniform4ui",
    "glUniform1fv", "glUniform2fv", "glUniform3fv", "glUniform4fv",
    "glUniform1iv", "glUniform2iv", "glUniform3iv", "glUniform4iv",
    "glUniform1uiv", "glUniform2uiv", "glUniform3uiv", "glUniform4uiv",
    "glUniformMatrix2fv", "glUniformMatrix3fv", "glUniformMatrix4fv",
    "glUniformMatrix2x3fv", "glUniformMatrix3x2fv",
    "glUniformMatrix2x4fv", "glUniformMatrix4x2fv",
    "glUniformMatrix3x4fv", "glUniformMatrix4x3fv",
    # 纹理
    "glGenTextures", "glDeleteTextures", "glBindTexture", "glActiveTexture",
    "glTexImage2D", "glTexSubImage2D", "glTexStorage2D",
    "glTexParameteri", "glTexParameterf", "glGenerateMipmap",
    # FBO / RBO
    "glGenFramebuffers", "glDeleteFramebuffers", "glBindFramebuffer",
    "glFramebufferTexture2D", "glFramebufferRenderbuffer",
    "glCheckFramebufferStatus", "glDrawBuffers", "glReadBuffer",
    "glGenRenderbuffers", "glDeleteRenderbuffers", "glBindRenderbuffer",
    "glRenderbufferStorage", "glReadPixels",
    # 绘制
    "glDrawArrays", "glDrawElements", "glDrawRangeElements",
    "glDrawArraysInstanced", "glDrawElementsInstanced",
    # 查询
    "glGenQueries", "glDeleteQueries", "glBeginQuery", "glEndQuery",
    "glGetQueryObjectuiv",
    # 杂项
    "glFinish", "glFlush", "glGetError", "glGetString", "glGetStringi",
    "glGetIntegerv", "glGetFloatv", "glIsEnabled",
}

# 桌面 GL 有 DSA（glProgramUniform*），WebGL 没有 —— 退化成 glUseProgram + glUniform*。
DSA_FALLBACK: dict[str, str] = {
    "glProgramUniform1f": "glUniform1f",
    "glProgramUniform2f": "glUniform2f",
    "glProgramUniform3f": "glUniform3f",
    "glProgramUniform4f": "glUniform4f",
    "glProgramUniform1i": "glUniform1i",
    "glProgramUniform1ui": "glUniform1ui",
    "glProgramUniform1fv": "glUniform1fv",
    "glProgramUniform2fv": "glUniform2fv",
    "glProgramUniform3fv": "glUniform3fv",
    "glProgramUniform4fv": "glUniform4fv",
    "glProgramUniformMatrix4fv": "glUniformMatrix4fv",
    "glProgramUniformMatrix3x2fv": "glUniformMatrix3x2fv",
}


def _param_decl(c: Command) -> str:
    return ", ".join(f"{p.cs_type} {p.name}" for p in c.params)


def _arg_list(c: Command, skip: int = 0) -> str:
    return ", ".join(p.name for p in c.params[skip:])


def emit_api(cmds: dict[str, Command], out_dir: str):
    """生成 IGlBackend 接口与 GlApi 门面。

    门面保持与 GL 完全相同的签名，所以调用点只需要把 ``GL.X(`` 换成 ``GlApi.X(``。
    原生路径下只多一次静态字段判空（分支预测几乎 100% 命中），WebGL 下转发到后端。
    """
    supported = [c for c in cmds.values() if c.name in WEBGL_COMMANDS]
    supported.sort(key=lambda c: c.cs_name)

    L: list[str] = [
        "// SPDX-License-Identifier: MIT",
        "// <auto-generated> 由 tools/glgen/gen.py 从 Khronos gl.xml 生成，请勿手改。 </auto-generated>",
        "#pragma warning disable CS1591",
        "#nullable enable",
        "using System.Runtime.CompilerServices;",
        "using Byta.Bindings;",
        "",
        "namespace Byta;",
        "",
        "/// <summary>",
        "/// 可替换的 GL 实现。当前唯一的非原生实现是 WebGL2（浏览器里通过 JS 互操作调用）。",
        "/// <para>",
        "/// 签名与 <c>GL</c> 的入口点表完全一致（含裸指针）—— 这样 <see cref=\"GlApi\"/> 的转发",
        "/// 是零转换的；实现方自己把指针包成 <c>Span</c> 交给运行时。",
        "/// </para>",
        "/// </summary>",
        "public unsafe interface IGlBackend",
        "{",
        "    /// <summary>实现名，用于诊断输出。</summary>",
        "    string Name { get; }",
        "",
        "    /// <summary>是否支持某个命令（按 GL 原名判断，如 <c>glBufferStorage</c>）。</summary>",
        "    bool Supports(string command);",
        "",
    ]
    for c in supported:
        L.append(f"    {c.ret_cs} {c.cs_name}({_param_decl(c)});")
    L.append("}")
    L.append("")

    # ---- 门面
    L += [
        "/// <summary>",
        "/// GL 调用的统一入口：有 <see cref=\"IGlBackend\"/> 时转发给它，否则直连原生函数指针。",
        "/// <para>",
        "/// 原生路径下每次调用只多一次可预测的分支，实测开销在噪声级别；",
        "/// 换来的是同一份渲染代码既能在本机跑原生 GL，也能在浏览器里跑 WebGL2。",
        "/// </para>",
        "/// </summary>",
        "public static unsafe class GlApi",
        "{",
        "    /// <summary>当前活动后端；<c>null</c> 表示走原生函数指针。</summary>",
        "    public static IGlBackend? Backend { get; private set; }",
        "",
        "    public static bool IsBackendActive => Backend is not null;",
        "",
        "    /// <summary>切换渲染后端。<paramref name=\"backend\"/> 为 <c>null</c> 时回到原生入口点表。</summary>",
        "    public static void UseBackend(IGlBackend? backend)",
        "    {",
        "        Backend = backend;",
        "        GlState.Invalidate();",
        "    }",
        "",
        "    /// <summary>按 GL 原名查询命令是否可用（原生看入口点表，后端看它自己的支持列表）。</summary>",
        "    public static bool IsAvailable(string command)",
        "    {",
        "        if (Backend is { } backend)",
        "            return backend.Supports(command);",
        "        return GL.Resolve(command) != 0;",
        "    }",
        "",
    ]

    for c in sorted(cmds.values(), key=lambda x: x.cs_name):
        args = _arg_list(c)
        decl = _param_decl(c)
        is_supported = c.name in WEBGL_COMMANDS
        dsa_target = DSA_FALLBACK.get(c.name)

        L.append(f"    public static bool Has{c.cs_name} => Backend is {{ }} b ? b.Supports(\"{c.name}\") : GL.{c.cs_name} is not null;")
        L.append("")

        if is_supported:
            L.append(f"    [MethodImpl(MethodImplOptions.AggressiveInlining)]")
            L.append(f"    public static unsafe {c.ret_cs} {c.cs_name}({decl})")
            L.append("    {")
            if c.ret_cs == "void":
                L.append(f"        if (Backend is {{ }} backend) {{ backend.{c.cs_name}({args}); return; }}")
                L.append(f"        GL.{c.cs_name}({args});")
            else:
                L.append(f"        if (Backend is {{ }} backend) return backend.{c.cs_name}({args});")
                L.append(f"        return GL.{c.cs_name}({args});")
            L.append("    }")
        elif dsa_target:
            target = cmds[dsa_target]
            # 门面签名保留 program 参数；后端只收到 location 之后的部分
            tail_decl = ", ".join(f"{p.cs_type} {p.name}" for p in c.params[1:])
            tail_args = ", ".join(p.name for p in c.params[1:])
            decl_tail = f", {tail_decl}" if tail_decl else ""
            args_tail = f", {tail_args}" if tail_args else ""
            L.append(f"    [MethodImpl(MethodImplOptions.AggressiveInlining)]")
            L.append(f"    public static unsafe {c.ret_cs} {c.cs_name}({decl})")
            L.append("    {")
            L.append("        if (Backend is not null)")
            L.append("        {")
            L.append(f"            // WebGL 没有 DSA：先切程序（走状态缓存，保持前后端一致），再用非 DSA 的 uniform 调用")
            L.append(f"            GlState.UseProgram({c.params[0].name});")
            L.append(f"            Backend.{target.cs_name}({args_tail.lstrip(', ')});")
            L.append("            return;")
            L.append("        }")
            L.append(f"        GL.{c.cs_name}({args});")
            L.append("    }")
        else:
            L.append(f"    /// <summary>该命令在 WebGL 后端上不可用（GLES 3.0 没有）。</summary>")
            L.append(f"    public static unsafe {c.ret_cs} {c.cs_name}({decl})")
            L.append("    {")
            L.append("        if (Backend is { } backend)")
            L.append(f"            throw new GlNotSupportedException(\"{c.name}\", $\"实现 {{backend.Name}} 不支持该命令（桌面 GL 专有）\");")
            ret = "" if c.ret_cs == "void" else "return "
            L.append(f"        {ret}GL.{c.cs_name}({args});")
            L.append("    }")
        L.append("")

    L.append("}")
    write(os.path.join(out_dir, "Gl.Api.g.cs"), L)


def write(path: str, lines: list[str]):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"  -> {path} ({len(lines)} 行)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--xml", default="registry/gl.xml")
    ap.add_argument("--out", default="src/Byta.Bindings/Generated")
    ap.add_argument("--out-api", default="src/Byta/Generated")
    ap.add_argument("--api-only", action="store_true", help="只重新生成门面，不动绑定")
    args = ap.parse_args()

    root = ET.parse(args.xml).getroot()
    cmds, skipped, needed = collect_commands(root)
    consts, conflicts = collect_constants(root)

    print(f"命令: {len(cmds)} 条（注册表引用 {len(needed)} 条，跳过 {len(skipped)} 条）")
    print(f"常量: {len(consts)} 条")
    print(f"WebGL2 可覆盖: {len([c for c in cmds.values() if c.name in WEBGL_COMMANDS])} 条")
    if skipped:
        print("跳过示例: " + ", ".join(s.split(" ")[0] for s in skipped[:8]))
    if conflicts:
        print(f"常量值冲突 {len(conflicts)} 处（保留首个定义）")

    os.makedirs(args.out, exist_ok=True)
    if not args.api_only:
        emit_constants(consts, args.out)
        emit_commands(cmds, args.out)
    os.makedirs(args.out_api, exist_ok=True)
    emit_api(cmds, args.out_api)
    print("完成。")


if __name__ == "__main__":
    main()
