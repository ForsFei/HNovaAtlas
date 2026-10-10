@echo off
cd /d "%~dp0"

if not exist "node_modules\next\package.json" (
  echo [HNova Atlas] Missing dependencies. Run npm install first.
  pause
  exit /b 1
)

echo [HNova Atlas] Starting development server at http://localhost:43110/
call npm run dev
if errorlevel 1 pause
