param([switch]$SkipPublish)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent

# Build the same publish output the ZIP ships, unless it already exists.
if(-not $SkipPublish){ & (Join-Path $PSScriptRoot 'publish.ps1') }
$release=Join-Path $root 'build/release'
if(-not (Test-Path (Join-Path $release 'TS-Encode-Flow.exe'))){ throw 'Publish output is missing TS-Encode-Flow.exe. Run scripts/publish.ps1 first.' }

$version=(Select-String -Path (Join-Path $root 'src/App/EncodeFlow.App.csproj') -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
$installerProject=Join-Path $root 'installer/TS-Encode-Flow.Installer.wixproj'
dotnet build $installerProject -c Release -p:Version=$version -p:ReleaseDir=$release --nologo
if($LASTEXITCODE -ne 0){ throw 'MSI build failed' }

$msi=Join-Path $root "installer/bin/Release/TS-Encode-Flow-$version-win-x64.msi"
if(-not (Test-Path $msi)){ throw "MSI was not produced at $msi" }
$hash=(Get-FileHash $msi -Algorithm SHA256).Hash

# Read the generated ProductCode back from the MSI so the winget manifest is exact.
$productCode=''
try {
 $windowsInstaller=New-Object -ComObject WindowsInstaller.Installer
 $database=$windowsInstaller.OpenDatabase($msi,0)
 $view=$database.OpenView("SELECT Value FROM Property WHERE Property='ProductCode'")
 $view.Execute()
 $record=$view.Fetch()
 if($record){ $productCode=$record.StringData(1) }
}catch{ Write-Warning "ProductCode could not be read from the MSI: $_" }

# Generate the winget manifest set (v1.6 schema) for microsoft/winget-pkgs.
$winget=Join-Path $root 'build/winget'
New-Item -ItemType Directory -Force $winget | Out-Null
$url="https://github.com/Long0823-1/TS-Encode-Flow/releases/download/v$version/TS-Encode-Flow-$version-win-x64.msi"
@"
# Created with YamlCreate.ps1 equivalent generation by scripts/build-msi.ps1
PackageIdentifier: Long0823-1.TSEncodeFlow
PackageVersion: $version
DefaultLocale: ja-JP
ManifestType: version
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $winget 'Long0823-1.TSEncodeFlow.yaml') -Encoding UTF8
@"
PackageIdentifier: Long0823-1.TSEncodeFlow
PackageVersion: $version
InstallerLocale: ja-JP
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.17763.0
InstallerType: msi
Scope: user
ProductCode: '$productCode'
UpgradeBehavior: install
Protocols:
- ts-encode-flow
FileExtensions:
- ts
- m2ts
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $hash
  InstallerSwitches:
    Silent: /quiet /norestart
    SilentWithProgress: /passive /norestart
ManifestType: installer
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $winget 'Long0823-1.TSEncodeFlow.installer.yaml') -Encoding UTF8
@"
PackageIdentifier: Long0823-1.TSEncodeFlow
PackageVersion: $version
PackageLocale: ja-JP
Publisher: Long0823-1
PublisherUrl: https://github.com/Long0823-1
PackageName: TS Encode Flow
PackageUrl: https://github.com/Long0823-1/TS-Encode-Flow
License: MIT
ShortDescription: 録画TSをMP4・MKVへ変換するWindowsアプリ
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $winget 'Long0823-1.TSEncodeFlow.locale.ja-JP.yaml') -Encoding UTF8

Write-Output "MSI: $msi"
Write-Output "SHA256: $hash"
Write-Output "ProductCode: $productCode"
Write-Output "Winget manifests: $winget"
