@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /O2 /utf-8 /DNOMINMAX /EHsc /std:c++17 /TP /DHAVE_DTVINDEX=0 /DHAVE_AVISYNTH=1 /D_CRT_SECURE_NO_WARNINGS /I ..\extras logoframe.c logo_generator.cpp logoframe_det.c logoframe_mul.c /Fe:logoframe.exe

