# ソースからのビルド

## 必要なもの

- Windows x64
- Git
- .NET 9 SDK（`global.json` の基準は9.0.318）
- .NET MAUI Windowsワークロード
- Visual Studio 2022のC++ビルドツール、CMake、Windows SDK

## 取得とビルド

公開リポジトリを取得します。

```powershell
git clone https://github.com/Long0823-1/TS-Encode-Flow.git
cd TS-Encode-Flow
dotnet workload install maui-windows
./scripts/publish.ps1
```

ワークロードやC++ビルドツールのインストールには管理者権限が必要になる場合があります。PowerShellのスクリプト実行は利用環境のポリシーに従ってください。

`publish.ps1` はlibaribcaptionを取得してネイティブツール、MAUIアプリ、ワーカーをビルドし、`build/release` に実行に必要なファイルをまとめます。初回は依存物を取得するためインターネット接続が必要です。字幕ライブラリの固定コミットは `37dde0f32652c32aa98bf92278944d1cfb2e7354` です。

```powershell
./build/release/TS-Encode-Flow.exe
```

配布する場合は `build/release` の中身をすべてZIPにします。EXEだけでは動作しません。FFmpeg・Rigaya製エンコーダー・EDCB DLLは同梱されないため、[導入手順](SETUP.md)で個別に設定します。

## テスト

```powershell
# キューの再追加などの回帰テスト
dotnet run --project tests/Tests.csproj -c Release

# 字幕・DRCS・生成BATの検証
dotnet run --project tests/Tests.csproj -c Release -- --features --caption ./build/release/Tools/CaptionProbe.exe --worker ./build/release/Worker/TS-Encode-Worker.exe

# FFmpegを設定した環境でCPU実変換
dotnet run --project tests/Tests.csproj -c Release -- --live

# 対応AMD GPUとVCEEncCを用意した環境で実変換
dotnet run --project tests/Tests.csproj -c Release -- --live --vce C:/Tools/VCEEncC64.exe
```

テスト用TSや出力は一時フォルダーに作成されます。入力録画や個人の設定データをリポジトリに追加しないでください。検証済み環境と制限は[検証記録](VALIDATION.md)を参照してください。

## 新しい版を配布する

1. Appの `.csproj` のVersion・ApplicationDisplayVersion・ApplicationVersionと、WorkerのVersionを更新します。画面の版表記・READMEも更新します。
2. ビルド・必要な回帰テストを行い、配布フォルダーを全体ごとZIPにします。
3. ソースをmainへpushし、対応するコミットを対象に `v0.3.1` のような正式版タグでGitHub Releaseを作成します。Windows x64用ZIPを添付し、Latestに設定します。
4. Releasesの内容と配布物を確認します。起動中の旧版アプリでは、次の更新確認から通知されます。

正式版の例（バージョン、ZIP、notesのファイルは実際の内容に置き換え）：

```powershell
gh release create v0.3.1 ./TS-Encode-Flow-v0.3.1-windows-x64.zip --repo Long0823-1/TS-Encode-Flow --target main --title "v0.3.1" --notes-file ./release-notes.md --latest
```

コミットのpushだけでは通知しません。ドラフトやプレリリースも対象外です。確認APIは [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release) を使用します。

CM処理の検証：

```powershell
dotnet run --project tests/Tests.csproj -c Release -- --cm-live
dotnet run --project tests/Tests.csproj -c Release -- --cm-live --vce C:/Tools/VCEEncC64.exe
```

Comskip呼び出しの自動テストはプロトコル用スタブを使用します。実Comskipによる放送CMの判定精度を保証するものではありません。
