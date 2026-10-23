#!/usr/bin/env bash
# Byta 构建脚本
#
# 为什么不用 `dotnet build Byta.slnx`：
#   1. 这台机器上 arm64 的 CodeNet SDK 构建 .slnx 会 SIGSEGV(139) / MSB4166；
#      逐个项目构建完全正常（x64 SDK 构建 .slnx 也正常）。
#   2. macOS 27 上有 EXC_GUARD / Wall#2 的老问题：官方 arm64 .NET 建 CoreCLR 直接
#      0x8007000C 必失败；CodeNet 的 signalfix 版平时正常，但进程密集创建时（MSBuild
#      派生工作进程）会概率性失败 0x8007000E / 0x80070054F。
#      所以这里：逐个项目构建 + 失败重试 + 自动换一个能用的 SDK。
#
# 用法:
#   ./build.sh                 # Release
#   ./build.sh Debug
#   ./build.sh Release --test  # 构建后跑测试（用 arm64 运行时，才能拿到内置 4.6）
#   DOTNET=/path/to/dotnet ./build.sh
set -uo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
CONFIG="${1:-Release}"
RUN_TESTS="${2:-}"

PROJECTS=(
  "src/Byta.Bindings/Byta.Bindings.csproj"
  "src/Byta/Byta.csproj"
  "samples/Byta.Sample/Byta.Sample.csproj"
  "tests/Byta.Tests/Byta.Tests.csproj"
)

# 构建 SDK 候选：先用户指定的 / PATH 里的，再本机已知的备用 SDK
SDK_CANDIDATES=()
[ -n "${DOTNET:-}" ] && SDK_CANDIDATES+=("$DOTNET")
SDK_CANDIDATES+=("dotnet")
for alt in "$HOME/dotnet11x64/dotnet" "$HOME/dotnet-arm64/dotnet"; do
  [ -x "$alt" ] && SDK_CANDIDATES+=("$alt")
done

has_coreclr_failure() { grep -q "Failed to create CoreCLR" /tmp/byta-build.log 2>/dev/null; }

# 单个项目：最多试 4 次，只对 CoreCLR 建不起来的瞬时失败重试
build_project() {
  local sdk="$1" project="$2" attempt
  for attempt in 1 2 3 4; do
    if "$sdk" build "$ROOT/$project" -c "$CONFIG" -v q --nologo > /tmp/byta-build.log 2>&1; then
      [ "$attempt" -gt 1 ] && echo -n " [第 $attempt 次]"
      return 0
    fi
    has_coreclr_failure || return 1
    sleep 1
  done
  return 1
}

echo "配置 : $CONFIG"
echo

BUILD_SDK=""
for sdk in "${SDK_CANDIDATES[@]}"; do
  version="$("$sdk" --version 2>/dev/null | tail -1)"
  if [ -z "$version" ]; then
    echo "==> 跳过 SDK：${sdk}（连 --version 都起不来，CoreCLR 创建失败）"
    continue
  fi

  echo "==> 尝试 SDK：${sdk}（${version}）"
  failed=""
  for project in "${PROJECTS[@]}"; do
    printf "    %-44s" "$project"
    if build_project "$sdk" "$project"; then
      echo "OK"
    else
      echo "失败"
      failed="$project"
      break
    fi
  done

  if [ -z "$failed" ]; then
    BUILD_SDK="$sdk"
    break
  fi

  if has_coreclr_failure; then
    echo "    → 这个 SDK 建不起 CoreCLR（macOS 27 EXC_GUARD/Wall#2 的已知问题），换下一个"
  else
    echo
    tail -20 /tmp/byta-build.log
    exit 1
  fi
  echo
done

if [ -z "$BUILD_SDK" ]; then
  echo
  echo "所有 SDK 都失败了。已知原因："
  echo "  - 官方 arm64 .NET 在 macOS 27 上建 CoreCLR 会报 0x8007000C（EXC_GUARD / Wall#2）"
  echo "  - CodeNet 的 signalfix 版偶发 0x8007000E / 0x80070054F，重跑或重启后可恢复"
  echo "  - 换 x64 SDK：DOTNET=~/dotnet11x64/dotnet ./build.sh"
  exit 1
fi

echo
echo "构建完成，使用 SDK：${BUILD_SDK}"

if [ "$RUN_TESTS" = "--test" ]; then
  echo
  echo "==> 运行测试"
  TEST_DLL="$ROOT/tests/Byta.Tests/bin/$CONFIG/net10.0/Byta.Tests.dll"

  # 内置实现是 arm64 原生库，用 arm64 运行时跑才有 GL 4.6；x64 进程会自动退回 Apple 4.1
  RUN_SDK="$HOME/dotnet-arm64/dotnet"
  [ -x "$RUN_SDK" ] || RUN_SDK="$BUILD_SDK"

  for attempt in 1 2 3; do
    "$RUN_SDK" "$TEST_DLL" > /tmp/byta-test.log 2>&1
    code=$?
    cat /tmp/byta-test.log
    if [ $code -eq 0 ]; then
      exit 0
    fi
    # 只有"运行时起不来"才重试；测试真的挂了就直接把退出码传出去
    if grep -q "Failed to create CoreCLR" /tmp/byta-test.log; then
      sleep 1
      continue
    fi
    exit $code
  done

  echo
  echo "arm64 运行时起不来（macOS 27 EXC_GUARD / Wall#2 那类问题），改用构建用的 SDK 跑一遍："
  echo "注意：这时是 x64 进程，用不了 arm64 的内置实现，测的是系统 4.1 路径。"
  echo
  "$BUILD_SDK" "$TEST_DLL"
  exit $?
fi
