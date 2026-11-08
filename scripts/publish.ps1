param([string]$AribSource)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build-native.ps1') -AribSource $AribSource
$release=Join-Path $root 'build/release'
dotnet publish (Join-Path $root 'src/App/EncodeFlow.App.csproj') -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o $release --nologo
if($LASTEXITCODE -ne 0){throw 'MAUI publish failed'}
# NOTE: do NOT trim the publish output. Single-file publish breaks WinUI3
# (its native DLLs cannot load from the Temp self-extract folder), deleting
# loose DLLs breaks the self-contained host, and InvariantGlobalization
# breaks WinUI XAML startup (0xc000027b in Microsoft.UI.Xaml.dll). The
# ~120 culture folders are the price of a working unpackaged MAUI app.
# NOTE: never delete loose .dll/.winmd files from a self-contained publish.
# The .NET host resolves framework assemblies from the app folder by their
# .dll names (a missing System.*.dll makes the apphost demand the .NET
# Desktop Runtime), and WinUI3 loads its native DLLs from the same folder.
# Removing the culture folders alone takes the root from 100+ entries to
# roughly 30; the remaining loose files are load-bearing.
dotnet publish (Join-Path $root 'src/Worker/EncodeFlow.Worker.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $release 'Worker') --nologo
if($LASTEXITCODE -ne 0){throw 'Worker publish failed'}
foreach($dir in @((Join-Path $release 'Tools'),(Join-Path $release 'Worker/Tools'))){New-Item -ItemType Directory -Force $dir | Out-Null;Copy-Item (Join-Path $root 'build/native/Release/CaptionProbe.exe') $dir}
Copy-Item (Join-Path $root 'build/native/Release/ShellDialogs.exe') (Join-Path $release 'Tools')
foreach($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item (Join-Path $root $file) $release}
New-Item -ItemType Directory -Force (Join-Path $release 'docs') | Out-Null
Copy-Item -Path (Join-Path $root 'docs/*') -Destination (Join-Path $release 'docs') -Recurse -Force
Write-Output "Release folder: $release"
