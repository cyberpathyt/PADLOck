@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem Версия: build.bat 1.2.0 (stable) или build.bat 1.2.0-dev.1 (тестовая)
set "VERSION=%~1"
if "%VERSION%"=="" set /p "VERSION=Версия (например 1.2.0 или 1.2.0-dev.1): "
if "%VERSION%"=="" goto :fail
for /f "tokens=1 delims=-" %%a in ("%VERSION%") do set "NUMVERSION=%%a"

echo === PADLOck %VERSION% · vvedyaev ===

dotnet --list-sdks 2>nul | findstr /b "10." >nul
if errorlevel 1 (
  echo .NET 10 SDK не найден, устанавливаю через winget...
  winget install -e --id Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
  set "PATH=%PATH%;%ProgramFiles%\dotnet"
  dotnet --list-sdks 2>nul | findstr /b "10." >nul
  if errorlevel 1 (
    echo Не удалось установить .NET SDK. Закройте окно, откройте build.bat заново.
    goto :fail
  )
)

if not exist "tools\adb.exe" (
  echo Скачиваю Android platform-tools...
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$z=Join-Path $env:TEMP 'platform-tools.zip'; $d=Join-Path $env:TEMP 'kp-pt'; Invoke-WebRequest 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip' -OutFile $z; Expand-Archive $z $d -Force; New-Item -ItemType Directory -Force tools | Out-Null; Copy-Item (Join-Path $d 'platform-tools\adb.exe'),(Join-Path $d 'platform-tools\AdbWinApi.dll'),(Join-Path $d 'platform-tools\AdbWinUsbApi.dll') tools -Force"
  if not exist "tools\adb.exe" (
    echo Не удалось скачать adb. Положите adb.exe, AdbWinApi.dll и AdbWinUsbApi.dll в папку tools вручную.
    goto :fail
  )
)

echo.
echo [1/3] Сборка программы...
if exist publish rmdir /s /q publish
dotnet publish PADLOck.csproj -c Release -r win-x64 --self-contained true -p:Version=%VERSION% -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
if errorlevel 1 goto :fail

echo.
echo [2/3] Подготовка файлов...
if exist "kiosk-profile.default.json" copy /y "kiosk-profile.default.json" publish\ >nul
copy /y "Инструкция.txt" publish\ >nul

echo.
echo [3/3] Сборка установщика...
set ISCC=
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo Inno Setup не найден, устанавливаю через winget...
  winget install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
  if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
  if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
)
if not defined ISCC (
  echo Не удалось найти Inno Setup. Установите его: https://jrsoftware.org/isdl.php
  goto :fail
)

"%ISCC%" /Q /DAppVersion=%VERSION% /DNumVersion=%NUMVERSION% installer\PADLOck.iss
if errorlevel 1 goto :fail

echo.
echo Готово: %~dp0dist\PADLOck-Setup-%VERSION%.exe
if not defined NOPAUSE pause
exit /b 0

:fail
echo.
echo Сборка завершилась с ошибкой
if not defined NOPAUSE pause
exit /b 1
