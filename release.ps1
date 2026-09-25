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

# Описание версии для программ — одно и то же для GitHub и для сервера
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
$results = @()

# ---------- 1. Сервер PADLOck Hub (внутренняя сеть) ----------
# update-server.txt — адрес сервера, update-ca.crt — его корневой сертификат, release-key.txt — ключ API с ролью admin
# (padlock-hub apikey add releases --role admin). Все три файла в репозиторий не попадают.
$serverLine = if (Test-Path "update-server.txt") {
    Get-Content "update-server.txt" | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | Select-Object -First 1
}
if (-not $serverLine) {
    $results += "Сервер: не настроен (нет update-server.txt) — пропущен"
} elseif (-not (Test-Path "update-ca.crt") -or -not (Test-Path "release-key.txt")) {
    $results += "Сервер: ОШИБКА — рядом с проектом нужны update-ca.crt и release-key.txt"
} else {
    $base = if ($serverLine.Contains('://')) { $serverLine.TrimEnd('/') } else { "https://" + $serverLine.TrimEnd('/') }
    $key = (Get-Content "release-key.txt" -Raw).Trim()
    $ca = (Resolve-Path "update-ca.crt").Path
    $answer = Join-Path ([IO.Path]::GetTempPath()) "padlock-upload-answer.txt"
    # Сертификат сервера выпущен его собственным центром: доверяем ему через --cacert; списка отзыва у него нет
    function Send-ToServer([string[]]$extra, [string]$url) {
        $code = & curl.exe --cacert $ca --ssl-no-revoke -sS -o $answer -w "%{http_code}" -H "Authorization: Bearer $key" @extra $url
        if ($code -ne "200") {
            Write-Host "  ответ сервера ($code): $(if (Test-Path $answer) { Get-Content $answer -Raw -Encoding UTF8 })" -ForegroundColor Red
            return $false
        }
        return $true
    }

    Write-Host ""
    Write-Host "Загрузка на сервер $base ..." -ForegroundColor Cyan
    $ok = $true
    foreach ($f in $files) {
        $name = Split-Path $f -Leaf
        Write-Host "  $name"
        if (-not (Send-ToServer @('-X', 'PUT', '--data-binary', "@$f") "$base/api/updates/files/$([uri]::EscapeDataString($name))")) { $ok = $false; break }
    }
    if ($ok) {
        $serverManifest = Join-Path $PSScriptRoot "dist\server-manifest.json"
        [IO.File]::WriteAllText($serverManifest, $json, $utf8)
        $ok = Send-ToServer @('-X', 'POST', '-H', 'Content-Type: application/json', '--data-binary', "@$serverManifest") "$base/api/updates/publish"
    }
    $results += if ($ok) { "Сервер: опубликовано" } else { "Сервер: ОШИБКА (подробности выше)" }
}

# ---------- 2. GitHub ----------
Write-Host ""
Write-Host "Публикую $tag в $Repo..." -ForegroundColor Cyan
$ghArgs = @('release', 'create', $tag) + $files + @('dist\SHA256SUMS.txt',
    '--repo', $Repo, '--title', "PADLOck $Version", '--notes-file', 'release-notes.md')
if ($prerelease) { $ghArgs += '--prerelease' }
& gh @ghArgs
if ($LASTEXITCODE -ne 0) {
    $results += "GitHub: ОШИБКА — релиз не создан"
} else {
    # updates/stable.json и dev.json в репозитории — их читают программы с источником «GitHub»
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
    if ($changed.Count -gt 0) {
        & git add -- $changed
        & git commit -m "Релиз $tag" -- $changed
        & git push
    }
    $results += if ($LASTEXITCODE -eq 0) { "GitHub: опубликовано — https://github.com/$Repo/releases/tag/$tag" }
                else { "GitHub: релиз создан, но описание версии не отправлено (git push) — выполните git push вручную" }
}

Write-Host ""
$results | ForEach-Object { Write-Host "  $_" }
$anyOk = @($results | Where-Object { $_ -match 'опубликовано' }).Count -gt 0
if ($anyOk) {
    Finish 0 "Готово. Программы обновятся при следующем запуске. release-notes.md можно очистить для следующей версии."
} else {
    Finish 1 "Версия никуда не опубликована"
}
