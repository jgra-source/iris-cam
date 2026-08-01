using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Installs and removes the "Iris Camera" camera device.
    ///
    /// The driver and its registration tool travel inside this executable as
    /// data. They cannot run from in there: Windows loads the driver by path,
    /// into other applications' processes, so it has to be written to disk
    /// first. That is the only reason install exists as a separate step rather
    /// than just happening.
    ///
    /// Program Files is used because the Windows camera service runs under a
    /// restricted account that cannot read a user's own folders. Installing to
    /// a user folder appears to work and then fails when the camera starts.
    /// </summary>
    public static class CameraInstaller
    {
        const string InstallDir = @"C:\Program Files\Iris";
        const string DriverName = "VirtualCameraMediaSource.dll";
        const string ToolName = "register-camera.exe";
        const string Clsid = "{AD12AC5D-5241-4FF1-92AC-B45CAF2ABA15}";

        public static int Install()
        {
            if (!IsElevated())
            {
                Console.WriteLine("Adding a camera to Windows needs administrator rights.");
                Console.WriteLine("Right-click this program and choose 'Run as administrator', then try again.");
                return 1;
            }

            try
            {
                Directory.CreateDirectory(InstallDir);

                var driver = Path.Combine(InstallDir, DriverName);
                var tool = Path.Combine(InstallDir, ToolName);

                // If a camera already exists it holds the driver file open, so
                // remove it before trying to overwrite anything.
                if (File.Exists(tool))
                {
                    Console.WriteLine("Removing the previous camera...");
                    Run(tool, "/uninstall");
                    StopFrameServer();
                }

                Console.WriteLine($"Writing the driver to {InstallDir}...");
                if (!Extract("Payload." + DriverName, driver)) return 1;
                if (!Extract("Payload." + ToolName, tool)) return 1;

                Console.WriteLine("Registering the driver...");
                // regsvr32 does not apply here - the driver has no
                // DllRegisterServer. Registration is this one key.
                RegAdd($@"HKLM\Software\Classes\CLSID\{Clsid}\InProcServer32", "/ve", driver);
                RegAdd($@"HKLM\Software\Classes\CLSID\{Clsid}\InProcServer32", "/v ThreadingModel", "Both");

                Console.WriteLine("Creating the camera...");
                var rc = Run(tool, "");
                if (rc != 0)
                {
                    Console.WriteLine("Could not create the camera.");
                    return rc;
                }

                Console.WriteLine();
                Console.WriteLine("Done. \"Iris Camera\" is now in the camera list of Teams,");
                Console.WriteLine("Zoom, Google Meet and Discord.");
                Console.WriteLine("Run this program normally (no --install) to feed it your phone.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Install failed: {ex.Message}");
                return 1;
            }
        }

        public static int Uninstall()
        {
            if (!IsElevated())
            {
                Console.WriteLine("Removing the camera needs administrator rights.");
                return 1;
            }

            try
            {
                var tool = Path.Combine(InstallDir, ToolName);
                if (File.Exists(tool)) Run(tool, "/uninstall");

                StopFrameServer();

                RunHidden("reg.exe", $@"delete HKLM\Software\Classes\CLSID\{Clsid} /f");

                if (Directory.Exists(InstallDir))
                {
                    try { Directory.Delete(InstallDir, true); }
                    catch (IOException)
                    {
                        Console.WriteLine("The driver file is still in use; it will clear on reboot.");
                    }
                }

                Console.WriteLine("\"Iris Camera\" removed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Uninstall failed: {ex.Message}");
                return 1;
            }
        }

        /// <summary>Writes one of the bundled payloads out to disk.</summary>
        static bool Extract(string resourceName, string destination)
        {
            using var src = typeof(CameraInstaller).Assembly.GetManifestResourceStream(resourceName);
            if (src == null)
            {
                Console.WriteLine($"This build does not contain {resourceName}.");
                Console.WriteLine("It was built without the driver - see build-release.cmd.");
                return false;
            }

            using var dst = File.Create(destination);
            src.CopyTo(dst);
            return true;
        }

        /// <summary>
        /// The camera service keeps the driver open. Stopping it lets the file
        /// be replaced; it starts again by itself when a camera is next used.
        /// </summary>
        static void StopFrameServer()
        {
            RunHidden("net.exe", "stop FrameServer");
            System.Threading.Thread.Sleep(1500);
        }

        static void RegAdd(string key, string valueArg, string data) =>
            RunHidden("reg.exe", $"add \"{key}\" {valueArg} /t REG_SZ /d \"{data}\" /f");

        static int Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false };
            using var p = Process.Start(psi);
            p!.WaitForExit();
            return p.ExitCode;
        }

        static int RunHidden(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p!.WaitForExit();
            return p.ExitCode;
        }

        static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
