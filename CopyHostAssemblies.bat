@echo off
rem Copies the Warudo assemblies the mod compiles against but must not ship. The mod
rem references them at build time and binds to Warudo's own loaded copies at runtime.
rem Usage: CopyHostAssemblies.bat [WarudoDir]

setlocal
set "WARUDO_DIR=%~1"
if "%WARUDO_DIR%"=="" set "WARUDO_DIR=D:\Program Files (x86)\Steam\steamapps\common\Warudo"
set "MANAGED_DIR=%WARUDO_DIR%\Warudo_Data\Managed"
set "DEST_DIR=%~dp0Assets\HostAssemblies"

if not exist "%MANAGED_DIR%\Klak.Spout.Runtime.dll" (
	echo Klak.Spout.Runtime.dll not found under "%MANAGED_DIR%"
	exit /b 1
)

copy /y "%MANAGED_DIR%\Klak.Spout.Runtime.dll" "%DEST_DIR%\" >nul || exit /b 1
echo Copied Klak.Spout.Runtime.dll from "%MANAGED_DIR%"
