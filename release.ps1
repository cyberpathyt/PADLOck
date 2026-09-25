# Публикация релиза на GitHub: установщик + APK FreeKiosk из папки kiosk + SHA256SUMS.txt
#   .\release.bat 1.2.0-dev.1   тестовая версия (Pre-release: получают те, у кого включены тестовые версии)
#   .\release.bat 1.2.0         рабочая версия (получают все)
# Репозиторий должен совпадать с AppInfo.UpdateRepository в программе
param([string]$Version)

$Repo = 'cyberpathyt/PADLOck'
# Ошибки проверяются явно: в Windows PowerShell 5.1 режим Stop ломается на выводе gh/dotnet в stderr
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

function Finish([int]$code, [string]$message) {
    Write-Host ""
    if ($code -eq 0) { Write-Host $message -ForegroundColor Green } else { Write-Host $message -ForegroundColor Red }
    Read-Host "Нажмите Enter" | Out-Null
    exit $code
}

if (-not $Version) { $Version = Read-Host "Версия (например 1.2.0 или 1.2.0-dev.1)" }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { Finish 1 "Неверная версия: '$Version'. Пример: 1.2.0 или 1.2.0-dev.1" }
$tag = "v$Version"
$prerelease = $Version.Contains('-')

# GitHub CLI
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Finish 1 "Нужна утилита GitHub CLI: https://cli.github.com (затем один раз: gh auth login)"
}
& gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Finish 1 "Выполните один раз: gh auth login" }

& gh release view $tag --repo $Repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { Finish 1 "Релиз $tag уже есть в $Repo. Укажите версию больше." }

# APK FreeKiosk — ровно один
$apks = @(Get-ChildItem "kiosk\*.apk" -ErrorAction SilentlyContinue)
if ($apks.Count -gt 1) { Finish 1 "В папке kiosk больше одного APK. Оставьте один — тот, которым прошивать." }
if ($apks.Count -eq 0) {
    Write-Host "В папке kiosk нет APK: релиз выйдет без FreeKiosk, у пользователей останется прежний." -ForegroundColor Yellow
    if ((Read-Host "Продолжить? (y/n)") -ne 'y') { Finish 1 "Отменено" }
}

# Что нового
if (-not (Test-Path "release-notes.md") -or -not ("" + (Get-Content "release-notes.md" -Raw)).Trim()) {
    Write-Host "Опишите изменения в release-notes.md — откроется Блокнот. Сохраните и закройте его."
    if (-not (Test-Path "release-notes.md")) { Set-Content "release-notes.md" "- " -Encoding UTF8 }
    Start-Process notepad "release-notes.md" -Wait
}

Write-Host ""
Write-Host "Выпуск $tag $(if ($prerelease) { '(тестовая, Pre-release)' } else { '(рабочая)' })" -ForegroundColor Cyan
if ($apks.Count -eq 1) { Write-Host "FreeKiosk: $($apks[0].Name)" }

# Сборка
& "$PSScriptRoot\build.ps1" -Version $Version -NoPause
if ($LASTEXITCODE -ne 0) { Finish 1 "Сборка не удалась — релиз не опубликован" }

$setup = "dist\PADLOck-Setup-$Version.exe"
if (-not (Test-Path $setup)) { Finish 1 "Не найден $setup" }

# Контрольные суммы
$files = @((Resolve-Path $setup).Path) + @($apks | ForEach-Object FullName)
$files | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), (Split-Path $_ -Leaf) } |
    Set-Content "dist\SHA256SUMS.txt" -Encoding Ascii

# Публикация
Write-Host ""
Write-Host "Публикую $tag в $Repo..." -ForegroundColor Cyan
$ghArgs = @('release', 'create', $tag) + $files + @('dist\SHA256SUMS.txt',
    '--repo', $Repo, '--title', "PADLOck $Version", '--notes-file', 'release-notes.md')
if ($prerelease) { $ghArgs += '--prerelease' }
& gh @ghArgs
if ($LASTEXITCODE -ne 0) { Finish 1 "Публикация не выполнена" }

Finish 0 "Опубликовано: https://github.com/$Repo/releases/tag/$tag`nПрограммы обновятся при следующем запуске. release-notes.md можно очистить для следующей версии."
