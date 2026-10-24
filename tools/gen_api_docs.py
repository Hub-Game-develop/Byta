#!/usr/bin/env python3
"""从 Byta 的 C# 源码生成 API 文档页（单文件 HTML，无依赖）。

用法：
    python3 tools/gen_api_docs.py

产物：
    docs/api/index.html —— 部署到 GitHub Pages 的 /Byta/api/

设计上刻意不引 DocFX 之类的工具链：文档内容完全来自源码里的 XML 注释，
生成器只做"扫描 + 排版"，所以不会出现"文档和代码不一致"的漂移。
"""

from __future__ import annotations

import html
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE_DIR = ROOT / "src" / "Byta"
OUTPUT = ROOT / "docs" / "api" / "index.html"

TYPE_PATTERN = re.compile(
    r"^(?P<indent>\s*)public\s+(?:(?:sealed|readonly|abstract|static|unsafe|partial|ref)\s+)*"
    r"(?P<kind>class|struct|interface|enum)\s+(?P<name>\w+)"
)
MEMBER_PATTERN = re.compile(
    r"^\s{4}public\s+(?:(?:static|readonly|unsafe|async|const|override|virtual|new|extern)\s+)*"
    r"(?P<type>[\w<>\[\],\.\*\?\s]+?)\s+(?P<name>\w+)\s*(?P<tail>[\(\{;=])"
)
SUMMARY_END = "</summary>"


def strip_xml(text: str) -> str:
    """把 <summary> 里的 XML 标签去掉，保留可读文字。"""
    text = re.sub(r"<see cref=\"([^\"]+)\"\s*/>", r"\1", text)
    text = re.sub(r"<c>([^<]*)</c>", r"\1", text)
    text = re.sub(r"<b>([^<]*)</b>", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"\s+", " ", text)
    return text.strip()


def collect_fields(body: str) -> str:
    """把 <param>/<returns> 也拼进摘要，让签名更有信息量。"""
    extras = []
    for tag in ("param", "returns"):
        for match in re.finditer(rf"<{tag}[^>]*>(.*?)</{tag}>", body, re.S):
            value = strip_xml(match.group(1))
            if value:
                extras.append(value)
    return " ".join(extras)


def parse_file(path: pathlib.Path) -> dict:
    lines = path.read_text(encoding="utf-8").splitlines()

    doc_buffer: list[str] = []
    in_summary = False
    summary_parts: list[str] = []

    current_type = None
    types: list[dict] = []

    for index, line in enumerate(lines):
        stripped = line.strip()

        # 收集 XML 注释
        if stripped.startswith("///"):
            content = stripped[3:].strip()
            if "<summary>" in content:
                in_summary = True
                content = content.split("<summary>", 1)[1]
            if in_summary:
                if SUMMARY_END in content:
                    summary_parts.append(content.split(SUMMARY_END)[0])
                    in_summary = False
                    doc_buffer = [" ".join(summary_parts)]
                    summary_parts = []
                else:
                    summary_parts.append(content)
            elif content.startswith("<param") or content.startswith("<returns"):
                doc_buffer.append(content)
            elif content and not in_summary and not content.startswith("<"):
                doc_buffer.append(content)
            continue

        type_match = TYPE_PATTERN.search(line)
        if type_match:
            current_type = {
                "name": type_match.group("name"),
                "kind": type_match.group("kind"),
                "summary": strip_xml(" ".join(doc_buffer)),
                "members": [],
                "file": path.name,
            }
            types.append(current_type)
            doc_buffer = []
            continue

        if current_type and stripped.startswith("public"):
            member = MEMBER_PATTERN.match(line)
            if member:
                signature = stripped.rstrip("{;=").rstrip()
                summary = strip_xml(" ".join(doc_buffer))
                extras = collect_fields(" ".join(doc_buffer))
                combined = " ".join(part for part in (summary, extras) if part)
                current_type["members"].append(
                    {"signature": signature, "summary": combined}
                )
            doc_buffer = []
            continue

        if stripped and not stripped.startswith(("//", "[", "#", "using", "namespace", "}", "{", ")", "]")):
            # 非注释的代码行会打断文档注释的归属
            if not stripped.startswith("///"):
                doc_buffer = []

    return {"file": path.name, "types": types}


HIGHLIGHTS = [
    ("BytaRuntime.Initialize()", "一行拿到本机能给出的最高 GL 版本：内置实现 → BYTA_GL_DRIVER → 系统实现，从 4.6 逐级往下试。",
     "var session = BytaRuntime.Initialize();"),
    ("GlBuffer", "值句柄缓冲。Span 直传，不产生托管分配；Allocate / Upload / Orphan 覆盖静态与流式两种用法。",
     "using var vbo = GlBuffer.Create();\nvbo.Allocate(vertices);"),
    ("GlProgram", "着色器编译与链接。失败直接抛异常，消息里带完整驱动日志，不用自己去 glGetShaderInfoLog。",
     "using var program = GlProgram.FromSource(vs, fs);"),
    ("GlState", "线程局部状态缓存。绑定、视口、开关都走它，冗余切换在到达驱动之前就被吃掉。",
     "GlState.BindBuffer(BufferTarget.ArrayBuffer, id);"),
    ("GlApi", "后端门面：有 IGlBackend 时转发给它，否则直连原生函数指针 —— 同一份代码跑原生与 WebGL2。",
     "GlApi.UseBackend(new WebGlBackend());"),
]


def render_highlights() -> str:
    cards = []
    for name, description, snippet in HIGHLIGHTS:
        cards.append(
            '<div class="hl">'
            f'<h3>{html.escape(name)}</h3>'
            f'<p>{html.escape(description)}</p>'
            f'<pre>{html.escape(snippet)}</pre>'
            "</div>"
        )
    return "".join(cards)


def render(modules: list[dict]) -> str:
    sections = []
    nav = []

    for module in modules:
        for type_info in module["types"]:
            anchor = f"{type_info['name']}-{module['file'].replace('.', '-')}"
            nav.append(
                f'<a href="#{anchor}"><span class="kind">{type_info["kind"]}</span>'
                f'<span class="name">{html.escape(type_info["name"])}</span></a>'
            )

            rows = []
            for member in type_info["members"]:
                rows.append(
                    '<div class="member">'
                    f'<code>{html.escape(member["signature"])}</code>'
                    f'<p>{html.escape(member["summary"]) or "<em>（源码里没有注释）</em>"}</p>'
                    "</div>"
                )

            sections.append(
                f'<section id="{anchor}">'
                f'<h2><span class="kind">{type_info["kind"]}</span>{html.escape(type_info["name"])}</h2>'
                f'<p class="type-summary">{html.escape(type_info["summary"]) or "<em>（源码里没有注释）</em>"}</p>'
                f'<p class="source">{html.escape(module["file"])}</p>'
                f'<div class="members">{"".join(rows) or "<p class=empty>（没有公开成员）</p>"}</div>'
                "</section>"
            )

    total_types = sum(len(m["types"]) for m in modules)
    total_members = sum(len(t["members"]) for m in modules for t in m["types"])

    highlights = render_highlights()

    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Byta API 参考</title>
<style>
  :root {{
    color-scheme: dark;
    --bg: #0b0d13;
    --panel: #10141f;
    --line: #1d2434;
    --text: #e6e8ee;
    --muted: #8b93a7;
    --accent: #2b6cff;
    --accent-soft: #22d3ee;
    --mono: "SF Mono", "Menlo", ui-monospace, monospace;
  }}
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0; background: var(--bg); color: var(--text);
    font: 15px/1.7 -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif;
  }}
  header {{
    padding: 56px 32px 32px; border-bottom: 1px solid var(--line);
    background: radial-gradient(1200px 400px at 20% -10%, #16305e 0%, transparent 70%);
  }}
  header h1 {{ margin: 0 0 8px; font-size: 34px; letter-spacing: -0.5px; }}
  header p {{ margin: 0; color: var(--muted); max-width: 760px; }}
  header .stats {{ margin-top: 18px; font-size: 13px; color: var(--muted); font-variant-numeric: tabular-nums; }}
  .highlights {{
    display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
    gap: 12px; margin-top: 26px;
  }}
  .hl {{
    background: #0f1626; border: 1px solid var(--line); border-radius: 12px; padding: 14px 16px;
  }}
  .hl h3 {{ margin: 0 0 6px; font-family: var(--mono); font-size: 14px; color: #9ecbff; }}
  .hl p {{ margin: 0 0 10px; font-size: 13px; color: var(--muted); }}
  .hl pre {{
    margin: 0; background: #0a0f1a; border-radius: 8px; padding: 9px 11px;
    font-family: var(--mono); font-size: 11.5px; color: #7f8ea8; overflow-x: auto;
  }}
  .layout {{ display: grid; grid-template-columns: 280px 1fr; gap: 0; align-items: start; }}
  nav {{
    position: sticky; top: 0; max-height: 100vh; overflow: auto;
    padding: 24px 16px 40px; border-right: 1px solid var(--line);
  }}
  nav a {{
    display: flex; gap: 8px; align-items: baseline;
    padding: 6px 10px; border-radius: 7px; text-decoration: none; color: var(--text);
    font-size: 13.5px;
  }}
  nav a:hover {{ background: #161c2b; }}
  nav .kind {{
    font-size: 10px; text-transform: uppercase; letter-spacing: .06em;
    color: var(--accent-soft); min-width: 38px;
  }}
  nav .name {{ font-family: var(--mono); }}
  main {{ padding: 24px 40px 120px; max-width: 980px; }}
  section {{ padding: 26px 0 30px; border-bottom: 1px solid var(--line); }}
  section h2 {{
    margin: 0 0 6px; font-size: 21px; font-family: var(--mono); font-weight: 600;
    display: flex; align-items: baseline; gap: 10px;
  }}
  section h2 .kind, .type-summary + .source + .members + .kind {{ }}
  section h2 .kind {{
    font-family: -apple-system, sans-serif; font-size: 10px; letter-spacing: .08em;
    text-transform: uppercase; color: var(--accent-soft); font-weight: 500;
  }}
  .type-summary {{ margin: 0 0 4px; color: #c3cadb; }}
  .source {{ margin: 0 0 16px; font-family: var(--mono); font-size: 11.5px; color: #5d6780; }}
  .member {{
    background: var(--panel); border: 1px solid var(--line); border-radius: 10px;
    padding: 12px 15px; margin-bottom: 9px;
  }}
  .member code {{
    font-family: var(--mono); font-size: 13px; color: #9ecbff; word-break: break-word;
  }}
  .member p {{ margin: 6px 0 0; font-size: 13.5px; color: var(--muted); }}
  .member em, .type-summary em {{ color: #5d6780; }}
  .empty {{ color: var(--muted); }}
  footer {{ padding: 40px; color: #4d5670; font-size: 12.5px; text-align: center; }}
  @media (max-width: 860px) {{
    .layout {{ grid-template-columns: 1fr; }}
    nav {{ position: static; max-height: none; border-right: 0; border-bottom: 1px solid var(--line); }}
    main {{ padding: 20px; }}
  }}
</style>
</head>
<body>
<header>
  <h1>Byta API 参考</h1>
  <p>本页由 <code>tools/gen_api_docs.py</code> 扫描源码里的 XML 注释生成 —— 文档内容就是代码里的注释，
     不会和实现漂移。零分配热路径、值句柄对象层、可插拔后端（原生 GL / WebGL2）。</p>
  <div class="stats">{total_types} 个公开类型 · {total_members} 个公开成员</div>
  <div class="highlights">{highlights}</div>
</header>
<div class="layout">
  <nav>{"".join(nav)}</nav>
  <main>{"".join(sections)}</main>
</div>
<footer>Byta · MIT · github.com/Hub-Game-develop/Byta</footer>
</body>
</html>
"""


def main() -> int:
    files = sorted(p for p in SOURCE_DIR.rglob("*.cs") if "Generated" not in p.parts)
    modules = [parse_file(p) for p in files]
    modules = [m for m in modules if m["types"]]

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(render(modules), encoding="utf-8")

    types = sum(len(m["types"]) for m in modules)
    members = sum(len(t["members"]) for m in modules for t in m["types"])
    print(f"{OUTPUT}: {len(modules)} 个文件 / {types} 个类型 / {members} 个成员")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
