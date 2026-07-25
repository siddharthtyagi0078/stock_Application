@echo off
REM Runs the NIFTY EMA fetch script and appends output to a daily log.
REM Point Task Scheduler at this file (Program/script: full path to this .bat).

setlocal
cd /d "%~dp0"

if not exist "logs" mkdir "logs"
for /f "usebackq" %%d in (`powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"`) do set "TODAY=%%d"
set "LOGFILE=logs\fetch_%TODAY%.log"

echo. >> "%LOGFILE%"
echo ===== %DATE% %TIME% ===== >> "%LOGFILE%"
python fetch_nifty_ema.py >> "%LOGFILE%" 2>&1
set "RC=%ERRORLEVEL%"
echo Exit code: %RC% >> "%LOGFILE%"

exit /b %RC%
