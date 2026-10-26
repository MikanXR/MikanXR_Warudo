@echo off
rem Installs the automation scripts into Warudo's Playground, which compiles them on the fly and
rem hot-reloads them on every change. A development tool: anything that can reach the port
rem controls Warudo, so do not leave it installed in a Warudo you stream with.
rem Usage: install.bat [WarudoDir]

setlocal
set "WARUDO_DIR=%~1"
if "%WARUDO_DIR%"=="" set "WARUDO_DIR=D:\Program Files (x86)\Steam\steamapps\common\Warudo"
set "PLAYGROUND=%WARUDO_DIR%\Warudo_Data\StreamingAssets\Playground"

if not exist "%PLAYGROUND%" (
	echo No Playground folder at "%PLAYGROUND%"
	exit /b 1
)

robocopy "%~dp0Source" "%PLAYGROUND%\WarudoAutomation" *.cs /mir /njh /njs /ndl /nfl >nul
if errorlevel 8 (
	echo Copy failed
	exit /b 1
)

echo Installed the automation scripts into "%PLAYGROUND%\WarudoAutomation"
exit /b 0
