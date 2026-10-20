@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
set "APP=%~dp0"
for %%I in ("%APP%..\..") do set "ROOT=%%~fI"

if not exist "%ROOT%\app\PackFrame" if not exist "%ROOT%\instance" (
    echo Pack root not found: %ROOT%
    pause
    exit /b 1
)

echo.
echo WARNING: This will delete ALL PackFrame data inside:
echo   %ROOT%
echo.
echo This includes instances, saves, mods, configs,
echo SteamCMD and temporary files.
echo Input profile and Output profile will be kept.
echo.
echo The RimWorld game folder will NOT be touched.
echo.
set /p "CONFIRM=Type RESET to continue: "
if /I not "!CONFIRM!"=="RESET" (
    echo Cancelled.
    pause
    exit /b 0
)

taskkill /IM packframe.exe /F >nul 2>&1

set "SAVED_CFG="
set "SAVED_INDEX="
if exist "%APP%bin" (
    for /r "%APP%bin" %%F in (packframe.json) do (
        if exist "%%~fF" (
            set "SAVED_CFG=%%~fF"
            set "CFG=%%~fF"
            for /f "usebackq delims=" %%U in (`powershell -NoProfile -Command "$j=Get-Content -LiteralPath $env:CFG -Raw | ConvertFrom-Json; if ($j.modpackIndexUrl) { $j.modpackIndexUrl }"`) do set "SAVED_INDEX=%%U"
        )
    )
)

call :clearDir "%ROOT%\instance"
call :clearDir "%ROOT%\tools\steamcmd"
call :clearDir "%ROOT%\runtime"
call :clearDir "%ROOT%\mods"
call :clearDir "%ROOT%\run"
call :clearDir "%ROOT%\profile"

if exist "%ROOT%\packframe.json" del /f /q "%ROOT%\packframe.json" >nul 2>&1
if exist "%ROOT%\steam_appid.txt" del /f /q "%ROOT%\steam_appid.txt" >nul 2>&1

if exist "%APP%bin" (
    for /r "%APP%bin" %%F in (packframe.json*) do (
        if exist "%%~fF" del /f /q "%%~fF" >nul 2>&1
    )
    if exist "%APP%steam_appid.txt" del /f /q "%APP%steam_appid.txt" >nul 2>&1
)

if defined SAVED_CFG if defined SAVED_INDEX (
    powershell -NoProfile -Command "$j=@{modpackIndexUrl=$env:SAVED_INDEX} | ConvertTo-Json -Compress; [IO.File]::WriteAllText($env:SAVED_CFG,$j,(New-Object Text.UTF8Encoding($false)))"
)

echo.
echo Factory reset complete.
echo Cleared PackFrame instances, saves, mods, configs, caches and settings.
echo Empty folder structure was kept.
echo Preserved: Input profile, Output profile
echo Preserved: modpack repository index URL
echo The RimWorld game folder was not modified.
echo.
echo Start packframe.exe to choose a language and configure the game path again.
pause
endlocal
exit /b 0

:clearDir
if exist "%~1" (
    echo Clearing: %~1
    rmdir /s /q "%~1"
)
if not exist "%~1" mkdir "%~1"
exit /b 0
