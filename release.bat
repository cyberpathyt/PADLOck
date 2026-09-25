@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem Публикация релиза на GitHub: сборка установщика + APK FreeKiosk из папки kiosk
rem   release.bat 1.2.0-dev.1  — тестовая версия (Pre-release, получают только включившие «тестовые версии»)
rem   release.bat 1.2.0        — рабочая версия (получают все)
rem Репозиторий должен совпадать с AppInfo.UpdateRepository в программе
set "REPO=cyberpathyt/PADLOck"

set "VERSION=%~1"
if "%VERSION%"=="" set /p "VERSION=Версия (например 1.2.0 или 1.2.0-dev.1): "
if "%VERSION%"=="" goto :fail

where gh >nul 2>nul
if errorlevel 1 (
  echo Нужна утилита GitHub CLI. Установка: winget install --id GitHub.cli
  echo Затем один раз: gh auth login
  goto :fail
)
gh auth status >nul 2>nul
if errorlevel 1 (
  echo Выполните один раз: gh auth login
  goto :fail
)

set APKCOUNT=0
for %%f in (kiosk\*.apk) do set /a APKCOUNT+=1 & set "APK=%%f"
if %APKCOUNT% GTR 1 (
  echo В папке kiosk больше одного APK. Оставьте один — тот, которым прошивать.
  goto :fail
)
if %APKCOUNT%==0 (
  echo В папке kiosk нет APK: релиз выйдет без FreeKiosk, у пользователей останется прежний.
  choice /m "Продолжить"
  if errorlevel 2 goto :fail
)

if not exist release-notes.md (
  echo Опишите изменения в release-notes.md ^(открою Блокнот^), сохраните и закройте его.
  echo - > release-notes.md
  notepad release-notes.md
)

set NOPAUSE=1
call build.bat %VERSION%
if errorlevel 1 goto :fail

set "SETUP=dist\PADLOck-Setup-%VERSION%.exe"
echo.
echo Контрольные суммы...
powershell -NoProfile -Command ^
  "$files = @('%SETUP%') + @(Get-ChildItem kiosk\*.apk | ForEach-Object FullName);" ^
  "$files | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), (Split-Path $_ -Leaf) } | Set-Content -Encoding ascii dist\SHA256SUMS.txt"

set PRE=
echo %VERSION% | findstr /c:"-" >nul && set "PRE=--prerelease"

echo.
echo Публикация v%VERSION% в %REPO% %PRE%
if defined APK (
  gh release create v%VERSION% "%SETUP%" "%APK%" dist\SHA256SUMS.txt --repo %REPO% --title "PADLOck %VERSION%" --notes-file release-notes.md %PRE%
) else (
  gh release create v%VERSION% "%SETUP%" dist\SHA256SUMS.txt --repo %REPO% --title "PADLOck %VERSION%" --notes-file release-notes.md %PRE%
)
if errorlevel 1 goto :fail

echo.
echo Опубликовано. Программы обновятся при следующем запуске.
echo release-notes.md можно очистить для следующей версии.
pause
exit /b 0

:fail
echo.
echo Публикация не выполнена
pause
exit /b 1
