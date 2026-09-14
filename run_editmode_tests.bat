@echo off
setlocal
rem ============================================================
rem  One-click EditMode test run (headless batch mode).
rem  Usage : run_editmode_tests.bat [--nopause]
rem  IMPORTANT: close the Tuanjie/Unity editor for this project
rem  before running - two instances cannot share the Library.
rem  Results: TestResults\EditMode-results.xml  +  editmode-log.txt
rem ============================================================

set "TUANJIE=C:\Program Files\Tuanjie\Hub\Editor\2022.3.62t10\Editor\Tuanjie.exe"
set "PROJ=E:\Unity\My project"
set "OUT=%PROJ%\TestResults"
set "PAUSE=1"
if /i "%~1"=="--nopause" set "PAUSE=0"

if not exist "%TUANJIE%" (
    echo [FAIL] Tuanjie editor not found: %TUANJIE%
    pause
    exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

echo === Running EditMode tests (first run imports the project, may take minutes) ===
"%TUANJIE%" -batchmode -nographics -projectPath "%PROJ%" -runTests -testPlatform EditMode -testResults "%OUT%\EditMode-results.xml" -logFile "%OUT%\editmode-log.txt" -quit
set "RC=%errorlevel%"

echo.
if exist "%OUT%\EditMode-results.xml" (
    powershell -NoProfile -Command "$x=[xml](Get-Content '%OUT%\EditMode-results.xml'); $r=$x.'test-run'; 'Result: total={0} passed={1} failed={2} skipped={3} duration={4}s' -f $r.total,$r.passed,$r.failed,$r.skipped,[math]::Round([double]$r.duration,2)"
) else (
    echo [FAIL] No result file generated, check "%OUT%\editmode-log.txt"
)
if not %RC% equ 0 echo [NOTE] batch exit code %RC% (0 = all passed, 2 = some tests failed)
if "%PAUSE%"=="1" pause
exit /b 0
