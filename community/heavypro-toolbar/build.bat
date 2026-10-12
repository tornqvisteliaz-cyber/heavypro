@echo off
rem Builds the HeavyPro toolbar package with the MSFS 2024 SDK package tool.
set "SDK=%MSFS2024_SDK%"
if "%SDK%"=="" set "SDK=%MSFS_SDK%"
if "%SDK%"=="" (
  echo The MSFS 2024 SDK is not installed. Install it from the in-sim Dev Mode menu, then run this again.
  exit /b 1
)
"%SDK%\Tools\bin\fspackagetool.exe" "%~dp0HeavyProToolbar.xml" -nomirroring
if errorlevel 1 exit /b 1
echo.
echo Done. Copy "%~dp0Packages\heavypro-toolbar" into your MSFS 2024 Community folder.
