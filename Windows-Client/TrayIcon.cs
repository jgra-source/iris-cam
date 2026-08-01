using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Puts the program in the notification area instead of leaving a console
    /// window open.
    ///
    /// This is not decoration. The program has to keep running for the camera to
    /// work, including during a call, but a stray black console window looks
    /// like something left behind by mistake - so the natural thing to do is
    /// close it, which kills the camera. A tray icon says "this is meant to be
    /// running", shows whether the phone is connected, and gives an obvious way
    /// to stop it on purpose.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly FrameStore store;
        readonly string viewerUrl;
        readonly string[] phoneUrls;
        readonly Action onQuit;

        NotifyIcon? icon;
        System.Windows.Forms.Timer? refresh;
        Thread? uiThread;
        bool lastLive;

        public TrayIcon(FrameStore store, string viewerUrl, string[] phoneUrls, Action onQuit)
        {
            this.store = store;
            this.viewerUrl = viewerUrl;
            this.phoneUrls = phoneUrls;
            this.onQuit = onQuit;
        }

        public void Start()
        {
            uiThread = new Thread(RunUiLoop) { IsBackground = true, Name = "TrayIcon" };
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
        }

        void RunUiLoop()
        {
            try
            {
                var menu = new ContextMenuStrip();

                menu.Items.Add(new ToolStripLabel("iPhone Webcam") { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) });
                menu.Items.Add(new ToolStripSeparator());

                foreach (var url in phoneUrls)
                {
                    var item = new ToolStripMenuItem($"Copy phone address  {url}");
                    var captured = url;
                    item.Click += (_, _) => TrySetClipboard(captured);
                    menu.Items.Add(item);
                }

                var openViewer = new ToolStripMenuItem("Watch on this PC");
                openViewer.Click += (_, _) => OpenInBrowser(viewerUrl);
                menu.Items.Add(openViewer);

                menu.Items.Add(new ToolStripSeparator());

                var autostart = new ToolStripMenuItem("Start when I sign in") { CheckOnClick = true };
                autostart.Checked = Autostart.Status().enabled;
                autostart.Click += (_, _) =>
                {
                    if (autostart.Checked) Autostart.Enable(); else Autostart.Disable();
                };
                menu.Items.Add(autostart);

                var quit = new ToolStripMenuItem("Quit  (this stops the camera)");
                quit.Click += (_, _) => { icon!.Visible = false; onQuit(); Application.ExitThread(); };
                menu.Items.Add(quit);

                icon = new NotifyIcon
                {
                    Icon = MakeIcon(false),
                    Text = "iPhone Webcam - waiting for phone",
                    ContextMenuStrip = menu,
                    Visible = true
                };
                icon.DoubleClick += (_, _) => OpenInBrowser(viewerUrl);

                // Reflect connection state in the icon, so a glance tells you
                // whether the phone is actually feeding it.
                refresh = new System.Windows.Forms.Timer { Interval = 1000 };
                refresh.Tick += (_, _) => UpdateState();
                refresh.Start();

                Application.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[tray] could not start: {ex.Message}");
            }
        }

        void UpdateState()
        {
            if (icon == null) return;
            var live = store.IsLive;
            if (live == lastLive) return;

            lastLive = live;
            var old = icon.Icon;
            icon.Icon = MakeIcon(live);
            icon.Text = live ? "iPhone Webcam - phone connected" : "iPhone Webcam - waiting for phone";
            old?.Dispose();
        }

        /// <summary>
        /// Draws the tray icon rather than shipping an .ico file, so the single
        /// executable stays self-contained. Green when the phone is sending.
        /// </summary>
        static Icon MakeIcon(bool live)
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                var body = new Rectangle(3, 9, 20, 15);
                using var bodyBrush = new SolidBrush(live ? Color.FromArgb(46, 204, 113) : Color.FromArgb(130, 136, 148));
                g.FillRectangle(bodyBrush, body);

                // little lens barrel, so it reads as a camera at 16px
                var lens = new[] { new Point(24, 14), new Point(30, 10), new Point(30, 23), new Point(24, 19) };
                g.FillPolygon(bodyBrush, lens);

                using var dot = new SolidBrush(Color.FromArgb(245, 247, 250));
                g.FillEllipse(dot, 9, 13, 8, 8);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        static void TrySetClipboard(string text)
        {
            try { Clipboard.SetText(text); } catch { /* clipboard can be busy */ }
        }

        static void OpenInBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { Console.WriteLine($"[tray] could not open browser: {ex.Message}"); }
        }

        public void Dispose()
        {
            refresh?.Stop();
            if (icon != null) { icon.Visible = false; icon.Dispose(); }
        }

        // ---- hiding the console window -------------------------------------

        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] processList, uint count);

        /// <summary>
        /// Hides the console window when the program was double-clicked, and
        /// leaves it alone when it was started from a terminal - where hiding it
        /// would take the user's own window with it, and where they are probably
        /// watching the output on purpose.
        /// </summary>
        public static void HideConsoleIfLaunchedByDoubleClick()
        {
            try
            {
                var handle = GetConsoleWindow();
                if (handle == IntPtr.Zero) return;

                // A console created just for us has exactly one process attached:
                // this one. Started from an existing terminal, there are more.
                var buffer = new uint[4];
                if (GetConsoleProcessList(buffer, (uint)buffer.Length) > 1) return;

                ShowWindow(handle, 0);   // SW_HIDE
            }
            catch { /* leave the window alone if anything is unexpected */ }
        }
    }
}
