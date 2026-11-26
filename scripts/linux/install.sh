#!/usr/bin/env bash
# TS Encode Flow (Linux) インストーラー
# - root 不要。ユーザーのホーム以下だけにインストールします。
# - 再実行するとそのまま上書きアップグレードします（冪等）。
# - ユーザーデータ（~/.local/share/TS-Encode-Flow の設定・キュー）には触れません。
set -euo pipefail

SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="${1:-$HOME/.local/share/ts-encode-flow}"
APPS_DIR="$HOME/.local/share/applications"
BIN_DIR="$HOME/.local/bin"

if [ ! -d "$SRC/app" ]; then
  echo "エラー: パッケージの展開が不完全です（app フォルダーがありません）。" >&2
  exit 1
fi

# 既存の別物フォルダーを誤って消さないための安全確認
if [ -d "$DEST" ] && [ ! -f "$DEST/.tsef-package" ] && [ -n "$(ls -A "$DEST" 2>/dev/null)" ]; then
  echo "エラー: $DEST は TS Encode Flow のインストール先ではないようです。中止します。" >&2
  echo "別の場所にインストールする場合は、引数でインストール先を指定してください: ./install.sh <パス>" >&2
  exit 1
fi

echo "インストール先: $DEST"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -a "$SRC/app/." "$DEST/"
chmod +x "$DEST/TS-Encode-Flow-Linux" "$DEST/start.sh" \
         "$DEST/Worker/TS-Encode-Worker" \
         "$DEST/Tools/CaptionProbe" "$DEST/Worker/Tools/CaptionProbe" 2>/dev/null || true

# アプリケーションメニュー用ランチャー
mkdir -p "$APPS_DIR"
sed "s|@DEST@|$DEST|g" "$SRC/ts-encode-flow.desktop.in" > "$APPS_DIR/ts-encode-flow.desktop"
chmod +x "$APPS_DIR/ts-encode-flow.desktop"
if [ -d "$HOME/Desktop" ]; then
  cp "$APPS_DIR/ts-encode-flow.desktop" "$HOME/Desktop/ts-encode-flow.desktop"
  chmod +x "$HOME/Desktop/ts-encode-flow.desktop"
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi

# コマンドライン起動用シンボリックリンク
mkdir -p "$BIN_DIR"
ln -sf "$DEST/start.sh" "$BIN_DIR/ts-encode-flow"

# ffmpeg / ffprobe の確認（変換に必須。同梱していない）
missing=""
command -v ffmpeg  >/dev/null 2>&1 || missing="$missing ffmpeg"
command -v ffprobe >/dev/null 2>&1 || missing="$missing ffprobe"
if [ -n "$missing" ]; then
  echo ""
  echo "警告: 次の必須ツールが PATH に見つかりません:$missing"
  echo "TS Encode Flow の変換には ffmpeg と ffprobe が必要です。先にインストールしてください。"
  echo "  Arch Linux : sudo pacman -S ffmpeg"
  echo "  Debian/Ubuntu: sudo apt install ffmpeg"
  echo "  Fedora     : sudo dnf install ffmpeg"
fi

echo ""
echo "インストールが完了しました。"
echo "起動方法: アプリケーションメニューの「TS Encode Flow」、またはコマンド ts-encode-flow"
echo "（$BIN_DIR が PATH に無い場合は $DEST/start.sh を直接実行してください）"
echo "設定や変換キューのデータは $HOME/.local/share/TS-Encode-Flow に保存されます（このインストーラーは変更しません）。"
