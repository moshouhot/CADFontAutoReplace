@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "USER_DOTNET=%USERPROFILE%\Tools\dotnet"

if exist "%USER_DOTNET%\sdk\10.0.201\" (
    set "DOTNET_ROOT=%USER_DOTNET%"
    set "PATH=%USER_DOTNET%;%PATH%"
)

pushd "%REPO_ROOT%" >nul
where pwsh.exe >nul 2>nul
if "%ERRORLEVEL%"=="0" (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Publish-ReleaseAssets.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$script = '%SCRIPT_DIR%Publish-ReleaseAssets.ps1'; $code = [System.IO.File]::ReadAllText($script, [System.Text.Encoding]::UTF8); $block = [scriptblock]::Create($code); & $block @args" %*
)
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul

echo.
if "%EXIT_CODE%"=="0" (
    echo Publish-ReleaseAssets completed successfully.
) else (
    echo Publish-ReleaseAssets failed with exit code %EXIT_CODE%.
)

pause
exit /b %EXIT_CODE%
