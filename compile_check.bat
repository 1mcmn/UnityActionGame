@echo off
setlocal
rem ============================================================
rem  Unity C# fast compile check - no need to open the editor.
rem  Usage : compile_check.bat [--nopause]
rem  Note  : MSB3277 "assembly unification" warnings are harmless,
rem          only look at CS#### errors.
rem ============================================================

set "MSB=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
set "PROJ=E:\Unity\My project"
set "PAUSE=1"
if /i "%~1"=="--nopause" set "PAUSE=0"

if not exist "%MSB%" (
    echo [FAIL] MSBuild not found: %MSB%
    exit /b 1
)

echo === [1/2] Building Assembly-CSharp (runtime scripts) ===
"%MSB%" "%PROJ%\Assembly-CSharp.csproj" /t:Build /v:m /nologo
set "RC1=%errorlevel%"

echo.
echo === [2/2] Building Assembly-CSharp-Editor (editor scripts) ===
"%MSB%" "%PROJ%\Assembly-CSharp-Editor.csproj" /t:Build /v:m /nologo
set "RC2=%errorlevel%"

echo.
if not %RC1% equ 0 echo [FAIL] Assembly-CSharp has compile errors
if not %RC2% equ 0 echo [FAIL] Assembly-CSharp-Editor has compile errors
if %RC1% equ 0 if %RC2% equ 0 echo [OK] All assemblies compiled. MSB3277 warnings can be ignored.
if "%PAUSE%"=="1" pause
exit /b 0
