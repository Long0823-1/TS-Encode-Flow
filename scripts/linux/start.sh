#!/bin/sh
# TS Encode Flow (Linux) 起動スクリプト
# ワーカーが落ちていても困らないよう、先にワーカーを起動してから GUI を開く。
# （ワーカーは worker.lock で二重起動を防ぐため、起動済みなら何も起きない）
# 自己完結型パッケージのため .NET ランタイムの事前導入は不要。
DIR="$(cd "$(dirname "$0")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}/TS-Encode-Flow"
mkdir -p "$DATA"
if [ -x "$DIR/Worker/TS-Encode-Worker" ]; then
  (cd "$DIR/Worker" && nohup "$DIR/Worker/TS-Encode-Worker" --serve >>"$DATA/worker-launch.log" 2>&1 &)
fi
exec "$DIR/TS-Encode-Flow-Linux" "$@"
