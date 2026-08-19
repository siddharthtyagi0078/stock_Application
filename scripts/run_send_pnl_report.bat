@echo off
REM Builds the monthly P/L report, renders the date-wise PNG and mails it.
REM Task Scheduler points here (Program/script: full path to this .bat) at 06:00 daily.
REM Requires env var GMAIL_APP_PASSWORD (Gmail App Password for siddhu0siddhu@gmail.com).

setlocal
cd /d "%~dp0"

if not exist "logs" mkdir "logs"
for /f "usebackq" %%d in (`powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"`) do set "TODAY=%%d"
set "LOGFILE=logs\pnl_report_%TODAY%.log"

echo. >> "%LOGFILE%"
echo ===== %DATE% %TIME% ===== >> "%LOGFILE%"
python send_pnl_report.py >> "%LOGFILE%" 2>&1
set "RC=%ERRORLEVEL%"
echo Exit code: %RC% >> "%LOGFILE%"

exit /b %RC%
