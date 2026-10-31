$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Visual Studio C++ x64 build tools are required'}
$source=Join-Path $root 'third_party/CmLogo/src'
$output=Join-Path $root 'build/cm-logo'
New-Item -ItemType Directory -Force $output | Out-Null
$batch=Join-Path $output 'compile.cmd'
@"
@echo off
call "$vs\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /O2 /utf-8 /DNOMINMAX /EHsc /std:c++17 /TP /DHAVE_DTVINDEX=0 /DHAVE_AVISYNTH=1 /D_CRT_SECURE_NO_WARNINGS /I ../extras logoframe.c logo_generator.cpp logoframe_det.c logoframe_mul.c /Fe:"$output\logoframe.exe" /Fo:"$output\\"
"@ | Set-Content -LiteralPath $batch -Encoding ascii
Push-Location $source
try{& cmd /c $batch;if($LASTEXITCODE -ne 0){throw 'CM logo build failed'}}finally{Pop-Location}
Copy-Item (Join-Path $root 'third_party/CmLogo') (Join-Path $output 'source') -Recurse -Force
Write-Output "CM logo executable and complete corresponding source: $output"
