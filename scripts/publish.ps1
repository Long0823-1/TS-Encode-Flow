param([string]$AribSource)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build-native.ps1') -AribSource $AribSource
$release=Join-Path $root 'build/release'
dotnet publish (Join-Path $root 'src/App/EncodeFlow.App.csproj') -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o $release --nologo
if($LASTEXITCODE -ne 0){throw 'MAUI publish failed'}
# Trim satellite (culture) folders and loose runtime DLLs except the ones the app
# loads straight from its own folder. WindowsAppSDK self-contained ships the
# WinUI/MRTCore native DLLs next to the exe on purpose: keep those, keep the
# apphost, the app assemblies and their json manifests, delete the rest.
$cultures=@('af-ZA','am-ET','ar','ar-SA','as-IN','az-Latn-AZ','bg-BG','bn-IN','bs-Latn-BA','ca','ca-ES','ca-Es-VALENCIA','cs','cs-CZ','cy-GB','da','da-DK','de','de-DE','el','el-GR','en-GB','en-us','es','es-ES','es-MX','et-EE','eu-ES','fa-IR','fi','fi-FI','fil-PH','fr','fr-FR','ga-IE','gd-gb','gl-ES','gu-IN','he-IL','hi-IN','hr-HR','hu-HU','hy-AM','id-ID','is-IS','it','it-IT','ja-JP','ka-GE','kk-KZ','km-KH','kn-IN','ko-KR','kok-IN','lb-LU','lo-LA','lt-LT','lv-LV','mi-NZ','mk-MK','ml-IN','mr-IN','ms-MY','mt-MT','nb-NO','ne-NP','nl-NL','nn-NO','no','or-IN','pa-IN','pl-PL','pt-BR','pt-PT','quz-PE','ro-RO','ru-RU','sk-SK','sl-SI','sq-AL','sr-Cyrl-BA','sr-Cyrl-RS','sr-Latn-RS','sv-SE','ta-IN','te-IN','th-TH','tr-TR','tt-RU','ug-CN','uk-UA','ur-PK','uz-Latn-UZ','vi-VN','zh-CN','zh-Hans','zh-Hant','zh-HK','zh-TW')
foreach($name in $cultures){$dir=Join-Path $release $name;if(Test-Path $dir){Remove-Item $dir -Recurse -Force}}
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
