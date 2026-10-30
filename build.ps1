#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "Building TaskManager++..." -ForegroundColor Cyan

# ── Publish self-contained win-x64 ───────────────────────────────────────────
dotnet publish "$root\TaskManagerPlusPlus.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o "$root\build\app"

Write-Host "Build complete -> $root\build\app" -ForegroundColor Green

# ── Optionally compile installer with Inno Setup ─────────────────────────────
$possibleIsccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$iscc = $possibleIsccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "Building installer using $iscc..." -ForegroundColor Cyan
    & $iscc "$root\installer\TaskManagerPlusPlus.iss"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Installer -> $root\build\installer" -ForegroundColor Green
    } else {
        Write-Warning "ISCC exited with code $LASTEXITCODE"
    }
} else {
    Write-Host "Inno Setup 6 not found, skipping installer build." -ForegroundColor Yellow
    Write-Host "Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
}
