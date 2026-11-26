#!/usr/bin/env bash
# Linux counterpart of build-native.ps1: builds CaptionProbe only (ShellDialogs is Windows-only).
# Usage: scripts/build-native.sh [libaribcaption source dir]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
commit="37dde0f32652c32aa98bf92278944d1cfb2e7354"
arib="${1:-$root/.deps/libaribcaption}"
if [ ! -d "$arib/.git" ]; then git clone --no-checkout https://github.com/xqq/libaribcaption.git "$arib"; fi
git -C "$arib" fetch origin "$commit" --depth 1
git -C "$arib" checkout --detach "$commit"
if [ "$(git -C "$arib" rev-parse HEAD)" != "$commit" ]; then echo "Unexpected libaribcaption revision" >&2; exit 1; fi
cmake_cmd="$(command -v cmake || true)"
if [ -z "$cmake_cmd" ] && [ -x "$HOME/tools/cmake/bin/cmake" ]; then cmake_cmd="$HOME/tools/cmake/bin/cmake"; fi
if [ -z "$cmake_cmd" ]; then echo "cmake not found" >&2; exit 1; fi
"$cmake_cmd" -S "$root/native" -B "$root/build/native" -DCMAKE_BUILD_TYPE=Release "-DARIBCAPTION_SOURCE=$arib"
"$cmake_cmd" --build "$root/build/native" --target CaptionProbe --parallel 4
mkdir -p "$root/build/native/Release"
cp "$root/build/native/CaptionProbe" "$root/build/native/Release/CaptionProbe"
echo "Built: $root/build/native/Release/CaptionProbe"
