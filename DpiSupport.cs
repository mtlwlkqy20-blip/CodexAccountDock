using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexAccountDock
{
    internal static class DpiSupport
    {
        private const int DefaultDpi = 96;
        private const int BaselineWidth = 573;
        private const int BaselineHeight = 860;
        private const int BaseMargin = 24;
        private const int EAccessDenied = unchecked((int)0x80070005);
        private static readonly IntPtr DpiAwarenessContextSystemAware = new IntPtr(-2);
        private static readonly IntPtr DpiAwarenessContextPerMonitorAware = new IntPtr(-3);
        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);
        private static bool initialized;
        private static string initializationPath = "manifest";

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            try
            {
                if (SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2))
                {
                    initializationPath = "SetProcessDpiAwarenessContext(PerMonitorV2)";
                    return;
                }

                int error = Marshal.GetLastWin32Error();
                if (error == 5)
                {
                    initializationPath = "manifest/already configured";
                    return;
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                int result = SetProcessDpiAwareness(2);
                if (result == 0)
                {
                    initializationPath = "SetProcessDpiAwareness(PerMonitor)";
                    return;
                }
                if (result == EAccessDenied)
                {
                    initializationPath = "manifest/already configured";
                    return;
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                if (SetProcessDPIAware()) initializationPath = "SetProcessDPIAware(System)";
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        internal static int DpiForWindow(IntPtr windowHandle)
        {
            if (windowHandle != IntPtr.Zero)
            {
                try
                {
                    uint dpi = GetDpiForWindow(windowHandle);
                    if (IsReasonableDpi(dpi)) return (int)dpi;
                }
                catch (EntryPointNotFoundException) { }
                catch (DllNotFoundException) { }

                try
                {
                    NativeRect rect;
                    if (GetWindowRect(windowHandle, out rect))
                    {
                        int centerX = rect.Left + ((rect.Right - rect.Left) / 2);
                        int centerY = rect.Top + ((rect.Bottom - rect.Top) / 2);
                        return DpiForPoint(new Point(centerX, centerY));
                    }
                }
                catch (EntryPointNotFoundException) { }
                catch (DllNotFoundException) { }
            }

            return DpiForPoint(Point.Empty);
        }

        internal static int DpiForPoint(Point point)
        {
            try
            {
                NativePoint nativePoint = new NativePoint(point.X, point.Y);
                IntPtr monitor = MonitorFromPoint(nativePoint, 2);
                if (monitor != IntPtr.Zero)
                {
                    uint x;
                    uint y;
                    int result = GetDpiForMonitor(monitor, 0, out x, out y);
                    if (result == 0 && IsReasonableDpi(x)) return (int)x;
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                uint dpi = GetDpiForSystem();
                if (IsReasonableDpi(dpi)) return (int)dpi;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    int dpi = (int)Math.Round(graphics.DpiX);
                    if (dpi >= 48 && dpi <= 768) return dpi;
                }
            }
            catch (Exception) { }

            return DefaultDpi;
        }

        internal static Size FitWindow(Rectangle workArea, int dpi)
        {
            if (dpi < 48 || dpi > 768) dpi = DefaultDpi;
            double scale = dpi / (double)DefaultDpi;
            int desiredWidth = Math.Max(1, (int)Math.Round(BaselineWidth * scale));
            int desiredHeight = Math.Max(1, (int)Math.Round(BaselineHeight * scale));
            int scaledMargin = Math.Max(8, (int)Math.Round(BaseMargin * scale));

            int scaledAvailableWidth = Math.Max(1, workArea.Width - (scaledMargin * 2));
            int scaledAvailableHeight = Math.Max(1, workArea.Height - (scaledMargin * 2));
            if (desiredWidth <= scaledAvailableWidth && desiredHeight <= scaledAvailableHeight)
                return new Size(desiredWidth, desiredHeight);

            // Once the window is work-area constrained, keep a stable physical margin.
            // Scaling the margin further would make the window shrink as DPI rises.
            int availableWidth = Math.Max(1, workArea.Width - (BaseMargin * 2));
            int availableHeight = Math.Max(1, workArea.Height - (BaseMargin * 2));

            double fit = Math.Min(availableWidth / (double)BaselineWidth,
                                  availableHeight / (double)BaselineHeight);
            if (fit <= 0 || Double.IsNaN(fit) || Double.IsInfinity(fit))
                return new Size(1, 1);

            int width = Math.Max(1, (int)Math.Floor(BaselineWidth * fit));
            int height = Math.Max(1, (int)Math.Floor(BaselineHeight * fit));
            return new Size(width, height);
        }

        internal static string AwarenessDiagnostic(out bool enabled)
        {
            enabled = false;
            try
            {
                IntPtr context = GetThreadDpiAwarenessContext();
                if (context == IntPtr.Zero) return "unavailable: zero context";
                if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAwareV2))
                {
                    enabled = true;
                    return "PerMonitorV2";
                }
                if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAware))
                {
                    enabled = true;
                    return "PerMonitor";
                }
                if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextSystemAware))
                {
                    enabled = true;
                    return "SystemAware";
                }
                return "Unaware/unknown context=" + context.ToInt64().ToString();
            }
            catch (EntryPointNotFoundException)
            {
                int dpi = DpiForPoint(Point.Empty);
                enabled = dpi >= DefaultDpi;
                return "legacy API; effective dpi=" + dpi.ToString();
            }
            catch (DllNotFoundException)
            {
                return "user32 unavailable";
            }
        }

        internal static int RunDiagnostic()
        {
            bool awarenessEnabled;
            string awareness = AwarenessDiagnostic(out awarenessEnabled);
            bool ok = awarenessEnabled;
            Console.WriteLine("DPI_ACTUAL awareness=" + awareness + " initialization=" + initializationPath +
                              " effective=" + DpiForPoint(Point.Empty).ToString());

            Rectangle[] areas =
            {
                new Rectangle(0, 0, 1920, 1080),
                new Rectangle(0, 0, 1280, 720),
                new Rectangle(-1920, -180, 1920, 1080)
            };
            string[] areaNames = { "1080p", "720p", "negative-origin" };
            int[] dpis = { 96, 120, 144, 192 };

            for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
            {
                int previousWidth = 0;
                int previousHeight = 0;
                for (int dpiIndex = 0; dpiIndex < dpis.Length; dpiIndex++)
                {
                    int dpi = dpis[dpiIndex];
                    Size size = FitWindow(areas[areaIndex], dpi);
                    int scaledMargin = Math.Max(8, (int)Math.Round(BaseMargin * (dpi / 96.0)));
                    int desiredWidth = (int)Math.Round(BaselineWidth * (dpi / 96.0));
                    int desiredHeight = (int)Math.Round(BaselineHeight * (dpi / 96.0));
                    bool idealFits = desiredWidth <= areas[areaIndex].Width - 2 * scaledMargin &&
                                     desiredHeight <= areas[areaIndex].Height - 2 * scaledMargin;
                    int margin = idealFits ? scaledMargin : BaseMargin;
                    bool bounded = size.Width > 0 && size.Height > 0 &&
                                   size.Width <= Math.Max(1, areas[areaIndex].Width - 2 * margin) &&
                                   size.Height <= Math.Max(1, areas[areaIndex].Height - 2 * margin);
                    double ratio = size.Width / (double)size.Height;
                    bool ratioOk = Math.Abs(ratio - BaselineWidth / (double)BaselineHeight) < 0.003;
                    bool monotonic = size.Width >= previousWidth && size.Height >= previousHeight;
                    ok = ok && bounded && ratioOk && monotonic;
                    Console.WriteLine("DPI_SIMULATED area=" + areaNames[areaIndex] +
                                      " origin=" + areas[areaIndex].X.ToString() + "," + areas[areaIndex].Y.ToString() +
                                      " dpi=" + dpi.ToString() + " size=" + size.Width.ToString() + "x" + size.Height.ToString() +
                                      " bounded=" + bounded.ToString() + " ratio=" + ratioOk.ToString() +
                                      " monotonic=" + monotonic.ToString());
                    previousWidth = size.Width;
                    previousHeight = size.Height;
                }
            }

            Console.WriteLine(ok ? "DPI_TEST_OK actual_awareness_and_simulated_fit" : "DPI_TEST_FAILED see_details");
            return ok ? 0 : 1;
        }

        private static bool IsReasonableDpi(uint dpi)
        {
            return dpi >= 48 && dpi <= 768;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    }
}
