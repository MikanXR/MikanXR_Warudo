@echo on
call %~dp0SetMikanVars_x64.bat

echo "Clear Previous Plugin State"
del /Q "%~dp0MikanXR\Behaviors\*.*"
del /Q "%~dp0MikanXR\Generated\*.*"
del /Q "%~dp0MikanXR\Serialization\*.*"
del /Q "%~dp0MikanXR\*.*"
del /Q "%~dp0Materials\*.*"
del /Q "%~dp0Shaders\*.*"

echo "Copy Mikan Unity Plugin Files"
copy "%MIKAN_BINDINGS_PATH%\Behaviors\*.cs" "%~dp0MikanXR\Behaviors\*.cs"
IF %ERRORLEVEL% NEQ 0 (
  echo "Error copying Behavior Scripts"
  goto failure
)
copy "%MIKAN_BINDINGS_PATH%\MikanXR\Generated\*.cs" "%~dp0MikanXR\Generated\*.cs"
IF %ERRORLEVEL% NEQ 0 (
  echo "Error copying Generated Scripts"
  goto failure
)
copy "%MIKAN_BINDINGS_PATH%\MikanXR\Serialization\*.cs" "%~dp0MikanXR\Serialization\*.cs"
IF %ERRORLEVEL% NEQ 0 (
  echo "Error copying Serialization Scripts"
  goto failure
)
copy "%MIKAN_BINDINGS_PATH%\MikanXR\*.cs" "%~dp0MikanXR\*.cs"
IF %ERRORLEVEL% NEQ 0 (
  echo "Error copying Mikan Manager Scripts"
  goto failure
)
copy "%MIKAN_BINDINGS_PATH%\Materials\*.mat" "%~dp0Materials\*.mat"
IF %ERRORLEVEL% NEQ 0 (
  echo "Error copying Materials"
  goto failure
)
copy "%MIKAN_BINDINGS_PATH%\Shaders\*.shader" "%~dp0Shaders\*.shader"
IF %ERRORLEVEL% NEQ 0 (
  echo "Error copying Shaders"
  goto failure
)

popd
EXIT /B 0

:failure
pause
EXIT /B 1