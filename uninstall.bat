@echo off
title TuneBar uninstaller
taskkill /im TuneBar.exe /f >nul 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v TuneBar /f >nul 2>nul
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\TuneBar.lnk" >nul 2>nul
rmdir /s /q "%LOCALAPPDATA%\Programs\TuneBar" >nul 2>nul
rmdir /s /q "%APPDATA%\TuneBar" >nul 2>nul
echo TuneBar has been removed.
timeout /t 5 >nul
