# 検証記録（2026-10-31）

Windows x64、.NET SDK 9.0.318、MAUI 9.0.111で確認しました。

- MAUI / WinUIアプリのビルド・self-contained publish：成功（警告なし）。配布フォルダーのEXEを実際に起動。
- 基本＋字幕・DRCS・BAT連携：43項目合格。配布用のCaptionProbeとself-containedワーカーで実行。
- CPU実変換：33項目合格。2本の並列実行、320×180、Yadif Bob 60000/1001fps、同名回避、元TS保持、中止を含む。
- AMD Radeon RX 570 / VCEEncC 9.22：同じ33項目合格。MAUI移行後の.NET 9コアでも再確認。
- 実際の地上波TS：変換成功。ARIB字幕をMP4のmov_textに保存し、EITの日本語番組名をメタデータに格納。
- MP4 / MKV：字幕コーデックと日本語番組名・説明のタグをffprobeで検証。保存した動画から字幕を取り出し、DRCSの手動代替文字が残ることも確認。
- DRCS：libaribcaptionの同梱サンプルから字形BMPと字幕文脈を取得。未登録判定、文字対応表の保存と再読込、字幕への反映を確認。未知文字の判定テストでは、既知代替文字を消した検証用データも使用。
- BAT：`FilePath` 環境変数を渡して生成BATそのものを実行。日本語・空白・`&`・`!`・`%PATH%` を含むファイル名が展開されず、そのままワーカーへ渡ることを確認。
- BATプリセット：手動変換の既定とは異なるプリセットを固定。作成後に設定を変更しても、BATが作成時の160×90で変換することを確認。
- 画面：ダッシュボード、用途別プリセット、DRCS字形、録画後専用プリセットの選択を確認。画面で「アニメ・コレクション」を選び、手動変換の「毎日の番組・バランス」と別に保存されることをJSON設定で確認。

NVIDIA / Intel GPU、全コーデック・全フィルタの組み合わせ、クリーンOS、実際のEPGTimer録画終了イベントからの起動は未検証です。BATはEDCBX形式の環境変数を再現して検証しました。

代表的な再現コマンド：

```powershell
dotnet run --project tests/Tests.csproj -c Release -- --live
dotnet run --project tests/Tests.csproj -c Release -- --live --vce C:/Tools/VCEEncC64.exe
dotnet run --project tests/Tests.csproj -c Release -- --features --caption ./build/release/Tools/CaptionProbe.exe --worker ./build/release/Worker/TS-Encode-Worker.exe
```
