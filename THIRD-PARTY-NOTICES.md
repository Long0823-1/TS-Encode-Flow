# Third-party notices

TS Encode Flow's original code is MIT licensed. Individual third-party components keep their own licenses.

## libaribcaption

CaptionProbe statically links [libaribcaption](https://github.com/xqq/libaribcaption), revision `37dde0f32652c32aa98bf92278944d1cfb2e7354`. The upstream root MIT license is included in [docs/libaribcaption-LICENSE.txt](docs/libaribcaption-LICENSE.txt). Decoder headers, implementations, and self-test caption data also carry the following notice, reproduced here:

Copyright (C) 2022 magicxqq <xqq@xqq.im>. All rights reserved.

Permission to use, copy, modify, and distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

Other upstream decoder files carry corresponding 2021 / 2022 notices by the same author. The source tree retains its original notices. Sample subtitle data is used only for the native decoder self-test and illustrative validation, not as part of any recorded program distribution.

## Microsoft components

The self-contained release includes .NET, .NET MAUI, and Windows App SDK runtime components. Their licenses apply independently: [.NET runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [.NET MAUI](https://github.com/dotnet/maui/blob/main/LICENSE.TXT), [Windows App SDK](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE). Runtime notices supplied by the SDK are retained in the release layout.

## External tools obtained on request

The app downloads portable tools from their official release distributions and invokes them as separate processes. Their licenses remain independent. The tool setup screen shows the source, release version, archive SHA-256 and available upstream/bundled license documents. An upstream digest is compared when the release API provides one; the screen distinguishes computed-only hashes.

- FFmpeg / ffprobe: GPL-enabled BtbN build. See https://ffmpeg.org/legal.html and the downloaded distribution license.
- NVEncC, VCEEncC, QSVEncC: see each rigaya project's license file and included library notices.
- EDCB / EpgDataCap3.dll: xtne6f/EDCB work-plus-s distribution; see its per-file licenses and notices. Do not assume all EDCB forks have identical terms.
- 7-Zip portable extractor: LGPL and applicable component notices. https://www.7-zip.org/license.txt
- chapter_exe: GPL-2.0; https://github.com/nekopanda/chapter_exe and https://github.com/rigaya/chapter_exe
- join_logo_scp: GPL-2.0; https://github.com/nekopanda/join_logo_scp
- AviSynth: GPL-2.0-or-later; https://github.com/nekopanda/AviSynthPlus
- L-SMASH Works source plugin: ISC and the licenses of its linked dependencies, including FFmpeg; https://github.com/VFR-maniac/L-SMASH-Works

The CM dependency downloader reads the rigaya/Amatsukaze distribution archive but retains only chapter_exe, join_logo_scp, AviSynth, L-SMASH source plugin, required runtime DLLs, JL scripts and legal/readme documents. It does not install or invoke AmatsukazeCLI, GUI, server, muxers or encoders. CM analysis is orchestrated by this app. No Amatsukaze application source is transplanted.

## Standalone CM logo helper

The optional separate logoframe executable is GPL-3.0. Copyright notices and the full license are retained in third_party/CmLogo. Its complete corresponding source, upstream revision and Windows build instructions are included in both this repository and the downloadable helper ZIP. This GPL code is compiled into a separate executable, not linked into the MAUI/Core/Worker assemblies. See third_party/CmLogo/README.md for the small info-only addition and scripts/build-cm-logo.ps1 for a reproducible build.
