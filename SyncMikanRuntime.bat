@echo off
rem Copies the MikanXR Unity package's runtime into the mod folder, which the mod tools compile
rem from source. The host-optional folders a mod must leave out (Bindings\Native for P/Invoke,
rem Scripts\FileSystem for System.IO) are skipped, and the package's shaders come along so the
rem mod can load them by path. The copies are gitignored: the package is their source.
rem Usage: SyncMikanRuntime.bat [PackageDir]

setlocal
set "PACKAGE_DIR=%~1"
if "%PACKAGE_DIR%"=="" set "PACKAGE_DIR=%MIKAN_UNITY_PACKAGE%"
if "%PACKAGE_DIR%"=="" set "PACKAGE_DIR=%~dp0..\..\git-MikanXR\MikanXR_Unity\Packages\com.mikanxr.unity"
set "MOD_DIR=%~dp0Assets\MikanXR"

if not exist "%PACKAGE_DIR%\Runtime\Scripts\MikanClient.cs" (
	echo No MikanXR Unity package at "%PACKAGE_DIR%"
	exit /b 1
)

robocopy "%PACKAGE_DIR%\Runtime" "%MOD_DIR%\Runtime" *.cs /mir /xd Native FileSystem Resources /njh /njs /ndl /nfl >nul
if errorlevel 8 (
	echo Copying the runtime failed
	exit /b 1
)

robocopy "%PACKAGE_DIR%\Runtime\Resources\MikanXR\Shaders" "%MOD_DIR%\Shaders" *.shader /mir /njh /njs /ndl /nfl >nul
if errorlevel 8 (
	echo Copying the shaders failed
	exit /b 1
)

rem The package commit the copy came from, for telling which runtime a mod build carries
pushd "%PACKAGE_DIR%"
for /f %%c in ('git rev-parse --short HEAD 2^>nul') do set "PACKAGE_COMMIT=%%c"
popd
> "%MOD_DIR%\Runtime\source.txt" echo %PACKAGE_DIR% %PACKAGE_COMMIT%

echo Synced the MikanXR runtime from "%PACKAGE_DIR%" (%PACKAGE_COMMIT%)
exit /b 0
