# MSI インストーラーと winget 対応（Issue #4）

ZIP 配布は従来どおりで、本インストーラーは同じ publish 成果物
（`scripts/publish.ps1` → `build/release`）を包む追加の配布形式です。
アプリ本体・設定・データの持ち方は ZIP 版と同一です。

## 設計

- WiX Toolset v5（`WixToolset.Sdk`、NuGet 復元のみで構築可能）
- ユーザー単位インストール：`%LocalAppData%\Programs\TS Encode Flow`
  - アプリは自己完結・非パッケージ型で、データも `%LocalAppData%\TS-Encode-Flow` にあるため管理者権限は不要
- スタートメニューにショートカットを作成、アンインストールは「アプリと機能」から可能
- UpgradeCode は全リリース共通。バージョンアップは MajorUpgrade で上書き
- ProductCode / PackageCode はビルド毎に自動生成

## ビルド

```powershell
./scripts/build-msi.ps1            # publish から MSI 生成まで
./scripts/build-msi.ps1 -SkipPublish  # build/release が既にある場合
```

成果物：

- `installer/bin/Release/TS-Encode-Flow-<version>-win-x64.msi`
- `build/winget/` に winget マニフェスト3点（URL・SHA256・ProductCode は実物から生成）

## リリースと winget 申請

1. GitHub Releases に MSI を ZIP と一緒に添付する（release ワークフローが実施）
2. `build/winget/` のマニフェストを microsoft/winget-pkgs リポジトリへ
   `manifests/l/Long0823-1/TSEncodeFlow/<version>/` として PR する
3. 以後のバージョンは同じ PackageIdentifier でマニフェストを追加するだけ

winget 申請はリリース資産の URL が確定してから行うため、初回申請は
MSI を含む最初のリリース後に実施します。
