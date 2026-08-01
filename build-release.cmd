@echo off
setlocal enabledelayedexpansion
REM ===========================================================================
REM Builds everything into one file: dist\iPhoneWebcam.exe
REM
REM Order matters. The camera driver and its registration tool are compiled
REM first, because the app embeds them inside itself and writes them out when
REM someone runs --install. Build the app first and you get a working app that
REM cannot install a camera.
REM
REM Needs:
REM   - .NET 8 SDK
REM   - Build Tools for Visual Studio, C++ workload + Windows 11 SDK 10.0.26100
REM
REM Usage:  build-release.cmd
REM ===========================================================================

set ROOT=%~dp0
set DRIVER=%ROOT%Virtual-Camera-Driver
set APP=%ROOT%Windows-Client
set DIST=%ROOT%dist

set VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat
set MSBUILD=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe
set NUGET=%ROOT%nuget.exe

echo.
echo === 1/4  camera driver =====================================================
if not exist "%MSBUILD%" (
  echo   MSBuild not found. Install Build Tools for Visual Studio with the C++
  echo   workload and Windows 11 SDK 10.0.26100, then run this again.
  exit /b 1
)

if not exist "%DRIVER%\packages\Microsoft.Windows.CppWinRT.3.0.260520.1" (
  echo   Restoring NuGet packages for the driver...
  if not exist "%NUGET%" (
    powershell -NoProfile -Command "Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile '%NUGET%'" || exit /b 1
  )
  "%NUGET%" restore "%DRIVER%\VirtualCameraMediaSource\packages.config" -PackagesDirectory "%DRIVER%\packages" || exit /b 1
)

REM The doubled backslash in SolutionDir is deliberate: a single one would
REM escape the closing quote and MSBuild would swallow the rest of the line.
"%MSBUILD%" "%DRIVER%\VirtualCameraMediaSource\VirtualCameraMediaSource.vcxproj" ^
  -p:Configuration=Release -p:Platform=x64 -p:SolutionDir="%DRIVER%\\" ^
  -v:minimal -nologo || exit /b 1

echo.
echo === 2/4  registration tool =================================================
if not exist "%VCVARS%" ( echo   vcvars64.bat not found. & exit /b 1 )
call "%VCVARS%" >nul
pushd "%DRIVER%"
cl /nologo /std:c++17 /EHsc /O2 /W3 register-camera.cpp /Fe:register-camera.exe /link /SUBSYSTEM:CONSOLE || (popd & exit /b 1)
del /q register-camera.obj 2>nul
popd

echo.
echo === 3/4  single-file app ===================================================
REM Self-contained so it runs on a machine with no .NET installed. That is what
REM makes the file large (~80MB); drop --self-contained for a ~15MB build that
REM requires the .NET 8 runtime.
dotnet publish "%APP%\Windows-Client.csproj" ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -o "%DIST%" --nologo || exit /b 1

echo.
echo === 4/4  tidy up ===========================================================
REM publish drops debug symbols and a loose wwwroot beside the exe; neither is
REM needed, since the pages are inside the executable.
del /q "%DIST%\*.pdb" 2>nul
del /q "%DIST%\*.xml" 2>nul
del /q "%DIST%\web.config" 2>nul
rmdir /s /q "%DIST%\wwwroot" 2>nul

echo.
for %%F in ("%DIST%\iPhoneWebcam.exe") do set SIZE=%%~zF
set /a MB=!SIZE! / 1048576
echo   Built: %DIST%\iPhoneWebcam.exe  (!MB! MB)
echo.
echo   Distribute that one file. Users run:
echo       iPhoneWebcam.exe --install     once, as administrator
echo       iPhoneWebcam.exe               to use it
echo.
