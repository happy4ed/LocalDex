param([string]$Destination = "")
$ErrorActionPreference = "Stop"
$version = "5.0.1"
$sha256 = "B12A2C4EE8BE317422451FC7DCF8EE20A71B5EA7EF9AD73DDD825A25316227A5"
if (!$Destination) { $Destination = Join-Path $PSScriptRoot "../DexManager/tools/scrcpy" }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$temp = Join-Path ([IO.Path]::GetTempPath()) ("localdex-scrcpy-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $zip = Join-Path $temp "scrcpy.zip"
    Invoke-WebRequest "https://github.com/Genymobile/scrcpy/releases/download/v$version/scrcpy-win64-v$version.zip" -OutFile $zip -UseBasicParsing
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $sha256) { throw "scrcpy checksum mismatch" }
    Expand-Archive $zip (Join-Path $temp "unpacked")
    $source = Join-Path $temp "unpacked/scrcpy-win64-v$version"
    if (!(Test-Path (Join-Path $source "scrcpy.exe"))) { throw "Unexpected scrcpy archive layout" }
    # Replace the complete managed runtime so old FFmpeg DLLs are not retained.
    $Destination = [IO.Path]::GetFullPath($Destination)
    New-Item -ItemType Directory -Path (Split-Path $Destination) -Force | Out-Null
    $backup = $Destination + ".previous-" + [Guid]::NewGuid()
    $hadPrevious = Test-Path $Destination
    if ($hadPrevious) { Move-Item $Destination $backup }
    try { Move-Item $source $Destination }
    catch {
        if ($hadPrevious -and !(Test-Path $Destination)) { Move-Item $backup $Destination }
        throw
    }
    if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
    Write-Host "Verified scrcpy $version installed in $Destination"
} finally { Remove-Item $temp -Recurse -Force }
