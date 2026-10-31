param([string]$AribSource)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build-native.ps1') -AribSource $AribSource
$release=Join-Path $root 'build/release'
dotnet publish (Join-Path $root 'src/App/EncodeFlow.App.csproj') -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o $release --nologo
if($LASTEXITCODE -ne 0){throw 'MAUI publish failed'}
dotnet publish (Join-Path $root 'src/Worker/EncodeFlow.Worker.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $release 'Worker') --nologo
if($LASTEXITCODE -ne 0){throw 'Worker publish failed'}
foreach($dir in @((Join-Path $release 'Tools'),(Join-Path $release 'Worker/Tools'))){New-Item -ItemType Directory -Force $dir | Out-Null;Copy-Item (Join-Path $root 'build/native/Release/CaptionProbe.exe') $dir}
Copy-Item (Join-Path $root 'build/native/Release/ShellDialogs.exe') (Join-Path $release 'Tools')
foreach($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item (Join-Path $root $file) $release}
New-Item -ItemType Directory -Force (Join-Path $release 'docs') | Out-Null
Copy-Item -Path (Join-Path $root 'docs/*') -Destination (Join-Path $release 'docs') -Recurse -Force
Write-Output "Release folder: $release"
