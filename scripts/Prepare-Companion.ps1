$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$target = Join-Path $repoRoot "DXDisplayCleanup/app/build/outputs/apk/release/app-release.apk"
$expected = "7CD40017789E22440DCA0291AB0C45ADB564A19D8A623E669F373395536B880F"
if (Test-Path $target) {
    if ((Get-FileHash $target -Algorithm SHA256).Hash -ne $expected) { throw "Existing Companion APK does not match the verified upstream build." }
    return
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$temp = Join-Path ([IO.Path]::GetTempPath()) ("localdex-companion-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $archive = Join-Path $temp "upstream.zip"
    Invoke-WebRequest "https://github.com/maze-mei/DX-Manager/releases/download/v2.0.1/DX-Manager-v2.0.1-win-x64.zip" -OutFile $archive -UseBasicParsing
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\', '/').EndsWith('/tools/companion/DX-Companion.apk') })
        if ($entry.Count -ne 1) { throw "Unexpected Companion archive layout" }
        $apk = Join-Path $temp "verified.apk"
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $apk)
    } finally { $zip.Dispose() }
    if ((Get-FileHash $apk -Algorithm SHA256).Hash -ne $expected) { throw "Companion checksum mismatch" }
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item $apk $target
} finally { Remove-Item $temp -Recurse -Force }
