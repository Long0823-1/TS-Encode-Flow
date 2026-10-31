param([string]$AribSource)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$commit='37dde0f32652c32aa98bf92278944d1cfb2e7354'
if(!$AribSource){
 $AribSource=Join-Path $root '.deps/libaribcaption'
 if(!(Test-Path (Join-Path $AribSource '.git'))){git clone --no-checkout https://github.com/xqq/libaribcaption.git $AribSource;if($LASTEXITCODE -ne 0){throw 'Clone failed'}}
 git -C $AribSource fetch origin $commit --depth 1
 if($LASTEXITCODE -ne 0){throw 'Fetch failed'}
 git -C $AribSource checkout --detach $commit
 if($LASTEXITCODE -ne 0){throw 'Checkout failed'}
}
if((& git -C $AribSource rev-parse HEAD) -ne $commit){throw 'Unexpected libaribcaption revision'}
$cmakeCommand=Get-Command cmake -ErrorAction SilentlyContinue
if($cmakeCommand){$cmake=$cmakeCommand.Source}else{
 $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
 $vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
 $cmake=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
}
& $cmake -S (Join-Path $root 'native') -B (Join-Path $root 'build/native') -A x64 "-DARIBCAPTION_SOURCE=$AribSource"
if($LASTEXITCODE -ne 0){throw 'Native configure failed'}
& $cmake --build (Join-Path $root 'build/native') --config Release --parallel 4
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
