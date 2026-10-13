param([string]$Version = '', [string]$Compiler = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $repo 'dist/LocalDex'
$exe = Join-Path $package 'LocalDex.exe'
if (!(Test-Path $exe)) { throw 'Run Package-Release.ps1 before building the installer.' }
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if (!$Version) { $Version = $actualVersion }
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$' -or $Version -ne $actualVersion) {
    throw 'Installer version must match the packaged executable.'
}
$privateFiles = Get-ChildItem $package -File -Recurse | Where-Object {
    $_.Name -eq 'settings.json' -or $_.Name -like '*.recovery' -or
    $_.Extension -in @('.pdb', '.log', '.keystore')
}
if ($privateFiles) { throw 'The package contains runtime data or development artifacts.' }
if (!$Compiler) {
    $Compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
}
if (!(Test-Path $Compiler)) { throw 'Inno Setup 6 is required. Supply -Compiler with its ISCC.exe path.' }
& $Compiler "/DAppVersion=$Version" "/DPackageDir=$package" "/DOutputDir=$(Join-Path $repo 'dist')" (Join-Path $repo 'build/windows/LocalDex.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
