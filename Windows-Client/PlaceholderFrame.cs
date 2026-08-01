using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WindowsWebcamReceiver
{
    /// <summary>
    /// Draws the picture shown when no phone is connected.
    ///
    /// A camera device must always hand over frames. If it stops, apps do not
    /// politely wait - they show a frozen or black box, and some drop the camera
    /// from their list entirely. So when there is no phone, we still produce a
    /// picture, one that explains itself rather than looking broken.
    ///
    /// Note on the text reading backwards: video call apps flip your own preview
    /// horizontally so it behaves like a mirror, and that flip applies to the
    /// whole camera feed, text included. Everyone else on the call sees it the
    /// right way round. A camera cannot know whether the app will mirror it, so
    /// this cannot be right in both places at once. Decided 2026-08-01 to leave
    /// the text unmirrored, which is correct for every viewer except yourself.
    /// Do not "fix" it by drawing the text backwards.
    /// </summary>
    public static class PlaceholderFrame
    {
        /// <summary>
        /// Renders the placeholder as raw RGBA, the same format the browser sends.
        /// <paramref name="tick"/> advances the animation so the picture is
        /// visibly alive - a still image looks like a frozen camera.
        /// </summary>
        public static byte[] Render(int width, int height, int tick, string message)
        {
            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(17, 17, 20));

                // Slow breathing glow, so it is obviously live without being busy.
                var pulse = (Math.Sin(tick / 18.0) + 1) / 2;               // 0..1
                var radius = Math.Min(width, height) * 0.30f;
                var centre = new PointF(width / 2f, height / 2f - height * 0.06f);

                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
                    using var glow = new PathGradientBrush(path)
                    {
                        CenterColor = Color.FromArgb((int)(38 + 26 * pulse), 60, 90, 130),
                        SurroundColors = new[] { Color.FromArgb(0, 17, 17, 20) }
                    };
                    g.FillPath(glow, path);
                }

                var titleSize = Math.Max(18f, height * 0.055f);
                var subSize = Math.Max(12f, height * 0.030f);

                using var title = new Font("Segoe UI", titleSize, FontStyle.Regular, GraphicsUnit.Pixel);
                using var sub = new Font("Segoe UI", subSize, FontStyle.Regular, GraphicsUnit.Pixel);
                using var centred = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Trailing dots, so it reads as "working on it" rather than stuck.
                var dots = new string('.', (tick / 12) % 4);
                using var titleBrush = new SolidBrush(Color.FromArgb(228, 232, 240));
                using var subBrush = new SolidBrush(Color.FromArgb(120, 128, 142));

                g.DrawString(message + dots, title, titleBrush,
                    new RectangleF(0, centre.Y - titleSize, width, titleSize * 2), centred);
                g.DrawString("Iris Camera", sub, subBrush,
                    new RectangleF(0, centre.Y + radius * 0.55f, width, subSize * 2), centred);
            }

            return ToRgba(bmp);
        }

        /// <summary>
        /// Converts the bitmap to raw RGBA. GDI+ gives BGRA rows, so the red and
        /// blue bytes are swapped on the way out.
        /// </summary>
        static byte[] ToRgba(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[bmp.Width * bmp.Height * 4];
                unsafe
                {
                    var src = (byte*)data.Scan0;
                    for (var y = 0; y < bmp.Height; y++)
                    {
                        var row = src + y * data.Stride;
                        var dst = y * bmp.Width * 4;
                        for (var x = 0; x < bmp.Width; x++)
                        {
                            bytes[dst + x * 4 + 0] = row[x * 4 + 2]; // R
                            bytes[dst + x * 4 + 1] = row[x * 4 + 1]; // G
                            bytes[dst + x * 4 + 2] = row[x * 4 + 0]; // B
                            bytes[dst + x * 4 + 3] = 255;
                        }
                    }
                }
                return bytes;
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
