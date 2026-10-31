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

## External tools (not included)

- [FFmpeg](https://ffmpeg.org/legal.html): LGPL / GPL depending on the selected build. Choose a build supporting the desired codecs and filters.
- [NVEnc](https://github.com/rigaya/NVEnc), [VCEEnc](https://github.com/rigaya/VCEEnc), [QSVEnc](https://github.com/rigaya/QSVEnc): see each project's license and notices.
- [EDCB / EpgDataCap3.dll](https://github.com/tsukumijima/EDCB): see EDCB's original freeware terms and per-file notices. This DLL is not included.

The app does not download, relicense, or embed these external tools. It invokes tools selected by the user.
