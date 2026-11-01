# logoframe Windows build for TS Encode Flow

Upstream: https://github.com/tobitti0/logoframe
Commit: 8185bafc281e86d847d8084de53c7ae42acfb532
License: GPL-3.0 (see LICENSE). This standalone program is separate from the MIT-licensed app.

Changes (2026-10-31): normalize managed logo repository paths for the existing Win32 detector. Add -info-only to report decoded frame geometry/FPS/count without logo scanning. Build with MSVC x64, AviSynth enabled, dtvindex disabled. CM analysis only; no Amatsukaze application code is included.

Build: open a Visual Studio x64 developer command prompt, cd src, run:
cl /nologo /O2 /utf-8 /DNOMINMAX /EHsc /std:c++17 /TP /DHAVE_DTVINDEX=0 /DHAVE_AVISYNTH=1 /D_CRT_SECURE_NO_WARNINGS /I ../extras logoframe.c logo_generator.cpp logoframe_det.c logoframe_mul.c /Fe:logoframe.exe

The downloadable binary archive includes this complete corresponding source tree and license. AviSynth.dll is obtained separately by the app's CM dependency downloader.


Changes (2026-11-02, cm-logo-v3): preserve strict background sampling first; on insufficient samples or active pixels retry with a dominant (70%) tightly clustered border colour. This tolerates limited edge text without treating arbitrary textured borders as flat. Add sample/active-pixel diagnostics and -logo-generate-only. CMLOGO_REVIEW_REQUIRED marks candidates produced by the retry: callers must inspect these before promoting them to the station repository because programme text can contaminate a candidate. The TS Encode Flow app stages these per job and requires explicit confirmation. No logo/no variance remains an error. Native regression tests are in tests/generator_tests.cpp (GPL-3.0).
