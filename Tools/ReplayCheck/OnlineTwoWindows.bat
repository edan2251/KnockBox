@echo off
rem Knockbox online step 2: two game windows on this PC connected by local UDP (lockstep, no packages, no internet).
rem Window 1 = host (P1), window 2 = guest (P2). Mouse aim + keys as usual in each window.
rem Rematch after a match: both players hold ATTACK at the same time.
rem Records are saved to the Replays folder when a window closes.
setlocal
set "EXE=%~dp0Knockbox.exe"
set "DELAY=%1"
if "%DELAY%"=="" set "DELAY=3"
start "Knockbox host" "%EXE%" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "%~dp0online_host.log" -host 7777 -delay %DELAY%
timeout /t 2 /nobreak >nul
start "Knockbox guest" "%EXE%" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "%~dp0online_guest.log" -join 127.0.0.1:7777
