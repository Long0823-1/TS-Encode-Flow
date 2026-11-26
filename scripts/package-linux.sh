#!/usr/bin/env bash
# TS Encode Flow の Linux 配布パッケージを作成する。
# 自己完結型 (self-contained) の GUI + ワーカー、CaptionProbe、CM 解析ツール一式、
# インストーラー/アンインストーラーを 1 つの tar.gz にまとめる。
# 受け取った側は .NET SDK 不要で ./install.sh だけで導入できる。
#
# Usage: scripts/package-linux.sh [出力フォルダー]   (既定: ~/tsef-release)
# 環境変数: CM_TOOLS_DIR = ビルド済み CM ツールの場所 (既定: ~/tools/cm-linux)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if ! command -v dotnet >/dev/null 2>&1; then export PATH="$HOME/.dotnet:$PATH"; fi
command -v dotnet >/dev/null 2>&1 || { echo "エラー: dotnet SDK が見つかりません。" >&2; exit 1; }

VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' src/App.Linux/EncodeFlow.App.Linux.csproj | head -n 1)"
[ -n "$VERSION" ] || { echo "エラー: バージョンを取得できません。" >&2; exit 1; }
PKG="TS-Encode-Flow-linux-x64-v$VERSION"
OUT="${1:-$HOME/tsef-release}"
STAGE="$OUT/.staging/$PKG"
mkdir -p "$OUT"
rm -rf "$OUT/.staging"
mkdir -p "$STAGE/app"

echo "== GUI を publish (self-contained linux-x64, v$VERSION) =="
dotnet publish src/App.Linux/EncodeFlow.App.Linux.csproj -c Release -r linux-x64 --self-contained true -o "$STAGE/app"
echo "== ワーカーを publish =="
dotnet publish src/Worker/EncodeFlow.Worker.csproj -c Release -r linux-x64 --self-contained true -o "$STAGE/app/Worker"
find "$STAGE/app" -name '*.pdb' -delete

# 起動スクリプト / アイコン / インストール済みマーカー
cp scripts/linux/start.sh "$STAGE/app/start.sh"
cp scripts/linux/icon.png "$STAGE/app/icon.png"
echo "$PKG" > "$STAGE/app/.tsef-package"
chmod +x "$STAGE/app/start.sh" "$STAGE/app/TS-Encode-Flow-Linux" "$STAGE/app/Worker/TS-Encode-Worker"

# CaptionProbe (無ければビルドする)
if [ ! -x build/native/Release/CaptionProbe ]; then
  echo "== CaptionProbe をビルド =="
  scripts/build-native.sh
fi
mkdir -p "$STAGE/app/Tools" "$STAGE/app/Worker/Tools"
cp build/native/Release/CaptionProbe "$STAGE/app/Tools/CaptionProbe"
cp build/native/Release/CaptionProbe "$STAGE/app/Worker/Tools/CaptionProbe"
chmod +x "$STAGE/app/Tools/CaptionProbe" "$STAGE/app/Worker/Tools/CaptionProbe"

# CM 解析ツール一式 (無ければビルドする)
CM_SRC="${CM_TOOLS_DIR:-$HOME/tools/cm-linux}"
if [ ! -x "$CM_SRC/bin/chapter_exe" ]; then
  echo "== CM ツールをビルド =="
  scripts/build-cm-tools.sh "$CM_SRC"
fi
mkdir -p "$STAGE/app/tools/cm"
cp -a "$CM_SRC/bin" "$STAGE/app/tools/cm/bin"
cp -a "$CM_SRC/JL" "$STAGE/app/tools/cm/JL"

# アプリが期待する配置を相対シンボリックリンクで用意する。
# Store.FindTool は <BaseDir>/Tools → <BaseDir> → PATH の順に探し、
# CmAnalysis は chapter_exe の場所から join_logo_scp / plugins64 /
# libavisynth.so / JL を導出する。実体は tools/cm/ に一本化する。
for base in "$STAGE/app" "$STAGE/app/Worker"; do
  if [ "$base" = "$STAGE/app" ]; then up=".."; cmroot="../../tools/cm"; else up="../.."; cmroot="../../../tools/cm"; fi
  t="$base/Tools"
  mkdir -p "$t/CmLogo"
  ln -s "$up/tools/cm/bin/chapter_exe"    "$t/chapter_exe"
  ln -s "$up/tools/cm/bin/join_logo_scp"  "$t/join_logo_scp"
  ln -s "$up/tools/cm/bin/logoframe"      "$t/logoframe"
  ln -s "$up/tools/cm/bin/libavisynth.so" "$t/libavisynth.so"
  ln -s "$up/tools/cm/bin/plugins64"      "$t/plugins64"
  ln -s "$cmroot/bin/logoframe"           "$t/CmLogo/logoframe"
  ln -s "$cmroot/bin/libavisynth.so"      "$t/CmLogo/libavisynth.so"
done
ln -s "tools/cm/JL"    "$STAGE/app/JL"
ln -s "../tools/cm/JL" "$STAGE/app/Worker/JL"

# インストーラー類と README
cp scripts/linux/install.sh   "$STAGE/install.sh"
cp scripts/linux/uninstall.sh "$STAGE/uninstall.sh"
cp scripts/linux/README-LINUX.md "$STAGE/README-LINUX.md"
cp scripts/linux/ts-encode-flow.desktop.in "$STAGE/ts-encode-flow.desktop.in"
chmod +x "$STAGE/install.sh" "$STAGE/uninstall.sh"

# ライセンス類 (存在するものだけ同梱する)
mkdir -p "$STAGE/licenses"
for f in docs/dotnet-LICENSE.txt docs/dotnet-ThirdPartyNotices.txt docs/libaribcaption-LICENSE.txt third_party/CmLogo/LICENSE third_party/CmLogo/LICENSE.txt; do
  [ -f "$f" ] && cp "$f" "$STAGE/licenses/"
done

echo "== アーカイブ作成 =="
tar -czf "$OUT/$PKG.tar.gz" -C "$OUT/.staging" "$PKG"
rm -rf "$OUT/.staging"
ls -lh "$OUT/$PKG.tar.gz"
sha256sum "$OUT/$PKG.tar.gz"
echo "完成: $OUT/$PKG.tar.gz"
