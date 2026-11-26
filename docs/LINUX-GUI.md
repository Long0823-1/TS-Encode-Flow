# Linux GUI（Avalonia）

`src/App.Linux/` は Linux 向けの Avalonia GUI です。Windows 版（`src/App`、MAUI）とは別プロジェクトですが、
データフォルダーとワーカーとの契約は同一です（`settings.json` / `queue.json` / `worker-status.json` / `Inbox` を
Core の `Integration` / `QueueManager` 経由で読み書きします）。そのため Windows GUI・Linux GUI・Worker は相互に使えます。

## 動作範囲（B-1）

- 実装済み：変換キュー（進捗のライブ更新）、TS の追加（ファイル選択・ドラッグ＆ドロップ）、
  プリセット選択（Linux 専用の VA-API プリセットを含む）、ジョブの中止・再試行、
  処理の再開・一時停止・すべて中止・履歴クリア、ワーカー状態表示と「ワーカーを起動」ボタン。
- 未実装（B-2 予定）：プリセット編集、字幕・DRCS、CM・タイムリスト、放送局ロゴ、自動化、解析・比較、設定の各画面。
  左レールの項目だけ用意してあり、選ぶとプレースホルダーが表示されます。

## ビルドと実行（開発時）

前提：.NET SDK 9.0.318（`~/.dotnet` など）、ffmpeg / ffprobe。

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet build src/Worker/EncodeFlow.Worker.csproj -c Release   # ワーカー（GUIが自動で探します）
dotnet run --project src/App.Linux -c Release                  # GUI
```

`--data <フォルダー>` を付けるとデータフォルダーを差し替えられます（Windows 版と同じ指定方法）。

## 配置（publish）とランチャー

```bash
dotnet publish src/App.Linux/EncodeFlow.App.Linux.csproj -c Release -o ~/Applications/TsEncodeFlow
mkdir -p ~/Applications/TsEncodeFlow/Worker
cp src/Worker/bin/Release/net9.0-windows/TS-Encode-Worker{,.dll,.deps.json,.runtimeconfig.json} \
   src/Worker/bin/Release/net9.0-windows/EncodeFlow.Core.dll ~/Applications/TsEncodeFlow/Worker/
```

フレームワーク依存ビルドのため、起動には `DOTNET_ROOT`（.NET のインストール先）が必要です。
同梱の `start.sh` が `DOTNET_ROOT=$HOME/.dotnet` の設定とワーカーの先行起動を行ってから GUI を開きます。
アプリランチャー用に `~/.local/share/applications/ts-encode-flow.desktop`（Exec=start.sh）を置くと、
デスクトップ環境のメニューから起動できます。

端末から起動する場合：

```bash
~/Applications/TsEncodeFlow/start.sh
```

## 検証用モード（GUI を開かずに確認する）

```bash
# GUI と同じクライアント経路で TS を登録し、ワーカーの処理結果を読み戻す
TS-Encode-Flow-Linux --data <フォルダー> --smoke-enqueue <TSファイル> [エンジン名]

# ヘッドレスでメインウィンドウを描画して PNG に保存する
TS-Encode-Flow-Linux --data <フォルダー> --screenshot <PNGパス>
```
