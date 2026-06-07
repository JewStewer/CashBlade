param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "Finora\Finora.csproj"
$nugetConfig = Join-Path $root "NuGet.Config"
$packagesDir = Join-Path $root ".dotnet-home\.nuget\packages"
$intermediateDir = Join-Path $root ".dotnet-home\standalone-obj\"
$appDataDir = Join-Path $root ".dotnet-home\appdata"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "Standalone\$Runtime"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $root $OutputDir
}

if (Test-Path -LiteralPath $OutputDir) {
    $resolvedOutput = (Resolve-Path -LiteralPath $OutputDir).Path
    if (-not $resolvedOutput.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output directory outside the workspace: $resolvedOutput"
    }

    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

$dotnetCandidates = @(
    (Join-Path $root ".dotnet-home\.dotnet-8.0.420\dotnet.exe"),
    (Join-Path $root ".dotnet-home\.dotnet\dotnet.exe"),
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
    "C:\Program Files\dotnet\dotnet.exe",
    "C:\Program Files (x86)\dotnet\dotnet.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$dotnet = $dotnetCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $dotnet) {
    throw "Could not find dotnet.exe. Install the .NET 8 SDK or add dotnet.exe to PATH, then run this script again."
}

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = $packagesDir
$env:APPDATA = $appDataDir

$appDataNuGetDir = Join-Path $appDataDir "NuGet"
if (-not (Test-Path -LiteralPath $appDataNuGetDir)) {
    New-Item -ItemType Directory -Path $appDataNuGetDir | Out-Null
}

& $dotnet restore $project `
    -r $Runtime `
    --configfile $nugetConfig `
    -p:RestorePackagesPath=$packagesDir `
    -p:BaseIntermediateOutputPath=$intermediateDir `
    -p:PublishReadyToRun=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=$($SingleFile.IsPresent.ToString().ToLowerInvariant()) `
    -p:IncludeNativeLibrariesForSelfExtract=$($SingleFile.IsPresent.ToString().ToLowerInvariant()) `
    -p:EnableCompressionInSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:SatelliteResourceLanguages=en `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:RestorePackagesPath=$packagesDir `
    -p:BaseIntermediateOutputPath=$intermediateDir `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exePath = Join-Path $OutputDir "Cashglade.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish completed, but expected executable was not found: $exePath"
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash

Write-Host "Standalone executable created:"
Write-Host $exePath
Write-Host "SHA256: $hash"
if (-not $SingleFile) {
    Write-Host ""
    Write-Host "Fast portable build created. Keep the files in this folder together and open Cashglade.exe."
    Write-Host "For a single large exe instead, rerun with: -SingleFile"
}

$shortcutTargets = @(
    (Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Cashglade.exe - Shortcut.lnk"),
    (Join-Path ([Environment]::GetFolderPath("Programs")) "Cashglade\Cashglade.lnk")
)

$shell = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in $shortcutTargets) {
    $shortcutDir = Split-Path -Parent $shortcutPath
    if (-not (Test-Path -LiteralPath $shortcutDir)) {
        New-Item -ItemType Directory -Path $shortcutDir | Out-Null
    }

    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $OutputDir
    $shortcut.IconLocation = "$exePath,0"
    $shortcut.Save()
    Write-Host "Updated shortcut: $shortcutPath"
}

$oldShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "Finora\Finora.lnk"
if (Test-Path -LiteralPath $oldShortcut) {
    Remove-Item -LiteralPath $oldShortcut -Force
    Write-Host "Removed old shortcut: $oldShortcut"
}
