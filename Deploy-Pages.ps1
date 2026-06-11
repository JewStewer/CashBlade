# Deploy-Pages.ps1
# Publishes Finora.Web (the iPhone PWA) straight into docs/ for GitHub Pages -
# independent of the WPF desktop build. Run from: C:\Users\Fruit\Desktop\Finora\
#
# This only stages docs/ via "git add" - review the diff, then commit and push
# to update the live public PWA.

$ErrorActionPreference = "Stop"

# git prints harmless notices (e.g. "LF will be replaced by CRLF") to stderr.
# Under $ErrorActionPreference = "Stop", PowerShell can turn those into a
# terminating NativeCommandError if the caller's invocation merges streams.
# Run git with errors temporarily non-terminating so those notices don't abort the script.
function Invoke-GitQuiet {
    param([Parameter(ValueFromRemainingArguments)] [string[]]$GitArgs)
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & git @GitArgs } finally { $ErrorActionPreference = $prevEAP }
}

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "Finora.Web\Finora.Web.csproj"
$tmpOut  = Join-Path $root ".tmp-pages-site"
$docsDir = Join-Path $root "docs"

$dotnetCandidates = @(
    (Join-Path $root ".dotnet-home\.dotnet\dotnet.exe"),
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
    "C:\Program Files\dotnet\dotnet.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$dotnet = $dotnetCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $dotnet) {
    throw "Could not find dotnet.exe. Install the .NET 8 SDK or add dotnet.exe to PATH, then run this script again."
}

if (Test-Path -LiteralPath $tmpOut) {
    Remove-Item -LiteralPath $tmpOut -Recurse -Force
}

Write-Host "`n=== Publishing Finora.Web for GitHub Pages ===" -ForegroundColor Cyan
& $dotnet publish $project -c Release -o $tmpOut
if ($LASTEXITCODE -ne 0) {
    throw "Blazor publish failed"
}

$wwwroot = Join-Path $tmpOut "wwwroot"
if (-not (Test-Path -LiteralPath $wwwroot)) {
    throw "Publish completed, but expected output was not found: $wwwroot"
}

Write-Host "`n=== Replacing docs/ with the fresh build ===" -ForegroundColor Cyan

# Remove the old tree from git's index (and disk) before copying the new one.
# Doing this avoids stale-cased leftovers (e.g. Finora.Web.wasm vs
# finora.web.wasm) that git's case-insensitive index on Windows can otherwise
# hide, which previously caused "Could not find assembly" on Pages.
Push-Location $root
try {
    Invoke-GitQuiet rm -r -q -f --ignore-unmatch docs | Out-Null
}
finally {
    Pop-Location
}
if (Test-Path -LiteralPath $docsDir) {
    Remove-Item -LiteralPath $docsDir -Recurse -Force
}

Copy-Item -LiteralPath $wwwroot -Destination $docsDir -Recurse

# dotnet publish doesn't emit .nojekyll, but GitHub Pages needs it - without it,
# Jekyll processing drops underscore-prefixed folders like _framework entirely.
New-Item -ItemType File -Path (Join-Path $docsDir ".nojekyll") -Force | Out-Null

# WPF serves the app from "/", but GitHub Pages serves docs/ from a repo
# subpath - switch to a relative base href so asset URLs resolve there too.
$indexPath = Join-Path $docsDir "index.html"
$indexHtml = (Get-Content -LiteralPath $indexPath -Raw).Replace('<base href="/" />', '<base href="./" />')
if ($indexHtml -notmatch '<base href="\./" />') {
    Write-Warning "docs/index.html does not have a relative base href - GitHub Pages assets may 404. Check the base tag manually."
}
Set-Content -LiteralPath $indexPath -Value $indexHtml -NoNewline

# GitHub Pages serves 404.html for unknown routes; the Blazor router takes it from there.
Copy-Item -LiteralPath $indexPath -Destination (Join-Path $docsDir "404.html") -Force

Push-Location $root
try {
    Invoke-GitQuiet add docs
}
finally {
    Pop-Location
}

Remove-Item -LiteralPath $tmpOut -Recurse -Force

Write-Host "`ndocs/ updated from a fresh Finora.Web build and staged in git." -ForegroundColor Green
Write-Host "Review with 'git status' / 'git diff --stat docs', then commit and push to update the live iOS app." -ForegroundColor Green
