#!/bin/sh
# Builds the Linux CM-cut toolchain (logoframe / chapter_exe / join_logo_scp /
# AviSynth+ / L-SMASH-Works) from pinned upstream sources and assembles the
# layout the app expects:
#   <out>/bin/{logoframe, chapter_exe, join_logo_scp, libavisynth.so,
#              plugins64/libLSMASHSource.so}
#   <out>/JL/JL_*.txt
# Point Settings.ChapterExe at <out>/bin/chapter_exe (logoframe can stay
# unset: the bundled copy is this build). Tool sources are fetched at build
# time and are NOT redistributed with this repository (logoframe excepted:
# its source already ships under third_party/CmLogo, GPL-3.0).
# Usage: build-cm-tools.sh [output-dir]   (default: ~/tools/cm-linux)
set -eu
OUT="${1:-$HOME/tools/cm-linux}"
SRC="${CM_TOOLS_SRC:-$HOME/cm-tools-src}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PATCHES="$REPO_ROOT/scripts/cm-tools-patches"
CMAKE="${CMAKE:-cmake}"
mkdir -p "$OUT/bin/plugins64" "$OUT/JL" "$SRC"
fetch() { # name url commit
  if [ ! -d "$SRC/$1/.git" ]; then git clone "$2" "$SRC/$1"; fi
  git -C "$SRC/$1" fetch --quiet origin "$3" 2>/dev/null || true
  git -C "$SRC/$1" checkout --quiet "$3"
}

# 1. AviSynth+ (runtime the other tools dlopen as libavisynth.so)
fetch AviSynthPlus https://github.com/AviSynth/AviSynthPlus.git 3207eaace450bc76e1766ca82c3112fce91ad84e
# Version.cmake derives the version from git describe; a shallow/pinned
# checkout has no tags, so tag the pinned commit locally like a release.
git -C "$SRC/AviSynthPlus" tag -f v3.7.5 >/dev/null 2>&1 || true
"$CMAKE" -S "$SRC/AviSynthPlus" -B "$SRC/AviSynthPlus/build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$SRC/avisynth" -DHEADERS_ONLY=OFF >/dev/null
"$CMAKE" --build "$SRC/AviSynthPlus/build" -j"$(nproc)" >/dev/null
"$CMAKE" --install "$SRC/AviSynthPlus/build" >/dev/null
cp -L "$SRC/avisynth/lib/libavisynth.so" "$OUT/bin/libavisynth.so"

# 2. logoframe (source ships in this repository: third_party/CmLogo)
rm -rf "$SRC/logoframe"; cp -r "$REPO_ROOT/third_party/CmLogo" "$SRC/logoframe"
make -C "$SRC/logoframe/src" -j"$(nproc)" WITH_DTVINDEX=no >/dev/null
cp "$SRC/logoframe/src/logoframe" "$OUT/bin/logoframe"

# 3. chapter_exe (+ patch: initialize AvsSource::env, otherwise it crashes
#    deleting a garbage script environment on Linux)
fetch chapter_exe https://github.com/rigaya/chapter_exe.git 32880d45f088e574285a101e6a49b032bb04f6ea
git -C "$SRC/chapter_exe" checkout -- src/source.h
git -C "$SRC/chapter_exe" apply "$PATCHES/chapter_exe-env-init.patch"
make -C "$SRC/chapter_exe/src" CFLAGS="-O3 -I$SRC/avisynth/include/avisynth -ffast-math -Wall -I. -std=gnu99 -fpermissive -fomit-frame-pointer -s -fno-tree-vectorize" >/dev/null
cp "$SRC/chapter_exe/src/chapter_exe" "$OUT/bin/chapter_exe"

# 4. join_logo_scp (+ JL judgement scripts)
fetch JoinLogoScp https://github.com/sogaani/JoinLogoScp.git b70215a06ee0bbac5290fe357966444eadbdace9
make -C "$SRC/JoinLogoScp/join_logo_scp/src" -j"$(nproc)" >/dev/null
cp "$SRC/JoinLogoScp/join_logo_scp/src/join_logo_scp" "$OUT/bin/join_logo_scp"
cp "$SRC/JoinLogoScp/join_logo_scp/JL/"*.txt "$OUT/JL/"

# 5. L-SMASH-Works AviSynth plugin (+ FFmpeg >= 6.2 API patch: the parser
#    split callback and AVCodec pix_fmts were removed upstream)
fetch L-SMASH-Works https://github.com/HomeOfAviSynthPlusEvolution/L-SMASH-Works.git 7e63c8400a252f4dbbeea80cc3bfec895abf12cf
git -C "$SRC/L-SMASH-Works" submodule update --init --recursive >/dev/null
git -C "$SRC/L-SMASH-Works" checkout -- common/lwlibav_video.c common/lwindex.c
git -C "$SRC/L-SMASH-Works" apply "$PATCHES/lsmash-ffmpeg9.patch"
"$CMAKE" -S "$SRC/L-SMASH-Works" -B "$SRC/L-SMASH-Works/build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DCMAKE_PREFIX_PATH="$SRC/avisynth" -DBUILD_VS_PLUGIN=OFF -DBUILD_AU2_PLUGIN=OFF -DENABLE_DAV1D=OFF -DENABLE_MFX=OFF -DENABLE_XML2=OFF -DENABLE_VPX=OFF -DZLIB_USE_STATIC_LIBS=OFF >/dev/null
"$CMAKE" --build "$SRC/L-SMASH-Works/build" -j"$(nproc)" >/dev/null
cp "$SRC/L-SMASH-Works/build/libLSMASHSource.so" "$OUT/bin/plugins64/libLSMASHSource.so"

echo "CM tools installed under $OUT"
LD_LIBRARY_PATH="$OUT/bin" "$OUT/bin/logoframe" 2>&1 | head -1 || true
ls -la "$OUT/bin" "$OUT/bin/plugins64" "$OUT/JL"
