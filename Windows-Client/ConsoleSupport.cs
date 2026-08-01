using System.Runtime.InteropServices;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Works out whether this run has anywhere to print to.
    ///
    /// The program is built as a windowed application, so Windows never makes a
    /// console for it. That is deliberate. It belongs in the notification area,
    /// and the previous approach - let Windows create a console, then hide the
    /// window - stopped working on Windows 11. Consoles there are hosted by
    /// Windows Terminal, so the window on screen belongs to Windows Terminal, a
    /// separate program. Asking to hide our own window did nothing, and every
    /// user was left with a terminal sitting on screen that killed the camera if
    /// they closed it.
    ///
    /// Never creating one is the only approach that does not depend on which
    /// console host Windows happens to be using.
    ///
    /// That leaves three cases:
    ///   - Started from a terminal: borrow it, so command output still appears
    ///     where it was typed, and redirection to a file still works.
    ///   - Asked for a console with --console: make one.
    ///   - Double-clicked: there is nowhere to print, so printing goes nowhere
    ///     instead of failing. The address reaches the user through the
    ///     notification-area icon.
    ///
    /// This must run before anything writes to the console, because .NET works
    /// out where output goes the first time it is used and keeps that answer.
    /// </summary>
    static class ConsoleSupport
    {
        // Documented as (DWORD)-1: "attach to the console of the parent".
        const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AllocConsole();

        public static void Prepare(string[] args)
        {
            // Started from a terminal - write there, exactly as a normal command
            // would. Nothing is rebound by hand: the standard handles are already
            // inherited, so .NET picks them up on first use, and a redirect like
            // "> log.txt" keeps working.
            if (AttachConsole(AttachParentProcess)) return;

            if (WantsAWindow(args) && AllocConsole()) return;

            // Double-clicked. Logging is not worth failing over, so send it
            // nowhere rather than at a handle that does not exist.
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        static bool WantsAWindow(string[] args) =>
            args.Contains("--console") || args.Contains("--help") || args.Contains("-h");
    }
}
