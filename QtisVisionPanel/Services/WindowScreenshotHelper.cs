using Cognex.VisionPro;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace QtisVisionPanel.Services
{
    public static class WindowScreenshotHelper
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        public static Bitmap CaptureCogDisplay(CogRecordDisplay display)
        {
            if (display == null || !display.IsHandleCreated || display.Width <= 0 || display.Height <= 0)
            {
                return null;
            }

            Bitmap bmp = new Bitmap(display.Width, display.Height);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        // PW_CLIENTONLY: cattura solo l'area client del controllo Cognex.
                        if (!PrintWindow(display.Handle, hdc, 0x2))
                        {
                            bmp.Dispose();
                            return null;
                        }
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }
    }
}
