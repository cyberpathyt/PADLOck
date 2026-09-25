# Публикация релиза на GitHub: установщик + APK FreeKiosk из папки kiosk + SHA256SUMS.txt
#   .\release.bat 1.2.0-dev.1   тестовая версия (Pre-release: получают те, у кого включены тестовые версии)
#   .\release.bat 1.2.0         рабочая версия (получают все)
# Репозиторий должен совпадать с AppInfo.UpdateRepository в программе
param([string]$Version)

$Repo = 'cyberpathyt/PADLOck'
# Ошибки проверяются явно: в Windows PowerShell 5.1 режим Stop ломается на выводе gh/dotnet в stderr
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

# Сравнение версий по правилам SemVer: 1.2.0-dev.2 < 1.2.0-dev.10 < 1.2.0 < 1.2.1
function Compare-SemVer([string]$a, [string]$b) {
    $pa = $a.Split('-', 2); $pb = $b.Split('-', 2)
    $c = ([version]$pa[0]).CompareTo([version]$pb[0])
    if ($c -ne 0) { return [Math]::Sign($c) }
    $ra = if ($pa.Count -gt 1) { $pa[1] } else { '' }
    $rb = if ($pb.Count -gt 1) { $pb[1] } else { '' }
    if ($ra -eq $rb) { return 0 }
    if (-not $ra) { return 1 }
    if (-not $rb) { return -1 }
    $xa = $ra.Split('.'); $xb = $rb.Split('.')
    for ($i = 0; $i -lt [Math]::Min($xa.Count, $xb.Count); $i++) {
        $ia = 0; $ib = 0
        $na = [int]::TryParse($xa[$i], [ref]$ia); $nb = [int]::TryParse($xb[$i], [ref]$ib)
        if ($na -and $nb) { if ($ia -ne $ib) { return [Math]::Sign($ia - $ib) } }
        elseif ($na) { return -1 }
        elseif ($nb) { return 1 }
        else { $c = [string]::CompareOrdinal($xa[$i], $xb[$i]); if ($c -ne 0) { return [Math]::Sign($c) } }
    }
    return [Math]::Sign($xa.Count - $xb.Count)
}

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
# gh auth token только читает сохранённый вход, без обращения к сети (auth status проверяет его через интернет и может ошибаться)
$null = & gh auth token 2>$null
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

# Что нового: release-notes.md открывается в Блокноте каждый раз — отредактируйте, сохраните и закройте
if (-not (Test-Path "release-notes.md")) { Set-Content "release-notes.md" "- " -Encoding UTF8 }
Write-Host "Откроется Блокнот с release-notes.md — напишите, что нового в $tag, сохраните и закройте его." -ForegroundColor Yellow
Start-Process notepad "`"$PSScriptRoot\release-notes.md`"" -Wait
if (-not ("" + (Get-Content "release-notes.md" -Raw -Encoding UTF8)).Trim().Trim('-').Trim()) { Finish 1 "release-notes.md пустой — релиз не опубликован" }

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

# Описание версии для программ: updates/stable.json и updates/dev.json в репозитории.
# Программа читает их вместо API GitHub (у API лимит 60 запросов в час на внешний адрес)
Write-Host ""
Write-Host "Описание версии для программ..." -ForegroundColor Cyan
$download = "https://github.com/$Repo/releases/download/$tag"
function Asset-Info([string]$path) {
    $f = Get-Item $path
    [ordered]@{
        name   = $f.Name
        url    = "$download/$([uri]::EscapeDataString($f.Name))"
        size   = $f.Length
        sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
    }
}
$manifest = [ordered]@{
    version    = $Version
    tag        = $tag
    prerelease = $prerelease
    notes      = ("" + (Get-Content "release-notes.md" -Raw -Encoding UTF8)).Trim()
    page       = "https://github.com/$Repo/releases/tag/$tag"
    installer  = Asset-Info $setup
    apk        = if ($apks.Count -eq 1) { Asset-Info $apks[0].FullName } else { $null }
}
$json = $manifest | ConvertTo-Json -Depth 5
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force "updates" | Out-Null

$changed = @()
if (-not $prerelease) {
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot "updates/stable.json"), $json, $utf8)
    $changed += "updates/stable.json"
}
# dev.json — самая новая версия из обоих каналов
$devPath = Join-Path $PSScriptRoot "updates/dev.json"
$devVersion = $null
if (Test-Path $devPath) { try { $devVersion = (Get-Content $devPath -Raw -Encoding UTF8 | ConvertFrom-Json).version } catch { } }
if (-not $devVersion -or (Compare-SemVer $Version $devVersion) -gt 0) {
    [IO.File]::WriteAllText($devPath, $json, $utf8)
    $changed += "updates/dev.json"
}

& git add -- $changed
& git commit -m "Релиз $tag" -- $changed
& git push
if ($LASTEXITCODE -ne 0) {
    Finish 1 "Релиз $tag выложен, но описание версии не отправлено на GitHub (git push).`nИсправьте причину и выполните: git push`nПока описание не отправлено, программы новую версию не увидят."
}

Finish 0 "Опубликовано: https://github.com/$Repo/releases/tag/$tag`nПрограммы обновятся при следующем запуске. release-notes.md можно очистить для следующей версии."
