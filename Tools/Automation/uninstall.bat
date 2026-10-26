@echo off
rem Removes the automation scripts from Warudo's Playground, leaving its other scripts alone.
rem Usage: uninstall.bat [WarudoDir]

setlocal
set "WARUDO_DIR=%~1"
if "%WARUDO_DIR%"=="" set "WARUDO_DIR=D:\Program Files (x86)\Steam\steamapps\common\Warudo"
set "TARGET=%WARUDO_DIR%\Warudo_Data\StreamingAssets\Playground\WarudoAutomation"

if exist "%TARGET%" rmdir /s /q "%TARGET%"
echo Removed "%TARGET%"
exit /b 0
