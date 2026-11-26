# TS Encode Flow for Linux

録画した TS ファイルを MP4 / MKV へ変換するアプリの Linux 版です。
GUI（Avalonia）と変換ワーカー、字幕解析ツール（CaptionProbe）、CM 解析ツール一式を
1 つのパッケージにまとめています。.NET SDK やソースからのビルドは不要です。

## 動作環境

- x86_64 の Linux（glibc 系ディストリビューション）
- **ffmpeg と ffprobe が PATH にあること**（変換に必須です。同梱していません）
  - Arch Linux: `sudo pacman -S ffmpeg`
  - Debian / Ubuntu: `sudo apt install ffmpeg`
  - Fedora: `sudo dnf install ffmpeg`
- デスクトップ環境（GUI の表示に必要です）
- ハードウェアエンコード（任意）
  - AMD GPU の VA-API エンコード: Mesa ドライバーで動作します（Radeon RX 570 クラスで動作確認済み）
  - NVIDIA / Intel の GPU を使う場合は、別途 NVEncC / QSVEncC / VCEEncC を用意し、
    アプリの設定画面でパスを指定してください（同梱していません）

## インストール

```bash
tar -xzf TS-Encode-Flow-linux-x64-v*.tar.gz
cd TS-Encode-Flow-linux-x64-*
./install.sh
```

- root 権限は不要です。`~/.local/share/ts-encode-flow` にインストールされます
  （インストール先を変える場合は `./install.sh <パス>`）。
- アプリケーションメニューに「TS Encode Flow」が登録され、
  `~/.local/bin/ts-encode-flow` コマンドでも起動できます。
- `install.sh` をもう一度実行すると、そのまま上書きアップグレードします。
- ffmpeg / ffprobe が無い場合は、インストールの最後に警告が表示されます。

## 起動方法

- アプリケーションメニューから「TS Encode Flow」を選ぶ
- または端末で `ts-encode-flow`（PATH に `~/.local/bin` が無い場合は
  `~/.local/share/ts-encode-flow/start.sh`）

起動時に変換ワーカーも自動で立ち上がります。

## アンインストール

```bash
cd TS-Encode-Flow-linux-x64-*
./uninstall.sh
```

アプリ本体とランチャーだけを削除します。**設定や変換キューのデータは残ります**
（後述のデータフォルダーを手動で削除すれば完全に消えます）。

## データの場所

設定・変換キュー・ログは次のフォルダーに保存されます。
インストールやアップグレードでは変更されません。

```
~/.local/share/TS-Encode-Flow
  settings.json   設定
  queue.json      変換キュー
```

## 同梱ツール

- **CaptionProbe**（`Tools/CaptionProbe`）: ARIB 字幕・DRCS 外字の解析
- **CM 解析ツール一式**（`tools/cm/`）: logoframe / chapter_exe / join_logo_scp /
  AviSynth+ / L-SMASH-Works の Linux ビルド。CM カットに使います。
  アプリが自動検出するため、パスの設定は不要です。

## 既知の制限

- **実放送の録画 TS での CM カット検証は未実施**です（合成素材での検証は完了）。
  ロゴ学習の成否は実際の放送映像に依存します。CM 区間はアプリの
  「CM・タイムリスト」画面で事前確認できます。
- **ロゴ消し（Delogo）の方式が Windows 版と異なります**。Linux 版は ffmpeg の
  delogo フィルターによる周囲からの補間です。Windows 版の DelogoHD
  （元画素の逆算合成）より画質面では不利です。
- **EPGTimer 連携と録画後 BAT は Windows 専用**です。Linux 版では設定画面で
  無効として表示されます（設定値自体は Windows 版と共有されます）。
- VA-API で AV1 のハードウェアエンコードはできません（対応ハードウェアが
  ないため、明示的にエラーになります）。
