@echo off
title TuneBar installer
setlocal EnableExtensions

rem Builds TuneBar from this folder and installs it for the current user.
rem After this you never need a command window again: TuneBar starts with Windows,
rem and you can find "TuneBar" in the Start menu (opening it again shows Settings).

rem ---- Find dotnet.exe (look in the usual folders, not just PATH)
set "DOTNET="
for %%D in ("%ProgramW6432%\dotnet\dotnet.exe" "%ProgramFiles%\dotnet\dotnet.exe" "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" "%USERPROFILE%\.dotnet\dotnet.exe" "%ProgramFiles(x86)%\dotnet\dotnet.exe") do (
  if not defined DOTNET if exist "%%~D" set "DOTNET=%%~D"
)
if not defined DOTNET (
  for /f "delims=" %%P in ('where dotnet 2^>nul') do if not defined DOTNET set "DOTNET=%%P"
)

if not defined DOTNET (
  echo.
  echo Couldn't find .NET on this PC at all.
  goto :need_sdk
)

echo Found .NET at: %DOTNET%

rem ---- Make sure an SDK (not just the runtime) is installed. SDK 8, 9 or 10 all work.
set "SDKLIST=%TEMP%\tunebar_sdks.txt"
"%DOTNET%" --list-sdks > "%SDKLIST%" 2>nul
findstr /r /b /c:"8\." /c:"9\." /c:"10\." "%SDKLIST%" >nul
if errorlevel 1 (
  echo.
  echo You have the .NET *Runtime*, which runs apps, but building TuneBar needs the .NET *SDK*.
  echo.
  echo Runtimes found on this PC:
  "%DOTNET%" --list-runtimes
  goto :need_sdk
)

echo SDKs found:
type "%SDKLIST%"
echo.

set "DEST=%LOCALAPPDATA%\Programs\TuneBar"
set "ARCH=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "ARCH=win-arm64"

echo Closing TuneBar if it's running...
taskkill /im TuneBar.exe /f >nul 2>nul

echo Building TuneBar (this takes a minute the first time)...
"%DOTNET%" publish "%~dp0src\TuneBar\TuneBar.csproj" -c Release -r %ARCH% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%DEST%"
if errorlevel 1 (
  echo.
  echo Build failed. Copy the red error text above and send it over so it can be fixed.
  pause
  exit /b 1
)

echo Adding TuneBar to the Start menu...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs')+'\TuneBar.lnk'); $s.TargetPath=$env:DEST+'\TuneBar.exe'; $s.Description='TuneBar settings'; $s.Save()"

echo Starting TuneBar...
start "" "%DEST%\TuneBar.exe"

echo.
echo Done! TuneBar is running and will start with Windows.
echo You can close this window.
timeout /t 8 >nul
exit /b 0

:need_sdk
echo.
echo Opening the download page. Pick ".NET 8.0" then, under "SDK", the Windows x64 Installer.
echo (Not "Runtime" or "Desktop Runtime".) Install it, then double-click install.bat again.
start "" "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
echo.
pause
exit /b 1