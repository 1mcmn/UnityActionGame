@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Fast compile check for this checkout; does not start Unity/Tuanjie.
rem Usage: compile_check.bat [--nopause]
rem Optional: COMPILE_CHECK_MSBUILD = full path to MSBuild.exe.
rem Exit codes: 0 = success, 2 = setup/usage error, otherwise MSBuild's code.
rem Unity must have imported packages and generated up-to-date project files.

set "CHECK_ROOT=%~dp0"
set "CHECK_PAUSE=1"
set "CHECK_EXIT=0"
set "CHECK_PUSHD=0"
set "CHECK_MSBUILD="

:parse_args
if "%~1"=="" goto check_projects
if /i "%~1"=="--nopause" (
    set "CHECK_PAUSE=0"
    shift
    goto parse_args
)
if /i "%~1"=="--help" goto show_help
echo [FAIL] Unknown argument: "%~1"
set "CHECK_EXIT=2"
set "CHECK_PAUSE=0"
goto finish

:check_projects
for %%P in (Game Assembly-CSharp Assembly-CSharp-Editor) do (
    if not exist "%CHECK_ROOT%%%P.csproj" (
        echo [FAIL] Missing generated project: "%CHECK_ROOT%%%P.csproj"
        set "CHECK_EXIT=2"
    )
)
if not "%CHECK_EXIT%"=="0" (
    echo Open this checkout in Tuanjie, finish importing, and regenerate C# project files.
    echo Also regenerate after adding or removing scripts; stale projects can omit files.
    goto finish
)
if not exist "%CHECK_ROOT%Library\ScriptAssemblies\UnityEngine.UI.dll" (
    echo [FAIL] Unity package assemblies are missing from this checkout.
    echo Open this checkout in Tuanjie and let package import and compilation finish.
    set "CHECK_EXIT=2"
    goto finish
)

rem An explicit override must be valid; do not silently use another compiler.
if defined COMPILE_CHECK_MSBUILD (
    set "CHECK_MSBUILD=%COMPILE_CHECK_MSBUILD%"
    goto validate_msbuild
)

rem vswhere excludes incomplete installations by default and finds Build Tools too.
set "CHECK_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%CHECK_VSWHERE%" (
    for /f "usebackq delims=" %%M in (`"%CHECK_VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        if not defined CHECK_MSBUILD set "CHECK_MSBUILD=%%M"
    )
)
if defined CHECK_MSBUILD goto validate_msbuild

rem Support an existing Developer Command Prompt without a vswhere installation.
for /f "delims=" %%M in ('where.exe MSBuild.exe 2^>nul') do (
    if not defined CHECK_MSBUILD set "CHECK_MSBUILD=%%M"
)

:validate_msbuild
if not defined CHECK_MSBUILD (
    echo [FAIL] MSBuild.exe was not found. Install the Visual Studio MSBuild component,
    echo or set COMPILE_CHECK_MSBUILD to an existing MSBuild.exe path.
    set "CHECK_EXIT=2"
    goto finish
)
if not exist "%CHECK_MSBUILD%" (
    echo [FAIL] MSBuild does not exist: "%CHECK_MSBUILD%"
    set "CHECK_EXIT=2"
    goto finish
)
rem Calling a batch file here would transfer control out of this script.
for %%M in ("%CHECK_MSBUILD%") do if /i not "%%~xM"==".exe" (
    echo [FAIL] MSBuild must be an .exe file: "%CHECK_MSBUILD%"
    set "CHECK_EXIT=2"
    goto finish
)
if exist "%CHECK_MSBUILD%\" (
    echo [FAIL] MSBuild path is a directory: "%CHECK_MSBUILD%"
    set "CHECK_EXIT=2"
    goto finish
)

pushd "%CHECK_ROOT%"
if errorlevel 1 (
    echo [FAIL] Cannot enter project directory: "%CHECK_ROOT%"
    set "CHECK_EXIT=2"
    goto finish
)
set "CHECK_PUSHD=1"
echo Project: "%CHECK_ROOT%"
echo MSBuild: "%CHECK_MSBUILD%"

rem Game.asmdef owns Assets/Scripts; list it explicitly in the checked targets.
rem Project references are built, not skipped.
echo [1/3] Building Game
"%CHECK_MSBUILD%" "Game.csproj" /t:Build /p:Configuration=Debug /p:BuildProjectReferences=true /v:minimal /nologo /nr:false
set "CHECK_EXIT=%errorlevel%"
if not "%CHECK_EXIT%"=="0" goto build_failed

echo [2/3] Building Assembly-CSharp
"%CHECK_MSBUILD%" "Assembly-CSharp.csproj" /t:Build /p:Configuration=Debug /p:BuildProjectReferences=true /v:minimal /nologo /nr:false
set "CHECK_EXIT=%errorlevel%"
if not "%CHECK_EXIT%"=="0" goto build_failed

echo [3/3] Building Assembly-CSharp-Editor
"%CHECK_MSBUILD%" "Assembly-CSharp-Editor.csproj" /t:Build /p:Configuration=Debug /p:BuildProjectReferences=true /v:minimal /nologo /nr:false
set "CHECK_EXIT=%errorlevel%"
if not "%CHECK_EXIT%"=="0" goto build_failed

echo [OK] Game, Assembly-CSharp and Assembly-CSharp-Editor compiled successfully.
echo Compilation only; this does not run tests or verify gameplay.
goto finish

:build_failed
echo [FAIL] Compilation stopped. MSBuild exit code: %CHECK_EXIT%
goto finish

:show_help
echo Usage: compile_check.bat [--nopause]
echo Set COMPILE_CHECK_MSBUILD to override automatic MSBuild discovery.
echo Run from any directory; the script uses its own checkout.
set "CHECK_PAUSE=0"

:finish
if "%CHECK_PUSHD%"=="1" popd
if "%CHECK_PAUSE%"=="1" pause
exit /b %CHECK_EXIT%
