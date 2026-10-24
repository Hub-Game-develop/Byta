#!/usr/bin/env python3
"""从 git 历史生成 Release 简介。

用法：
    python3 tools/release_notes.py v0.2.0 [--artifacts artifacts/packages] > notes.md

规则：
    * 按 Conventional Commits 的前缀分组（feat / fix / perf / docs / build / ci / refactor / chore）；
    * 找不到上一个 tag 时，把整段历史都算进首个版本；
    * ``--artifacts`` 指向含 .nupkg/.snupkg 的目录时，附上产物体积；
    * 输出是可直接作为 GitHub Release 正文的 Markdown —— 由 CI 自动调用，不手写。
"""

from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys

GROUPS = [
    ("feat", "新功能"),
    ("fix", "修复"),
    ("perf", "性能"),
    ("refactor", "重构"),
    ("docs", "文档"),
    ("build", "构建与打包"),
    ("ci", "持续集成"),
    ("test", "测试"),
    ("chore", "杂项"),
]

SKIP = {"chore"}  # 默认不把琐碎改动列进简介


def git(*args: str) -> str:
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        return ""
    return result.stdout.strip()


def previous_tag(tag: str) -> str:
    """找当前 tag 之前最近的一个 tag（按提交顺序，而非版本号排序）。"""
    out = git("describe", "--tags", "--abbrev=0", f"{tag}^")
    return out if out and out != tag else ""


def commit_subjects(rev_range: str) -> list[str]:
    out = git("log", rev_range, "--pretty=format:%s%x09%h")
    return [line for line in out.splitlines() if line.strip()]


def parse(subject_line: str) -> tuple[str, str, str, str] | None:
    subject, _, short = subject_line.partition("\t")
    match = re.match(r"^(?P<type>[a-zA-Z]+)(?:\((?P<scope>[^)]*)\))?(?P<breaking>!)?:\s*(?P<text>.+)$", subject)
    if not match:
        return None
    return (
        match.group("type").lower(),
        match.group("scope") or "",
        match.group("text").strip(),
        "breaking" if match.group("breaking") else short,
    )


def collect(rev_range: str) -> tuple[dict[str, list[str]], list[str], list[str]]:
    grouped: dict[str, list[str]] = {key: [] for key, _ in GROUPS}
    breaking: list[str] = []
    unclassified: list[str] = []

    for line in commit_subjects(rev_range):
        parsed = parse(line)
        if parsed is None:
            unclassified.append(line.split("\t")[0])
            continue

        kind, scope, text, marker = parsed
        entry = f"{text}" + (f" — `{scope}`" if scope else "")
        grouped.setdefault(kind, []).append(entry)
        if marker == "breaking":
            breaking.append(entry)

    return grouped, breaking, unclassified


def artifact_sizes(directory: pathlib.Path | None) -> list[str]:
    if directory is None or not directory.is_dir():
        return []
    lines = []
    for path in sorted(directory.iterdir()):
        if path.suffix not in {".nupkg", ".snupkg"}:
            continue
        size = path.stat().st_size
        human = f"{size / 1048576:.1f} MB" if size >= 1048576 else f"{size / 1024:.0f} KB"
        lines.append(f"| `{path.name}` | {human} |")
    return lines


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("tag", nargs="?", default="", help="要生成简介的 tag，缺省取最近的")
    parser.add_argument("--artifacts", default="", help="含 nupkg 的目录")
    parser.add_argument("--all", action="store_true", help="连 chore 一起列出来")
    args = parser.parse_args()

    tag = args.tag or git("describe", "--tags", "--abbrev=0")
    if not tag:
        print("找不到任何 tag", file=sys.stderr)
        return 1

    base = previous_tag(tag)
    rev_range = f"{base}..{tag}" if base else tag
    grouped, breaking, unclassified = collect(rev_range)
    count = len(commit_subjects(rev_range))

    skip = set() if args.all else SKIP

    out: list[str] = []
    out.append(f"本次发布包含 **{count}** 个提交" + (f"，相对 `{base}`。" if base else "（首个版本）。"))
    out.append("")

    if breaking:
        out.append("## 破坏性变更")
        out.append("")
        for item in breaking:
            out.append(f"- {item}")
        out.append("")

    for key, title in GROUPS:
        items = grouped.get(key) or []
        if not items or key in skip:
            continue
        out.append(f"## {title}")
        out.append("")
        for item in items:
            out.append(f"- {item}")
        out.append("")

    if unclassified and not skip:
        out.append("## 其他")
        out.append("")
        for item in unclassified:
            out.append(f"- {item}")
        out.append("")

    rows = artifact_sizes(pathlib.Path(args.artifacts) if args.artifacts else None)
    if rows:
        out.append("## 产物")
        out.append("")
        out.append("| 包 | 体积 |")
        out.append("| --- | --- |")
        out.extend(rows)
        out.append("")

    out.append("---")
    out.append("")
    out.append("> 本说明由 `tools/release_notes.py` 依据 git 历史自动生成，"
               "内容随提交信息变化 —— 提交时请遵守 Conventional Commits。")

    print("\n".join(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
