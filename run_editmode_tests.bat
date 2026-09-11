@echo off
REM ============================================================
REM  DAY8 - Run Tuanjie/Unity EditMode tests
REM  Editor: Tuanjie 2022.3.62t8
REM
REM  HOW TO USE
REM  ----------
REM  Run this from a NORMAL Windows Command Prompt (Win+R -> cmd),
REM  or just double-click it. Do NOT run it from Git Bash / a
REM  sandboxed shell: those environments cannot launch the editor's
REM  ILPP post-processing host (the run then stalls forever at
REM  "Application.AssetDatabase Initial Refresh Start").
REM
REM  Paths below use %%~dp0 (this script's own folder) so the
REM  Chinese characters in the project path never need to be typed.
REM ============================================================

setlocal

set "PROJ=%~dp0Day8Unity"
set "EDITOR=D:\unity\tuanjie\2022.3.62t8\Editor\Tuanjie.exe"
set "LOG=%~dp0Day8Unity\test-results-editmode-manual.log"
set "XML=%~dp0Day8Unity\test-results-editmode-manual.xml"

echo.
echo   Project : %PROJ%
echo   Editor  : %EDITOR%
echo   Log     : %LOG%
echo   Results : %XML%
echo.

if not exist "%EDITOR%" (
    echo [ERROR] Editor not found at %EDITOR%
    echo         Edit the EDITOR variable in this .bat file.
    goto :done
)

REM These two are required: TuanjiePackageManager.exe is a Node app whose
REM config-path lookups have no null check, so it exits immediately without them.
set "ProgramData=C:\ProgramData"
set "ALLUSERSPROFILE=C:\ProgramData"

echo Running EditMode tests... this takes a few minutes. Please wait.
echo.

"%EDITOR%" -batchmode -nographics -projectPath "%PROJ%" -runTests -testPlatform EditMode -testResults "%XML%" -logFile "%LOG%"

echo.
echo Editor exit code: %ERRORLEVEL%
echo.

if exist "%XML%" (
    echo [OK] Results written to:
    echo      %XML%
    echo.
    echo ---- summary (look for passed=/failed= on the test-run line) ----
    findstr /I /C:"test-run" "%XML%"
) else (
    echo [FAIL] No results XML was produced.
    echo        Check the log: %LOG%
    echo        If it stops at "Application.AssetDatabase Initial Refresh Start",
    echo        the shell environment blocked the ILPP host - use a real cmd.exe.
)

:done
echo.
pause
