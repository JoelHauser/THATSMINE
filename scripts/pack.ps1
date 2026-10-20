<#
.SYNOPSIS
    Builds THATSMINE, checks its version, packs the release zip, and optionally installs it.

.DESCRIPTION
    Run this through PowerShell, not Bash: Bash mangles 'H:\SPT4.1.X' into 'H:SPT4.1.X'.

    Each zip is unpacked over the SPT root, so it carries the full path BepInEx/plugins/... .
    THATSMINE-<v>.zip holds ThatsMine.dll, all a solo player needs. THATSMINE-Fika-<v>.zip holds
    only the Fika add-on. Neither has a README or other docs.
    Entries are written by hand with forward slashes: PowerShell 5.1's Compress-Archive writes
    backslashes, which some extractors turn into file names instead of folders.

.PARAMETER SPTPath
    The SPT install root to compile against (and install into, with -Install).

.PARAMETER FikaCorePath
    Fika.Core.dll to compile ThatsMine.Fika against. Defaults to the install's
    BepInEx\plugins\Fika\Fika.Core.dll; pass one from a Fika release zip when Fika is not installed.
    If none is found, only the solo zip is built.

.PARAMETER Install
    Also copy ThatsMine.dll into the install's BepInEx\plugins, plus the Fika add-on only if that
    install has Fika.

.EXAMPLE
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -FikaCorePath C:\somewhere\Fika.Core.dll -Install
#>
param(
    [Parameter(Mandatory = $true)][string] $SPTPath,
    [string] $FikaCorePath,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ThatsMine\ThatsMine.csproj'
$fikaProject = Join-Path $root 'src\ThatsMine.Fika\ThatsMine.Fika.csproj'
$source = Join-Path $root 'src\ThatsMine\ThatsMinePlugin.cs'
if (-not $FikaCorePath) { $FikaCorePath = Join-Path $SPTPath 'BepInEx\plugins\Fika\Fika.Core.dll' }
$haveFika = Test-Path $FikaCorePath

if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\core\BepInEx.dll'))) {
    throw "SPTPath '$SPTPath' is not an SPT install root: no BepInEx\core\BepInEx.dll."
}

# ---- version agreement -----------------------------------------------------------
# The version lives in two places and they have to agree, or a release ships a DLL whose
# logged version is not the one on the zip.
$csprojVersion = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
$sourceVersion = (Select-String -Path $source -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
$fikaVersion = ([xml](Get-Content $fikaProject)).Project.PropertyGroup.Version | Where-Object { $_ }
if ($csprojVersion -ne $sourceVersion -or $fikaVersion -ne $sourceVersion) {
    throw "Version mismatch: ThatsMine.csproj '$csprojVersion', ThatsMine.Fika.csproj '$fikaVersion', ThatsMinePlugin.PluginVersion '$sourceVersion'."
}
$version = $csprojVersion
Write-Host "Version $version" -ForegroundColor Cyan

# ---- build -----------------------------------------------------------------------
$built = Join-Path $root 'src\ThatsMine\bin\Release\ThatsMine.dll'
$builtFika = Join-Path $root 'src\ThatsMine.Fika\bin\Release\ThatsMine.Fika.dll'
if ($haveFika) {
    # Building the Fika project builds ThatsMine too, through its project reference.
    dotnet build $fikaProject -c Release "-p:SPTPath=$SPTPath" "-p:FikaCorePath=$FikaCorePath" --nologo
}
else {
    dotnet build $project -c Release "-p:SPTPath=$SPTPath" --nologo
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# ---- zips ------------------------------------------------------------------------
# Two zips on purpose. The main one is all a solo player needs. The Fika add-on is its own
# download: without Fika, BepInEx refuses to load it and SPT reports "1 plugin failed to load".
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

function New-ModZip([string] $zip, $entries) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($e in $entries.GetEnumerator()) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e.Value, $e.Key, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    $check = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $names = $check.Entries | ForEach-Object { $_.FullName }
    }
    finally {
        $check.Dispose()
    }
    $missing = $entries.Keys | Where-Object { $names -notcontains $_ }
    if ($missing) { throw "$zip is missing: $($missing -join ', ')" }
    $extra = $names | Where-Object { $entries.Keys -notcontains $_ }
    if ($extra) { throw "$zip has unexpected entries: $($extra -join ', ')" }
    Write-Host "Packed $zip" -ForegroundColor Green
    $names | ForEach-Object { Write-Host "  $_" }
}

New-ModZip (Join-Path $dist "THATSMINE-$version.zip") ([ordered]@{
    'BepInEx/plugins/ThatsMine.dll' = $built
})
if ($haveFika) {
    New-ModZip (Join-Path $dist "THATSMINE-Fika-$version.zip") ([ordered]@{
        'BepInEx/plugins/ThatsMine.Fika.dll' = $builtFika
    })
}
else {
    Write-Host "No Fika.Core.dll, so the Fika add-on was not built or packed." -ForegroundColor Yellow
}

# ---- install ---------------------------------------------------------------------
# The Fika add-on goes in only when that install has Fika; otherwise a stale copy is removed,
# since it would only fail to load.
if ($Install) {
    $dest = Join-Path $SPTPath 'BepInEx\plugins'
    Copy-Item $built $dest -Force
    Write-Host "Installed ThatsMine.dll to $dest" -ForegroundColor Green

    $installedFika = Join-Path $dest 'ThatsMine.Fika.dll'
    if (Test-Path (Join-Path $dest 'Fika\Fika.Core.dll')) {
        if ($haveFika) {
            Copy-Item $builtFika $dest -Force
            Write-Host "Fika is installed: installed ThatsMine.Fika.dll too" -ForegroundColor Green
        }
    }
    elseif (Test-Path $installedFika) {
        Remove-Item $installedFika -Force
        Write-Host "Fika is not installed: removed ThatsMine.Fika.dll" -ForegroundColor Yellow
    }
}
