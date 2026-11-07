@echo off
rem Knockbox cross-PC determinism check (CR-004 online step 1).
rem Replays the same input recording twice and compares every tick with reference\trace.txt.
rem Runs without a window and finishes in a few seconds. Output goes to the results folder.
setlocal
set "HERE=%~dp0"
set "EXE=%HERE%Knockbox.exe"
set "REC=%HERE%brawl_sample.kbrp"
set "REF=%HERE%reference\trace.txt"
set "OUT=%HERE%results"
if not exist "%EXE%" (
  echo Knockbox.exe not found next to this file: "%EXE%"
  pause
  exit /b 2
)
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%\run1"
mkdir "%OUT%\run2"
echo Replay check 1/2 ...
"%EXE%" -batchmode -nographics -logFile "%OUT%\run1\player.log" -replaycheck "%REC%" -expect "%REF%" -out "%OUT%\run1"
echo exit code %ERRORLEVEL%
echo Replay check 2/2 ...
"%EXE%" -batchmode -nographics -logFile "%OUT%\run2\player.log" -replaycheck "%REC%" -expect "%REF%" -out "%OUT%\run2"
echo exit code %ERRORLEVEL%
chcp 65001 >nul
echo.
echo ===== run 1 =====
type "%OUT%\run1\result.txt"
echo.
echo ===== run 2 =====
type "%OUT%\run2\result.txt"
echo.
echo Zip the whole "results" folder and send it back.
pause
