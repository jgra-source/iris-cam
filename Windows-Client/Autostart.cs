using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Starts the program automatically when you sign in.
    ///
    /// Worth having because of how this fails otherwise. The camera itself is
    /// registered permanently, so after a restart it still appears in Teams and
    /// still shows a picture - just the "Waiting for iPhone" one, forever,
    /// because nothing is feeding it. Nothing looks broken, which makes it
    /// unusually confusing to work out.
    ///
    /// Uses the per-user Run key rather than a shortcut: no administrator
    /// rights, and Windows lists it under Startup in Task Manager, so it can be
    /// turned off there like anything else.
    /// </summary>
    public static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Iris";

        public static int Enable()
        {
            try
            {
                var exe = CurrentExecutablePath();
                if (exe == null)
                {
                    Console.WriteLine("Could not work out where this program lives, so it cannot be");
                    Console.WriteLine("set to start automatically.");
                    return 1;
                }

                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                key.SetValue(ValueName, $"\"{exe}\"");

                Console.WriteLine($"Will start automatically when you sign in:\n  {exe}");
                Console.WriteLine();
                Console.WriteLine("Move or rename the file and this stops working - set it again if you do.");
                Console.WriteLine("Turn it off with --autostart-off, or in Task Manager's Startup tab.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not set automatic start: {ex.Message}");
                return 1;
            }
        }

        public static int Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (key.GetValue(ValueName) == null)
                {
                    Console.WriteLine("It was not set to start automatically.");
                    return 0;
                }

                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Console.WriteLine("Will no longer start automatically.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not remove automatic start: {ex.Message}");
                return 1;
            }
        }

        /// <summary>Whether automatic start is on, and the path it points at.</summary>
        public static (bool enabled, string? target) Status()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                var value = key?.GetValue(ValueName) as string;
                return (value != null, value?.Trim('"'));
            }
            catch { return (false, null); }
        }

        /// <summary>
        /// The real path of the running program. Process.MainModule is used
        /// rather than the assembly location, which is empty for a single-file
        /// build because there is no separate assembly file on disk.
        /// </summary>
        static string? CurrentExecutablePath()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }
    }
}
