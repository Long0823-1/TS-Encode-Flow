#!/usr/bin/env bash
# TS Encode Flow (Linux) アンインストーラー
# アプリ本体とランチャーだけを削除します。
# ユーザーデータ（~/.local/share/TS-Encode-Flow の設定・キュー・ログ）は残ります。
set -euo pipefail

DEST="${1:-$HOME/.local/share/ts-encode-flow}"

if [ -d "$DEST" ]; then
  if [ -f "$DEST/.tsef-package" ]; then
    rm -rf "$DEST"
    echo "アプリ本体を削除しました: $DEST"
  else
    echo "エラー: $DEST は TS Encode Flow のインストール先ではないようです。中止します。" >&2
    exit 1
  fi
else
  echo "アプリ本体は見つかりませんでした: $DEST"
fi

rm -f "$HOME/.local/share/applications/ts-encode-flow.desktop" \
      "$HOME/Desktop/ts-encode-flow.desktop" \
      "$HOME/.local/bin/ts-encode-flow"
echo "ランチャーとコマンドリンクを削除しました。"
echo ""
echo "ユーザーデータは残しています: $HOME/.local/share/TS-Encode-Flow"
echo "不要であれば、そのフォルダーを手動で削除してください。"
