# Linux GUI（Avalonia）

`src/App.Linux/` は Linux 向けの Avalonia GUI です。Windows 版（`src/App`、MAUI）とは別プロジェクトですが、
データフォルダーとワーカーとの契約は同一です（`settings.json` / `queue.json` / `worker-status.json` / `Inbox` を
Core の `Integration` / `QueueManager` 経由で読み書きします）。そのため Windows GUI・Linux GUI・Worker は相互に使えます。

## 動作範囲（B-2b）

- 実装済み：変換キュー（進捗のライブ更新）、TS の追加（ファイル選択・ドラッグ＆ドロップ）、
  プリセット選択（Linux 専用の VA-API プリセットを含む）、ジョブの中止・再試行、
  処理の再開・一時停止・すべて中止・履歴クリア、ワーカー状態表示と「ワーカーを起動」ボタン。
- プリセット画面：変換方式・カテゴリで絞り込み、選択、詳細・カスタマイズ（Windows 版エディタと
  同じ項目一式。工場プリセットの編集はカスタムとして複製保存）、自作プリセットの追加・複製・削除。
- 設定画面：保存先・ファイル名マクロ（プレビュー付き）、エンコード用キャッシュ（その場保存）、
  変換ツールのパス指定と自動検出（Store.Detect、検出状態の表示）、VA-API デバイス、同時変換数、
  設定フォルダーを開く。Windows 版の「ツールをまとめてダウンロード」は Windows x64 バイナリ
  専用のため Linux 版にはなく、代わりに `scripts/build-native.sh`（CaptionProbe）と
  `scripts/build-cm-tools.sh`（CM ツール）で用意する旨を画面に注記しています。
  ツール指定のうち EpgDataCap3.dll（番組情報）と DelogoHD.dll（ロゴ消しプラグイン）は
  Windows 専用機能のため、Linux 版の設定画面には項目自体がありません（settings.json の
  フィールドと Windows 版の動作は従来どおりです）。
  また Linux 版 GUI は、他のプロセス（もう一つの GUI や手編集）が settings.json を
  書き換えた場合に、1 秒周期の更新で内容を取り込みます（ワーカー側の変更監視と同じ考え方）。
  起動中の画面に外部の変更が反映されず、自前保存で消えてしまう問題への対策です。
- 自動化画面：フォルダー監視（フォルダー・サブフォルダー・安定待ち秒数・有効化）と同時変換数。
  録画後 BAT／EPGTimer 連携は Windows 専用のため、カードごと無効化して理由を注記しています
  （BatPresetIndex などの設定値自体は Windows 版と共有されます）。
- 字幕・DRCS画面：入力待ちジョブのDRCS字形一覧（字形画像の表示、未対応を先に表示）、
  参照画像の登録、代替文字の保存と「全字形が解決したジョブの再開依頼」、
  選択ジョブの字幕トラック情報。Windows 版と同じ `Drcs.Save` と再開依頼の契約を使います。
- CM・タイムリスト画面：確認待ちジョブの区間表示（本編／CMバー、区間の一覧・追加・削除）、
  EDL / AviSynth 分割ファイルの取り込み、CM処理方法の選択、確定（approve-cm 依頼）による再開。
  ほかに Core の `CmAnalysis` をそのまま呼ぶ「CM解析（診断）」の入口があります。
- 放送局ロゴ画面：登録済み局・候補・範囲指定待ちの一覧（LGDプレビューの表示と拡大確認）、
  LGDの手動追加（既存局／新規局）、ロゴの削除・候補の削除・局の削除、候補の承認／却下、
  範囲指定（ffmpeg でフレーム画像を取得し、ドラッグで矩形選択。数値入力とも連動）。
  すべて Windows 版と同じ Inbox 依頼とファイル形式を使うため、A-4 の Delogo がそのまま読めます。
- 解析・比較画面：Windows 版（EncodingLab）の6機能（映像の比較プレビュー、品質・容量・速度の試算、
  TSドロップ・時刻飛びの診断、混在FPSの全編解析、登録済みジョブの結果・再開情報、
  入力情報・音声ストリーム一覧）。診断結果は `Lab/<guid>/signal.json` 等へ保存し、
  試算結果からカスタムプリセットを保存できます。

## CM解析・ロゴ除去の実行（統合済み）

CM解析（logoframe / chapter_exe / join_logo_scp）とロゴ除去は、フェーズA-4
（旧 `feature/linux-cm-delogo` ブランチ）を本ブランチへ統合済みで、Linux でも
そのまま動作します。CMツールは `scripts/build-cm-tools.sh` で再ビルドし、設定画面の
「変換ツール」で chapter_exe / logoframe のパスを指定してください（配置先フォルダーから
join_logo_scp・AviSynth・JL スクリプトを相対解決します）。ロゴ除去は登録済みロゴ範囲
または LGD ヘッダーから矩形を解決し、ffmpeg の delogo フィルターで処理します
（Windows の DelogoHD 逆算合成とは方式が異なり、品質面では Windows 経路が上です）。

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

# セクションを指定して描画する（--edit-preset でプリセット編集画面も開ける）
TS-Encode-Flow-Linux --data <フォルダー> --screenshot <PNGパス> --section 設定
TS-Encode-Flow-Linux --data <フォルダー> --screenshot <PNGパス> --section プリセット --edit-preset

# B-2a の検証：プリセット編集→設定保存→自動化保存の経路を通し、ワーカーの反映を確認する
TS-Encode-Flow-Linux --data <フォルダー> --smoke-b2a <TSファイル>

# B-2b の検証：字幕・DRCS（CaptionProbe セルフテストの字形で収集→保存→再開依頼）、
# CM（確定→ワーカーがCMカット変換まで完走）、放送局ロゴ（範囲指定→LGD追加→Delogo解決）、
# 解析・比較（TS診断）を各画面のロジック経路で確認する
TS-Encode-Flow-Linux --data <フォルダー> --smoke-b2b <TSファイル>

# B-2b 各画面の表示確認用フィクスチャ（字形・CM区間・ロゴ範囲待ち・登録済み局）を投入して描画する
TS-Encode-Flow-Linux --data <フォルダー> --screenshot <PNGパス> --section <セクション名> --fixture-b2b <TSファイル>

# 解析・比較画面に TS 診断（トランスポート解析）の結果を表示して描画する
TS-Encode-Flow-Linux --data <フォルダー> --screenshot <PNGパス> --demo-signal <TSファイル>
```

## 配布パッケージ（誰でもインストールできる形）

`scripts/package-linux.sh` で、受け取った側が .NET SDK なしで導入できる自己完結型パッケージを作成します。

```bash
# 既定の出力先は ~/tsef-release（引数で変更可）
scripts/package-linux.sh

# ビルド済み CM ツールの場所を変える場合
CM_TOOLS_DIR=~/tools/cm-linux scripts/package-linux.sh
```

出力は `TS-Encode-Flow-linux-x64-v<バージョン>.tar.gz`（バージョンは `src/App.Linux` の csproj から自動取得）です。中身は次のとおりです。

- GUI とワーカー（self-contained linux-x64 で publish。.NET ランタイムの事前導入は不要）
- `Tools/CaptionProbe`（GUI 側・ワーカー側の両方に実体を配置）
- CM 解析ツール一式を `tools/cm/{bin,JL}` に同梱（logoframe / chapter_exe / join_logo_scp / libavisynth.so / plugins64/libLSMASHSource.so / JL スクリプト群）。GUI・ワーカーの `Tools/` から相対シンボリックリンクで接続され、設定の事前投入なしで自動検出されます
- `install.sh` / `uninstall.sh` / `README-LINUX.md` / `.desktop` テンプレート / アイコン / ライセンス類

CaptionProbe と CM ツールが手元に無い場合は、パッケージ作成の中で `scripts/build-native.sh` / `scripts/build-cm-tools.sh` により自動でビルドされます。

### インストール / アンインストール（受け取った側）

```bash
tar -xzf TS-Encode-Flow-linux-x64-v<バージョン>.tar.gz
cd TS-Encode-Flow-linux-x64-v<バージョン>
./install.sh            # root 不要。既定の導入先は ~/.local/share/ts-encode-flow
./install.sh <パス>     # 導入先を指定する場合
```

- `install.sh` は `~/.local/share/applications/` への `.desktop` 配置と `~/.local/bin/ts-encode-flow` リンクの作成まで行います。再実行するとそのまま上書きアップグレードします（冪等）。他人のフォルダーを誤って消さないよう、パッケージのマーカー（`.tsef-package`）を確認してから置き換えます。
- ユーザーデータ（`~/.local/share/TS-Encode-Flow` の設定・キュー）には一切触れません。
- ffmpeg / ffprobe が PATH に無い場合は、警告とディストリビューション別の導入ヒントを表示します（変換に必須のため同梱していません）。
- `./uninstall.sh` は本体とランチャーのみを削除し、ユーザーデータは残します。
- 起動はアプリケーションメニューの「TS Encode Flow」、コマンド `ts-encode-flow`、または導入先の `start.sh` から行います。
