@echo off
cd /d "%~dp0"
setlocal

echo 正在退出...
taskkill /f /im InphicBatteryTray9.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul

echo 移除开机自启...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v InphicBatteryTray9 /f >nul 2>&1

set "INSTALLED=%LOCALAPPDATA%\InphicBatteryTray9"
if exist "%INSTALLED%" (
    echo 删除程序目录: %INSTALLED%
    rd /s /q "%INSTALLED%" >nul 2>&1
)

echo.
echo [OK] 已退出并清理开机自启。
echo      如果程序装在 Downloads 或其它自定义位置, 请手动删除那个 InphicBatteryTray9.exe。
echo      任务栏里的图标会在程序退出后自动消失。
echo.
pause
