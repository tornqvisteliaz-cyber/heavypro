@echo off
setlocal
title HeavyFeel SimConnect setup
cd /d "%~dp0"

echo.
echo HeavyFeel - copy MSFS 2024 SimConnect files
echo.

if not exist "lib" mkdir "lib"

set "MANAGED="
set "NATIVE="

if defined MSFS2024_SDK (
  if exist "%MSFS2024_SDK%\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" (
    set "MANAGED=%MSFS2024_SDK%\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
    set "NATIVE=%MSFS2024_SDK%\SimConnect SDK\lib\SimConnect.dll"
  )
)

if not defined MANAGED if defined MSFS_SDK (
  if exist "%MSFS_SDK%\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" (
    set "MANAGED=%MSFS_SDK%\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
    set "NATIVE=%MSFS_SDK%\SimConnect SDK\lib\SimConnect.dll"
  )
)

if not defined MANAGED if exist "C:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" (
  set "MANAGED=C:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
  set "NATIVE=C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
)

if not defined MANAGED if exist "C:\MSFS2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" (
  set "MANAGED=C:\MSFS2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
  set "NATIVE=C:\MSFS2024 SDK\SimConnect SDK\lib\SimConnect.dll"
)

if not defined MANAGED if exist "%LOCALAPPDATA%\Programs\Microsoft Flight Simulator 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" (
  set "MANAGED=%LOCALAPPDATA%\Programs\Microsoft Flight Simulator 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
  set "NATIVE=%LOCALAPPDATA%\Programs\Microsoft Flight Simulator 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
)

if not defined MANAGED if exist "C:\Program Files\Microsoft Flight Simulator 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll" (
  set "MANAGED=C:\Program Files\Microsoft Flight Simulator 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
  set "NATIVE=C:\Program Files\Microsoft Flight Simulator 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
)

if not defined MANAGED (
  echo Could not find the MSFS 2024 SDK automatically.
  echo.
  echo 1. Start MSFS 2024
  echo 2. Options - enable Developer Mode
  echo 3. Install the SDK from the Developer menu
  echo 4. Run this file again
  echo.
  echo Or copy these two files into the lib folder next to this file:
  echo   Microsoft.FlightSimulator.SimConnect.dll
  echo   SimConnect.dll
  echo.
  pause
  exit /b 1
)

copy /Y "%MANAGED%" "lib\Microsoft.FlightSimulator.SimConnect.dll" >nul
copy /Y "%NATIVE%" "lib\SimConnect.dll" >nul

echo Copied:
echo   %MANAGED%
echo   %NATIVE%
echo.
echo Next: open HeavyFeel.sln in Visual Studio and press F5.
echo.
pause
