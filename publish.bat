@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1

echo Publishing QuadroSense as a standalone Windows x64 executable...
dotnet publish "src\QuadroSense.Desktop\QuadroSense.Desktop.csproj" -p:PublishProfile=SingleExe
if errorlevel 1 goto :failed

echo.
echo Published successfully:
echo "%CD%\dist\QuadroSense.Desktop.exe"
popd
pause
exit /b 0

:failed
echo.
echo Publishing failed. See the errors above.
popd
pause
exit /b 1
