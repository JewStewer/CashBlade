$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "LocalPublish"
$project = Join-Path $root "Finora\Finora.csproj"
$manifestPath = Join-Path $root "Finora\Finora.update.json"
$publishManifestPath = Join-Path $publishDir "Finora.update.json"
$exePath = Join-Path $publishDir "Cashglade.exe"
$updaterConfigPath = Join-Path $root "Finora\Finora.updater.json"
$publishUpdaterConfigPath = Join-Path $publishDir "Finora.updater.json"
$installedDir = Join-Path $env:LOCALAPPDATA "Cashglade"
$installedExePath = Join-Path $installedDir "Cashglade.exe"
$installedUpdaterConfigPath = Join-Path $installedDir "Finora.updater.json"

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:AssemblyName=Cashglade `
    -o $publishDir

$version = [xml](Get-Content -Raw -LiteralPath $project)
$appVersion = $version.Project.PropertyGroup.Version
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash

$manifest = [ordered]@{
    version = $appVersion
    executableUrl = $exePath
    sha256 = $hash
}

$manifestJson = $manifest | ConvertTo-Json
Set-Content -LiteralPath $manifestPath -Value $manifestJson
Set-Content -LiteralPath $publishManifestPath -Value $manifestJson

if (-not (Test-Path -LiteralPath $installedDir)) {
    New-Item -ItemType Directory -Path $installedDir | Out-Null
}

$updaterConfigToInstall = if (Test-Path -LiteralPath $publishUpdaterConfigPath) {
    $publishUpdaterConfigPath
}
else {
    $updaterConfigPath
}

Copy-Item -LiteralPath $updaterConfigToInstall -Destination $installedUpdaterConfigPath -Force

$runningCashglade = Get-Process -Name "Cashglade" -ErrorAction SilentlyContinue
if ($runningCashglade) {
    Write-Host "Cashglade is running. The installed exe will update itself on next launch."
}
else {
    Copy-Item -LiteralPath $exePath -Destination $installedExePath -Force
    Write-Host "Updated installed exe: $installedExePath"
}

Write-Host "Published Cashglade $appVersion"
Write-Host "Exe: $exePath"
Write-Host "SHA256: $hash"
