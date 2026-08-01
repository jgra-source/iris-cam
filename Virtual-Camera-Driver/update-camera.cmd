@echo off
REM ---------------------------------------------------------------------------
REM Replaces the installed driver with a freshly built one. Run as Administrator.
REM
REM The camera service keeps the driver file open while our camera exists, so
REM the file cannot simply be overwritten. This removes the camera first, which
REM lets the service release the file, then swaps it and puts the camera back.
REM Preferred over restarting the Windows camera service, which would cut the
REM picture in anything currently using a camera.
REM ---------------------------------------------------------------------------

set DEST=C:\Program Files\iPhoneWebcam
set LOG=%~dp0update-log.txt

echo === update started %DATE% %TIME% > "%LOG%"

echo Removing the camera so the driver file is released...
"%~dp0register-camera.exe" /uninstall >> "%LOG%" 2>&1

REM Order matters. Restarting the camera service while our camera still exists
REM just makes it load the driver straight back in. Remove the camera first,
REM then restart, so the service comes back with nothing of ours to hold open.
echo Restarting the camera service...
net stop FrameServer >> "%LOG%" 2>&1
ping -n 3 127.0.0.1 >nul

echo Replacing the driver...
copy /Y "%~dp0x64\Release\VirtualCameraMediaSource.dll" "%DEST%\VirtualCameraMediaSource.dll" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo STILL LOCKED - the driver could not be replaced. >> "%LOG%"
  echo STILL LOCKED. Close anything using a camera, or reboot, then retry.
  "%~dp0register-camera.exe" >> "%LOG%" 2>&1
  exit /b 1
)

echo Recreating the camera...
"%~dp0register-camera.exe" >> "%LOG%" 2>&1
if errorlevel 1 ( echo ERROR: could not recreate the camera - see "%LOG%" & exit /b 1 )

echo Done - the updated driver is installed.
