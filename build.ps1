# Сборка установщика PADLOck
#   .\build.bat 1.2.0          рабочая версия
#   .\build.bat 1.2.0-dev.1    тестовая версия
param(
    [string]$Version,
    [switch]$NoPause
)

# Ошибки проверяются явно: в Windows PowerShell 5.1 режим Stop ломается на выводе gh/dotnet в stderr
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

function Finish([int]$code, [string]$message) {
    Write-Host ""
    if ($code -eq 0) { Write-Host $message -ForegroundColor Green } else { Write-Host $message -ForegroundColor Red }
    if (-not $NoPause) { Read-Host "Нажмите Enter" | Out-Null }
    exit $code
}

if (-not $Version) { $Version = Read-Host "Версия (например 1.2.0 или 1.2.0-dev.1)" }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { Finish 1 "Неверная версия: '$Version'. Пример: 1.2.0 или 1.2.0-dev.1" }
$numVersion = $Version.Split('-')[0]

Write-Host "=== PADLOck $Version · vvedyaev ===" -ForegroundColor Cyan

# .NET 10 SDK
function Test-Sdk { try { (& dotnet --list-sdks 2>$null) -match '^10\.' } catch { $false } }
if (-not (Test-Sdk)) {
    Write-Host ".NET 10 SDK не найден, устанавливаю через winget..."
    winget install -e --id Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
    $env:Path += ";$env:ProgramFiles\dotnet"
    if (-not (Test-Sdk)) { Finish 1 "Не удалось установить .NET 10 SDK. Установите его вручную: https://dotnet.microsoft.com/download" }
}

# adb
if (-not (Test-Path "tools\adb.exe")) {
    Write-Host "Скачиваю Android platform-tools..."
    try {
        $zip = Join-Path $env:TEMP 'platform-tools.zip'
        $dir = Join-Path $env:TEMP 'padlock-pt'
        Invoke-WebRequest 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip' -OutFile $zip -UseBasicParsing -ErrorAction Stop
        Expand-Archive $zip $dir -Force -ErrorAction Stop
        New-Item -ItemType Directory -Force tools | Out-Null
        Copy-Item "$dir\platform-tools\adb.exe", "$dir\platform-tools\AdbWinApi.dll", "$dir\platform-tools\AdbWinUsbApi.dll" tools -Force -ErrorAction Stop
    } catch {
        Finish 1 "Не удалось скачать adb. Положите adb.exe, AdbWinApi.dll и AdbWinUsbApi.dll в папку tools вручную."
    }
}

Write-Host ""
Write-Host "[1/3] Сборка программы..." -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
& dotnet publish PADLOck.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o publish
if ($LASTEXITCODE -ne 0) { Finish 1 "Сборка программы завершилась с ошибкой" }

Write-Host ""
Write-Host "[2/3] Подготовка файлов..." -ForegroundColor Cyan
if (Test-Path "kiosk-profile.default.json") { Copy-Item "kiosk-profile.default.json" publish -Force }
Copy-Item "Инструкция.txt" publish -Force

Write-Host ""
Write-Host "[3/3] Сборка установщика..." -ForegroundColor Cyan
function Find-Iscc {
    @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
      "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
      "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "Inno Setup не найден, устанавливаю через winget..."
    winget install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
    $iscc = Find-Iscc
    if (-not $iscc) { Finish 1 "Не найден Inno Setup. Установите его: https://jrsoftware.org/isdl.php" }
}
& $iscc /Q "/DAppVersion=$Version" "/DNumVersion=$numVersion" "installer\PADLOck.iss"
if ($LASTEXITCODE -ne 0) { Finish 1 "Сборка установщика завершилась с ошибкой" }

Finish 0 "Готово: $PSScriptRoot\dist\PADLOck-Setup-$Version.exe"
