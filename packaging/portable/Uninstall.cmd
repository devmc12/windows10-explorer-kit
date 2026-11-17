@echo off
setlocal
"%~dp0Win10ExplorerKit.exe" --uninstall
if errorlevel 1 (
    echo Cleanup failed. Keep this folder and try again after closing the app.
    pause
    exit /b 1
)
echo Win10 Explorer Kit registration and personal data have been removed.
echo You can now delete this portable folder. Running the EXE again will register it again.
pause
