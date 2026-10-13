param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$Installer = [IO.Path]::GetFullPath($Installer)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('LocalDex setup test ' + [Guid]::NewGuid())
$installDir = Join-Path $testRoot 'LocalDex'
New-Item $testRoot -ItemType Directory | Out-Null
function Run-Setup([string]$path, [string]$arguments) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode)" }
}
try {
    $argsText = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="" /DIR="' + $installDir + '" /LOG="' + (Join-Path $testRoot 'install.log') + '"'
    Run-Setup $Installer $argsText
    foreach ($file in @('LocalDex.exe', 'ko/LocalDex.resources.dll', 'tools/scrcpy/scrcpy.exe', 'tools/adb-proxy/LocalDexAdbProxy.exe', 'unins000.exe')) {
        if (!(Test-Path (Join-Path $installDir $file))) { throw "Missing installed file: $file" }
    }
    $saved = @{
        'config/settings.json' = '{"preserve":"user settings"}';
        'config/freeform-recovery/test.recovery' = 'preserve phone recovery';
        'screenshot/keep.txt' = 'preserve captures'
    }
    foreach ($entry in $saved.GetEnumerator()) {
        $target = Join-Path $installDir $entry.Key
        New-Item (Split-Path $target) -ItemType Directory -Force | Out-Null
        [IO.File]::WriteAllText($target, $entry.Value)
    }
    Run-Setup $Installer $argsText
    foreach ($entry in $saved.GetEnumerator()) {
        if ([IO.File]::ReadAllText((Join-Path $installDir $entry.Key)) -ne $entry.Value) { throw 'Upgrade changed user data.' }
    }
    & (Join-Path $installDir 'tools/adb-proxy/LocalDexAdbProxy.exe') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Bundled proxy self-test failed.' }
    Run-Setup (Join-Path $installDir 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    if (Test-Path (Join-Path $installDir 'LocalDex.exe')) { throw 'Uninstall left the application executable behind.' }
    foreach ($entry in $saved.GetEnumerator()) {
        if ([IO.File]::ReadAllText((Join-Path $installDir $entry.Key)) -ne $entry.Value) { throw 'Uninstall removed user data.' }
    }
    Write-Host 'Installer fresh install, upgrade, helper self-test and uninstall passed; user data preserved.'
} finally {
    # Only our uniquely named temporary test directory is removed.
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
