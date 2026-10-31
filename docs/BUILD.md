# ソースからのビルド

## 必要なもの

- Windows x64
- Git
- .NET 9 SDK（`global.json` の基準は9.0.318）
- .NET MAUI Windowsワークロード
- Visual Studio 2022のC++ビルドツール、CMake、Windows SDK

## 取得とビルド

非公開リポジトリを読めるGitHubアカウントで認証し、取得します。

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
