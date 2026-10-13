param(
    [string]$Version = "",
    [switch]$SkipBuild,
    [string]$TargetFrameworkRootPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repoRoot "DexManager.sln"
$releaseRoot = Join-Path $repoRoot "DexManager\bin\Release"
$distRoot = Join-Path $repoRoot "dist"
$packageRoot = Join-Path $distRoot "LocalDex"
$companionApkSource = Join-Path $repoRoot `
    "DXDisplayCleanup\app\build\outputs\apk\release\app-release.apk"
$companionApkSha256 = `
    "7CD40017789E22440DCA0291AB0C45ADB564A19D8A623E669F373395536B880F"

function Assert-ChildPath([string]$Parent, [string]$Child) {
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd([char[]]@('\', '/')) + [IO.Path]::DirectorySeparatorChar
    $childPath = [IO.Path]::GetFullPath($Child)
    if (!$childPath.StartsWith(
        $parentPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the distribution folder: $childPath"
    }
}

function Find-MSBuild {
    $fromPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found) { return $found }
    }
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw "MSBuild was not found. Build Release in Visual Studio, then run this script with -SkipBuild."
}

if (Get-Process LocalDex -ErrorAction SilentlyContinue) {
    throw "LocalDex is running. Exit it before creating a release package."
}

$releaseAdbPath = Join-Path $releaseRoot "tools\scrcpy\adb.exe"
$releaseAdbProcesses = Get-Process adb -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            [string]::Equals(
                [IO.Path]::GetFullPath($_.Path),
                [IO.Path]::GetFullPath($releaseAdbPath),
                [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    }
if ($releaseAdbProcesses -and (Test-Path -LiteralPath $releaseAdbPath)) {
    & $releaseAdbPath kill-server
    Start-Sleep -Milliseconds 300
    $remainingReleaseAdb = Get-Process adb -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                [string]::Equals(
                    [IO.Path]::GetFullPath($_.Path),
                    [IO.Path]::GetFullPath($releaseAdbPath),
                    [StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        }
    if ($remainingReleaseAdb) {
        throw "The bundled ADB server is still running. Stop it before packaging."
    }
}

if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot "Update-Scrcpy.ps1")
    & (Join-Path $PSScriptRoot "Prepare-Companion.ps1")
    $msbuild = Find-MSBuild
    $arguments = @(
        $solutionPath,
        "/t:Rebuild",
        "/p:Configuration=Release",
        "/m"
    )
    $frameworkRoot = $TargetFrameworkRootPath
    if ([string]::IsNullOrWhiteSpace($frameworkRoot)) {
        $frameworkRoot = $env:DXM_TARGET_FRAMEWORK_ROOT
    }
    if ([string]::IsNullOrWhiteSpace($frameworkRoot)) {
        $frameworkRoot = Join-Path $repoRoot ".build-tools\net462\build"
    }
    if (Test-Path -LiteralPath $frameworkRoot) {
        $frameworkRoot = [IO.Path]::GetFullPath($frameworkRoot)
        $referenceAssembly = Join-Path $frameworkRoot `
            ".NETFramework\v4.6.2\mscorlib.dll"
        if (!(Test-Path -LiteralPath $referenceAssembly -PathType Leaf)) {
            throw "The supplied .NET Framework root does not contain the v4.6.2 reference assemblies: $frameworkRoot"
        }
        $arguments += "/p:TargetFrameworkRootPath=$frameworkRoot"
    }

    & $msbuild @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

# The ADB helper is a separate .NET 8 process, published with its own runtime.
# No additional .NET 8 installation is required on the target Windows PC.
if (!$SkipBuild) {
    & dotnet publish (Join-Path $repoRoot "DexManager.AdbProxy/DexManager.AdbProxy.csproj") `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $releaseRoot "tools/adb-proxy")
    if ($LASTEXITCODE -ne 0) { throw "LocalDex ADB helper publish failed." }
}

$requiredOutput = @(
    "LocalDex.exe",
    "LocalDex.exe.config",
    "ko",
    "licenses",
    "tools"
)
foreach ($item in $requiredOutput) {
    $path = Join-Path $releaseRoot $item
    if (!(Test-Path -LiteralPath $path)) {
        throw "Required Release output is missing: $path"
    }
}
$requiredFiles = @(
    "ko\LocalDex.resources.dll",
    "tools\adb-proxy\LocalDexAdbProxy.exe",
    "tools\adb\legacy\adb.exe",
    "tools\adb\legacy\AdbWinApi.dll",
    "tools\adb\legacy\AdbWinUsbApi.dll",
    "tools\adb\legacy\libwinpthread-1.dll",
    "tools\scrcpy\scrcpy.exe",
    "tools\scrcpy\scrcpy-server",
    "tools\scrcpy\adb.exe",
    "tools\scrcpy\AdbWinApi.dll",
    "tools\scrcpy\AdbWinUsbApi.dll",
    "tools\scrcpy\SDL3.dll",
    "tools\scrcpy\avcodec-63.dll",
    "tools\scrcpy\avformat-63.dll",
    "tools\scrcpy\avutil-61.dll",
    "tools\scrcpy\swresample-7.dll",
    "tools\scrcpy\libusb-1.0.dll",
    "licenses\DX-Manager-MIT-LICENSE.txt",
    "licenses\THIRD_PARTY_NOTICES.md",
    "licenses\scrcpy-LICENSE.txt",
    "licenses\dotnet-LICENSE.txt",
    "licenses\dotnet-THIRD-PARTY-NOTICES.txt",
    "licenses\LGPL-2.1-LICENSE.txt",
    "licenses\MinGW-w64-winpthreads-LICENSE.txt",
    "licenses\SDL3-LICENSE.txt",
    "licenses\zlib-LICENSE.txt",
    "licenses\dav1d-LICENSE.txt"
)
foreach ($item in $requiredFiles) {
    $path = Join-Path $releaseRoot $item
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Release file is missing: $path"
    }
}

if (!(Test-Path -LiteralPath $companionApkSource -PathType Leaf)) {
    throw "The signed DX Companion Release APK is missing: $companionApkSource"
}
$actualCompanionHash = (Get-FileHash `
    -LiteralPath $companionApkSource `
    -Algorithm SHA256).Hash
if (![string]::Equals(
    $actualCompanionHash,
    $companionApkSha256,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "The signed DX Companion APK hash does not match the verified 2.0.0 build."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $assemblyInfo = Get-Content -LiteralPath (Join-Path $repoRoot "DexManager\Properties\AssemblyInfo.cs")
    $versionLine = $assemblyInfo | Where-Object {
        $_ -match 'AssemblyInformationalVersion\("([^"]+)"\)'
    } | Select-Object -First 1
    if (!$versionLine -or $versionLine -notmatch 'AssemblyInformationalVersion\("([^"]+)"\)') {
        throw "AssemblyInformationalVersion was not found."
    }
    $Version = $Matches[1]
}

if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') {
    throw "The version contains unsupported filename characters: $Version"
}

$zipPath = Join-Path $distRoot "LocalDex-v$Version-win-x64.zip"
Assert-ChildPath $distRoot $packageRoot
Assert-ChildPath $distRoot $zipPath

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$packageConfig = Join-Path $packageRoot "config"
New-Item -ItemType Directory -Path $packageConfig -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "DexManager\config\README.txt") `
    -Destination $packageConfig

foreach ($item in $requiredOutput) {
    Copy-Item -LiteralPath (Join-Path $releaseRoot $item) -Destination $packageRoot -Recurse -Force
}

$packageCompanionDirectory = Join-Path $packageRoot "tools\companion"
New-Item -ItemType Directory -Path $packageCompanionDirectory -Force |
    Out-Null
Copy-Item -LiteralPath $companionApkSource `
    -Destination (Join-Path $packageCompanionDirectory "DX-Companion.apk")

# Release builds create a PDB for the managed ADB helper under tools. Keep
# debugging symbols out of the public portable package.
Get-ChildItem -LiteralPath $packageRoot -Filter "*.pdb" -File -Recurse |
    Remove-Item -Force
$packageReadme = Join-Path $packageRoot "README.md"
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\PACKAGE_README.md") `
    -Destination $packageReadme
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $packageRoot

$packageDocs = Join-Path $packageRoot "docs"
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
foreach ($document in @(
    "USER_GUIDE_EN.md",
    "USER_GUIDE_KO.md",
    "FAQ_EN.md",
    "FAQ_KO.md",
    "LOCALDEX_ONEUI.md",
    "LOCALDEX_BUILD.md"
)) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\$document") -Destination $packageDocs
}
$imageRoot = Join-Path $repoRoot "docs\images"
if (Test-Path -LiteralPath $imageRoot) {
    Copy-Item -LiteralPath $imageRoot -Destination $packageDocs -Recurse -Force
}

# The repository user guides link to a source-tree license path. Rewrite only
# the generated package copies so their local links also work after extraction.
$packageMarkdownFiles = @($packageReadme) + @(
    Get-ChildItem -LiteralPath $packageDocs -Filter "*.md" -File |
        ForEach-Object { $_.FullName }
)
# A BOM keeps the Korean sections readable in the Windows 7 version of
# Notepad while remaining valid UTF-8 for Markdown viewers.
$packageUtf8 = New-Object Text.UTF8Encoding($true)
foreach ($markdownPath in $packageMarkdownFiles) {
    $markdown = [IO.File]::ReadAllText($markdownPath)
    $markdown = $markdown.Replace(
        "DexManager/licenses/THIRD_PARTY_NOTICES.md",
        "licenses/THIRD_PARTY_NOTICES.md")
    [IO.File]::WriteAllText($markdownPath, $markdown, $packageUtf8)
}

Get-ChildItem -LiteralPath $packageRoot -Filter ".gitkeep" -File -Recurse |
    Remove-Item -Force

$unexpectedPdb = Get-ChildItem -LiteralPath $packageRoot -Filter "*.pdb" `
    -File -Recurse | Select-Object -First 1
if ($unexpectedPdb) {
    throw "Debug symbols must not be included in the package: $($unexpectedPdb.FullName)"
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Release folder: $packageRoot"
Write-Host "Release archive: $zipPath"
