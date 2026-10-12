@echo off
setlocal
rem Builds the HeavyPro toolbar package with the MSFS 2024 SDK package tool.
cd /d "%~dp0"

set "TOOL="
for %%S in ("%MSFS2024_SDK%" "%MSFS_SDK%" "C:\MSFS 2024 SDK" "C:\MSFS SDK" "%ProgramFiles%\MSFS 2024 SDK") do (
  if not defined TOOL if exist "%%~S\Tools\bin\fspackagetool.exe" set "TOOL=%%~S\Tools\bin\fspackagetool.exe"
)

if not defined TOOL (
  echo [HeavyPro] Could not find fspackagetool.exe.
  echo MSFS2024_SDK = "%MSFS2024_SDK%"
  echo MSFS_SDK     = "%MSFS_SDK%"
  echo Install the MSFS 2024 SDK ^(Dev Mode - Help - SDK Installer^), or open HeavyProToolbar.xml
  echo in Dev Mode - Tools - Project Editor and press Build All instead.
  goto :fail
)

echo [HeavyPro] Using "%TOOL%"
"%TOOL%" "%~dp0HeavyProToolbar.xml" -nomirroring
if errorlevel 1 (
  echo [HeavyPro] fspackagetool failed with code %errorlevel%. See the messages above.
  echo Tip: close MSFS and run this again, or build from the Dev Mode Project Editor.
  goto :fail
)

if not exist "%~dp0Packages\heavypro-toolbar\layout.json" (
  echo [HeavyPro] The tool finished but Packages\heavypro-toolbar was not created.
  goto :fail
)

echo.
echo [HeavyPro] Done. Copy "%~dp0Packages\heavypro-toolbar" into your MSFS 2024 Community folder.
pause
exit /b 0

:fail
echo.
pause
exit /b 1
