@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Uses this checkout and its ProjectVersion.txt. Never closes an open editor.
rem Usage: run_editmode_tests.bat [--nopause] [--check-only]
rem Optional: EDITMODE_EDITOR = full path to the matching Tuanjie.exe.

set "TEST_PAUSE=1"
set "TEST_CHECK_ONLY="
set "TEST_EXIT=0"

:parse_args
if "%~1"=="" goto run
if /i "%~1"=="--nopause" (
    set "TEST_PAUSE=0"
    shift
    goto parse_args
)
if /i "%~1"=="--check-only" (
    set "TEST_CHECK_ONLY=-CheckOnly"
    shift
    goto parse_args
)
if /i "%~1"=="--help" goto show_help
echo [FAIL] Unknown argument: "%~1"
set "TEST_EXIT=2"
set "TEST_PAUSE=0"
goto finish

:run
if not exist "%~dp0Scripts\run_editmode_tests.ps1" (
    echo [FAIL] Missing helper: "%~dp0Scripts\run_editmode_tests.ps1"
    set "TEST_EXIT=2"
    goto finish
)
rem -File arguments keep paths out of executable PowerShell source code.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\run_editmode_tests.ps1" -ProjectRoot "%~dp0." %TEST_CHECK_ONLY%
set "TEST_EXIT=%errorlevel%"
goto finish

:show_help
echo Usage: run_editmode_tests.bat [--nopause] [--check-only]
echo --check-only checks setup without starting the editor or running tests.
echo Set EDITMODE_EDITOR to the matching Tuanjie.exe if it is installed elsewhere.
echo Results: TestResults\EditMode-timestamp-unique-id\results.xml and editor.log
set "TEST_PAUSE=0"

:finish
if "%TEST_PAUSE%"=="1" pause
exit /b %TEST_EXIT%
