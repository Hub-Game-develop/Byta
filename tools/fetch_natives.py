#!/usr/bin/env python3
"""把 Homebrew bottle 里的 Mesa(EGL+zink) 依赖闭包拼成一份"不与安装路径耦合"的原生目录。

为什么不用 brew install：
  1. 这台机器上 brew 的安装沙箱（sandbox-exec）跑不起来；
  2. bottle 里的 dylib 引用的是 @@HOMEBREW_PREFIX@@ 这样的占位符，装完才被改写，
     我们本来就要自己改写 install name；
  3. 这个脚本能在 CI 里复现同样的结果，不依赖任何人的本机状态。

用法:
    # 先用 brew fetch 把 bottle 下到 ~/Library/Caches/Homebrew/downloads
    brew fetch --force --bottle-tag=arm64_golden_gate mesa llvm molten-vk \\
        zstd spirv-tools libx11 libxcb libxext libxfixes libpng

    python3 tools/fetch_natives.py --rid osx-arm64 --dest src/Byta/runtimes/osx-arm64/native
"""

from __future__ import annotations

import argparse
import pathlib
import shutil
import subprocess
import sys
import tarfile

CACHE = pathlib.Path.home() / "Library/Caches/Homebrew/downloads"
STAGING = pathlib.Path("/tmp/byta-natives-staging")

# 需要一起打包的核心库（其余依赖靠 otool 递归发现）
ROOTS = ["mesa", "vulkan-loader"]
ENTRY_LIBRARIES = ["libEGL.1.dylib", "libEGL.dylib"]

PLACEHOLDER_PREFIX = "@@HOMEBREW_PREFIX@@/opt/"
PLACEHOLDER_CELLAR = "@@HOMEBREW_CELLAR@@/"


def find_bottle(formula: str) -> pathlib.Path:
    hits = sorted(CACHE.glob(f"*--{formula}--*.bottle*.tar.gz"))
    if not hits:
        sys.exit(f"缺少 {formula} 的 bottle：先跑 brew fetch --force --bottle-tag=arm64_golden_gate {formula}")
    return hits[-1]


_extracted: dict[str, pathlib.Path] = {}


def formula_root(formula: str) -> pathlib.Path:
    if formula in _extracted:
        return _extracted[formula]

    dest = STAGING / formula
    if not dest.exists():
        dest.mkdir(parents=True)
        bottle = find_bottle(formula)
        print(f"  解包 {bottle.name}")
        with tarfile.open(bottle) as tf:
            tf.extractall(dest)

    # bottle 内部布局：<formula>/<version>/lib
    candidates = [p.parent for p in dest.glob("*/*/lib")]
    if not candidates:
        sys.exit(f"{formula} 的 bottle 结构不符合预期（找不到 <formula>/<version>/lib）")
    root = candidates[0]
    _extracted[formula] = root
    return root


def lib_dir(formula: str) -> pathlib.Path:
    return formula_root(formula) / "lib"


def otool_deps(path: pathlib.Path) -> list[str]:
    out = subprocess.run(["otool", "-L", str(path)], capture_output=True, text=True, check=True).stdout
    deps = []
    for line in out.splitlines()[1:]:
        dep = line.strip().split(" ")[0]
        if dep.startswith(("/usr/lib", "/System")):
            continue
        deps.append(dep)
    return deps


def resolve(dep: str) -> tuple[pathlib.Path, str] | None:
    """把 bottle 里的占位符路径解析成 (真实文件, 落盘文件名)。

    bottle 里常常只有版本化的真实文件（libxcb.1.1.0.dylib），而引用名是
    libxcb.1.dylib（Homebrew 安装时才建这个软链）。所以落盘名一律用**引用名**，
    这样重写依赖时不用再猜。
    """
    if dep.startswith(PLACEHOLDER_PREFIX):
        rest = dep[len(PLACEHOLDER_PREFIX):]          # <formula>/lib/<name>
        formula, _, tail = rest.partition("/")
        return _materialize(lib_dir(formula), tail.split("/")[-1])

    if dep.startswith(PLACEHOLDER_CELLAR):
        rest = dep[len(PLACEHOLDER_CELLAR):]          # <formula>/<version>/lib/<name>
        formula = rest.split("/")[0]
        return _materialize(lib_dir(formula), rest.split("/")[-1])

    if dep.startswith(("/opt/homebrew/", "/usr/local/")):
        candidate = pathlib.Path(dep)
        return (candidate, candidate.name) if candidate.exists() else None

    if dep.startswith("@rpath/"):
        # 形如 @rpath/libvulkan.1.dylib —— 交给后面按名字在闭包里找
        return _materialize(lib_dir("vulkan-loader"), dep[len("@rpath/"):])

    return None


def _materialize(directory: pathlib.Path, name: str) -> tuple[pathlib.Path, str] | None:
    direct = directory / name
    if direct.exists():
        return direct, name

    # 版本化文件名兜底：libxcb.1.dylib -> libxcb.1.1.0.dylib
    if name.endswith(".dylib"):
        stem = name[: -len(".dylib")]
        for candidate in sorted(directory.glob(stem + "*.dylib")):
            return candidate, name

    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", default="osx-arm64")
    parser.add_argument("--dest", default="src/Byta/runtimes/osx-arm64/native")
    parser.add_argument("--keep-staging", action="store_true")
    args = parser.parse_args()

    if STAGING.exists() and not args.keep_staging:
        shutil.rmtree(STAGING)
    STAGING.mkdir(parents=True, exist_ok=True)

    dest = pathlib.Path(args.dest).resolve()
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)

    # ---- 1. 从 EGL 出发收集闭包
    print("==> 解析依赖闭包")
    for formula in ROOTS:
        formula_root(formula)  # 触发解包

    queue: list[tuple[pathlib.Path, str]] = []
    mesa_lib = lib_dir("mesa")
    for entry in ENTRY_LIBRARIES:
        candidate = mesa_lib / entry
        if candidate.exists():
            queue.append((candidate.resolve(), entry))
            break
    else:
        sys.exit("mesa 的 libEGL 没找到")

    # 驱动模块（zink_dri / swrast_dri 等）不是被链接的，而是运行时 dlopen，要单独加进来。
    # Mesa 按 LIBGL_DRIVERS_PATH 找它们，所以原样保留 dri/ 子目录。
    for driver in (mesa_lib / "dri").glob("*_dri.dylib"):
        queue.append((driver.resolve(), f"dri/{driver.name}"))

    # zink 通过 @rpath/libvulkan.1.dylib 加载 Vulkan loader，显式带上
    vulkan_lib = lib_dir("vulkan-loader")
    for candidate in sorted(vulkan_lib.glob("libvulkan*.dylib")):
        queue.append((candidate.resolve(), candidate.name))

    copied: dict[str, pathlib.Path] = {}   # 落盘名 -> 源文件
    unresolved: set[str] = set()

    while queue:
        source, dest_name = queue.pop(0)
        if dest_name in copied:
            continue
        copied[dest_name] = source

        for dep in otool_deps(source):
            resolved = resolve(dep)
            if resolved is None:
                unresolved.add(dep)
                continue
            file, name = resolved
            if file.exists() and name not in copied:
                queue.append((file.resolve(), name))

    print(f"==> 闭包共 {len(copied)} 个库")
    if unresolved:
        print(f"!! {len(unresolved)} 个依赖没能解析（可能不影响 EGL）：")
        for dep in sorted(unresolved)[:6]:
            print(f"   {dep}")

    # ---- 2. 拷贝 + 改写 install name
    print("==> 拷贝并改写 install name")
    for name, source in copied.items():
        target = dest / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source.resolve(), target)

    for name in copied:
        target = dest / name
        subprocess.run(["install_name_tool", "-id", f"@loader_path/{name}", str(target)],
                       capture_output=True)

        for dep in otool_deps(target):
            dep_name = dep.split("/")[-1]
            # 驱动模块在 dri/ 子目录里，链接时仍写成 @loader_path/<名字>
            if dep_name in {pathlib.PurePosixPath(k).name for k in copied}:
                subprocess.run(["install_name_tool", "-change", dep, f"@loader_path/{dep_name}", str(target)],
                               capture_output=True)

        # 让残留的 @rpath/xxx 也能在本目录找到
        subprocess.run(["install_name_tool", "-add_rpath", "@loader_path", str(target)], capture_output=True)

        # arm64 上改过的 Mach-O 必须重签，否则 dyld 直接拒绝
        subprocess.run(["codesign", "--force", "--sign", "-", "--timestamp=none", str(target)],
                       capture_output=True)

    # ---- 3. MoltenVK ICD（zink 需要）
    print("==> 准备 Vulkan ICD")
    icd_dir = dest / "vulkan" / "icd.d"
    icd_dir.mkdir(parents=True, exist_ok=True)

    molten = lib_dir("molten-vk") / "libMoltenVK.dylib"
    if molten.exists():
        shutil.copy2(molten, dest / "libMoltenVK.dylib")
        subprocess.run(["install_name_tool", "-id", "@loader_path/libMoltenVK.dylib", str(dest / "libMoltenVK.dylib")],
                       capture_output=True)
        for dep in otool_deps(dest / "libMoltenVK.dylib"):
            dep_name = dep.split("/")[-1]
            if (dest / dep_name).exists():
                subprocess.run(["install_name_tool", "-change", dep, f"@loader_path/{dep_name}", str(dest / "libMoltenVK.dylib")],
                               capture_output=True)
        subprocess.run(["codesign", "--force", "--sign", "-", "--timestamp=none", str(dest / "libMoltenVK.dylib")],
                       capture_output=True)

        (icd_dir / "MoltenVK_icd.json").write_text(
            '{\n'
            '  "file_format_version": "1.0.0",\n'
            '  "ICD": {\n'
            '    "library_path": "../../libMoltenVK.dylib",\n'
            '    "api_version": "1.3.0",\n'
            '    "is_portability_driver": false\n'
            '  }\n'
            '}\n')
        print("   MoltenVK ICD 已生成")
    else:
        print("   !! 没有 libMoltenVK，zink 不可用（llvmpipe 仍可）")

    # ---- 4. 报告
    total = sum(f.stat().st_size for f in dest.rglob("*") if f.is_file())
    print()
    print(f"==> 输出目录：{dest}")
    print(f"    文件数：{len(list(dest.rglob('*')))}  总体积：{total / 1048576:.1f} MB")
    biggest = sorted((f for f in dest.iterdir() if f.is_file()), key=lambda f: -f.stat().st_size)[:5]
    for f in biggest:
        print(f"    {f.stat().st_size / 1048576:8.1f} MB  {f.name}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
