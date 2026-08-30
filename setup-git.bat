@echo off
REM One-time setup: initializes the local git repo and points it at
REM https://github.com/jdickinson9691/Mutants, then makes the first commit.
REM Run this once from inside the Mutants folder (double-click is fine).

where git >nul 2>nul
if errorlevel 1 (
  echo Git was not found on this machine. Install Git for Windows first:
  echo https://git-scm.com/download/win
  pause
  exit /b 1
)

if exist ".git" (
  echo A git repo already exists here - skipping "git init".
) else (
  git init
)

git add -A
git commit -m "Initial project scaffold: research, GDD, tech stack, agent contracts"

git remote get-url origin >nul 2>nul
if errorlevel 1 (
  git remote add origin https://github.com/jdickinson9691/Mutants.git
) else (
  echo Remote "origin" already set - leaving it as-is.
)

echo.
echo Done. Review the commit, then push with:
echo   git push -u origin master
echo (or "main" if that's your repo's default branch name)
pause
