@echo off
setlocal EnableExtensions EnableDelayedExpansion
pushd "%~dp0"
if errorlevel 1 exit /b 1

set "BUILD_CONFIGURATION=Release"
set "RUNTIME=win-x64"
set "OUTPUT_ROOT=%CD%\dist\build-all"
set "APP_OUTPUT=%OUTPUT_ROOT%\QuadroSense"
set "MOD_OUTPUT=%OUTPUT_ROOT%\Mods"

tasklist /FI "IMAGENAME eq QuadroSense.Desktop.exe" 2>NUL | find /I "QuadroSense.Desktop.exe" >NUL
if not errorlevel 1 (
  echo QuadroSense.Desktop.exe is currently running.
  echo Close QuadroSense before publishing so the existing EXE can be replaced.
  goto :failed
)

echo Building QuadroSense Desktop...
dotnet publish "src\QuadroSense.Desktop\QuadroSense.Desktop.csproj" ^
  -c "%BUILD_CONFIGURATION%" -r "%RUNTIME%" --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%APP_OUTPUT%"
if errorlevel 1 goto :failed

set "FOUND_MOD=0"
for /r "Mods" %%F in (*.csproj) do (call :publish_mod "%%F" || goto :failed)
if exist "ModsIntegrations" for /r "ModsIntegrations" %%F in (*.csproj) do (call :publish_mod "%%F" || goto :failed)

if "%FOUND_MOD%"=="0" echo No mod projects found under Mods or ModsIntegrations.

echo.
echo Build completed successfully.
echo QuadroSense: "%APP_OUTPUT%\QuadroSense.Desktop.exe"
if not "%FOUND_MOD%"=="0" echo Mods: "%MOD_OUTPUT%"
popd
exit /b 0

:publish_mod
set "FOUND_MOD=1"
set "MOD_NAME=%~n1"
set "MOD_DIR=%MOD_OUTPUT%\%MOD_NAME%"
echo.
echo Building mod %MOD_NAME%...
dotnet publish "%~1" ^
  -c "%BUILD_CONFIGURATION%" -r "%RUNTIME%" --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%MOD_DIR%"
if errorlevel 1 exit /b 1
exit /b 0

:failed
echo.
echo Build failed. See the errors above.
popd
exit /b 1
