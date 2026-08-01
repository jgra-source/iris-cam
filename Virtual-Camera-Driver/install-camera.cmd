@echo off
REM ---------------------------------------------------------------------------
REM Installs the Iris camera device. Run as Administrator.
REM
REM Three steps, each needed for a reason we learned the hard way:
REM
REM   1. Copy the DLL to Program Files. The Windows camera service runs as
REM      LocalService and cannot read files inside a user profile - the camera
REM      would appear to install and then fail to start.
REM   2. Write the COM registration. regsvr32 does not work here: the DLL has no
REM      DllRegisterServer, so registration is a plain registry key.
REM   3. Create the camera device itself, which needs administrator rights.
REM
REM To remove it again: uninstall-camera.cmd
REM ---------------------------------------------------------------------------

set CLSID={AD12AC5D-5241-4FF1-92AC-B45CAF2ABA15}
set DEST=C:\Program Files\Iris
set LOG=%~dp0install-log.txt

echo === install started %DATE% %TIME% > "%LOG%"

if not exist "%~dp0x64\Release\VirtualCameraMediaSource.dll" (
  echo ERROR: driver not built. Build Release^|x64 first. | tee
  echo ERROR: driver not built >> "%LOG%"
  exit /b 1
)

echo Copying driver to "%DEST%"...
if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%~dp0x64\Release\VirtualCameraMediaSource.dll" "%DEST%\VirtualCameraMediaSource.dll" >> "%LOG%" 2>&1
if errorlevel 1 ( echo ERROR: copy failed & exit /b 1 )

echo Registering the component...
reg add "HKLM\Software\Classes\CLSID\%CLSID%\InProcServer32" /ve /t REG_SZ /d "%DEST%\VirtualCameraMediaSource.dll" /f >> "%LOG%" 2>&1
reg add "HKLM\Software\Classes\CLSID\%CLSID%\InProcServer32" /v ThreadingModel /t REG_SZ /d "Both" /f >> "%LOG%" 2>&1
if errorlevel 1 ( echo ERROR: registration failed & exit /b 1 )

echo Creating the camera device...
"%~dp0register-camera.exe" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo ERROR: could not create the camera device - see "%LOG%"
  exit /b 1
)

echo.
echo Done. "Iris Camera" should now appear in the camera list of Teams,
echo Zoom, Meet and Discord. Start the desktop app to feed it video.
echo.
