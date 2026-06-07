# publish-all.ps1
# Publishes Finance Blade (WPF + Blazor) and deploys the phone app to Netlify.
# Run from: C:\Users\Fruit\Desktop\Finora\

$dotnet   = "$PSScriptRoot\.dotnet-home\.dotnet\dotnet.exe"
$wpfProj  = "$PSScriptRoot\Finora\Finora.csproj"
$webProj  = "$PSScriptRoot\Finora.Web\Finora.Web.csproj"
$outWpf   = "$PSScriptRoot\Standalone\win-x64"
$outWeb   = "$PSScriptRoot\Standalone\WebApp"
$wwwroot  = "$outWeb\wwwroot"

Write-Host "`n=== Publishing Finance Blade (desktop) ===" -ForegroundColor Cyan
& $dotnet publish $wpfProj -c Release -r win-x64 --self-contained true -o $outWpf
if ($LASTEXITCODE -ne 0) { Write-Host "WPF publish FAILED" -ForegroundColor Red; exit 1 }

Write-Host "`n=== Publishing Finance Blade (phone app) ===" -ForegroundColor Cyan
& $dotnet publish $webProj -c Release -o $outWeb
if ($LASTEXITCODE -ne 0) { Write-Host "Blazor publish FAILED" -ForegroundColor Red; exit 1 }

Write-Host "`n=== Copying phone app into desktop folder ===" -ForegroundColor Cyan
Copy-Item -Recurse -Force $outWeb "$outWpf\WebApp"

# Force TLS 1.2 — required by Netlify (PowerShell 5.1 defaults to TLS 1.0)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ── Netlify deploy ────────────────────────────────────────────────────────────
$netlifyConfig = "$PSScriptRoot\netlify.json"
if (-not (Test-Path $netlifyConfig)) {
    Write-Host "`nSkipping Netlify deploy — netlify.json not found." -ForegroundColor Yellow
    Write-Host "Create netlify.json next to this script with:"
    Write-Host '  { "siteId": "your-site-id", "token": "your-personal-access-token" }'
    Write-Host "`nDone! Restart Finance Blade to use the new build." -ForegroundColor Green
    exit 0
}

$cfg = Get-Content $netlifyConfig -Raw | ConvertFrom-Json
if (-not $cfg.siteId -or -not $cfg.token) {
    Write-Host "`nnetlify.json is missing siteId or token — skipping Netlify deploy." -ForegroundColor Yellow
    exit 0
}

Write-Host "`n=== Deploying to Netlify ===" -ForegroundColor Cyan
$zip = "$env:TEMP\finblade-web.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$wwwroot\*" -DestinationPath $zip

try {
    $curlExe = "$env:SystemRoot\System32\curl.exe"
    if (-not (Test-Path $curlExe)) { $curlExe = "curl" }

    $respText = & $curlExe -s -X POST `
        -H "Authorization: Bearer $($cfg.token)" `
        -H "Content-Type: application/zip" `
        --data-binary "@$zip" `
        "https://api.netlify.com/api/v1/sites/$($cfg.siteId)/deploys"

    $resp = $respText | ConvertFrom-Json
    if ($resp.id) {
        $deployUrl = if ($resp.ssl_url) { $resp.ssl_url } else { $resp.url }
        Write-Host "Netlify deployed successfully!" -ForegroundColor Green
        Write-Host "URL: $deployUrl" -ForegroundColor Green
    } else {
        Write-Host "Netlify deploy failed: $respText" -ForegroundColor Red
    }
} catch {
    Write-Host "Netlify deploy failed: $_" -ForegroundColor Red
}

Write-Host "`nDone! Restart Finance Blade to use the new build." -ForegroundColor Green
