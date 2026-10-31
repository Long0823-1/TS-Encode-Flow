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

## v0.2.1 修正と追加検証

- メニュー往復時のCOM例外（0x800F1000、「インストールされたコンポーネントが何も検出されませんでした。」）を再現。ダッシュボードのLabelとキューのCollectionViewを画面ごとに作成し、MAUIのネイティブ親の重複を解消。各メニューからダッシュボードへの復帰、キューとプリセットの往復を確認。
- Windows標準ファイル選択を同梱のShellDialogsで開き、表示とキャンセルを確認。例外詳細をui-errors.logに保存。
- AMDログの変換進捗とGPU使用率を区別。基本＋字幕・DRCS・BAT連携は44項目合格。
- 提供された約2.3GB・30分の録画TSを、Radeon RX 570 / VCEEncC 9.22で全編変換。H.264/MP4（1280×720、Yadif、AAC）とHEVC/MKV（1440×1080、SAR 4:3、Bwdif、音声コピー）を並列実行し、両方成功。各ケース24項目合格。いずれも表示比16:9、プログレッシブ、約1807.37秒、日本語番組名・番組説明を保持。
- この録画は字幕PIDの宣言がある一方、全12,303,537 TSパケット中、字幕パケットは0。字幕なしを正常に扱い、実データがない旨を完了表示に反映。実字幕・DRCSは別の実放送TSおよび字形サンプルで検証している。
- ユーザーの元TS・番組情報・エラーログは変更していない。
- v0.2.1でAMD並列・中止・Yadif Bob等の追加34項目に合格。実録画のAMD出力2本はFFmpegで先頭から末尾まで映像・音声を再デコードし、エラーログ0バイトで完了（`-v error -xerror -map 0:v -map 0:a -f null -`）。
