using System;
using System.Windows;
using System.Windows.Automation;

namespace TuneBar
{
    /// <summary>
    /// Works out the empty space on the taskbar between the Widgets (weather) button
    /// and the Start button, using Windows UI Automation (the accessibility API).
    /// Positions are in physical pixels, relative to the taskbar's left edge.
    /// </summary>
    internal static class TaskbarLayout
    {
        public readonly record struct Zone(double Left, double Right)
        {
            public double Width => Right - Left;
        }

        /// <summary>
        /// Returns the free zone, or null if it can't be found
        /// (e.g. icons are left-aligned, or this Windows version doesn't expose the buttons).
        /// Call from a background thread: UI Automation can take a few milliseconds.
        /// </summary>
        public static Zone? FindFreeZone()
        {
            try
            {
                if (!NativeMethods.TryGetTaskbarHandle(out IntPtr tray, out var tb)) return null;
                double tbWidth = tb.Right - tb.Left;
                if (tbWidth <= 0) return null;

                var root = AutomationElement.FromHandle(tray);
                Rect? start = FindRect(root, "StartButton");
                if (start == null) return null;

                double startLeft = start.Value.Left - tb.Left;

                // Start button near the left edge = left-aligned taskbar (or Windows 10).
                // There's no gap before it, so let the user place TuneBar by hand.
                if (startLeft < tbWidth * 0.15) return null;

                double left = 0;
                Rect? widgets = FindRect(root, "WidgetsButton");
                if (widgets != null)
                {
                    double widgetsRight = widgets.Value.Right - tb.Left;
                    if (widgetsRight > 0 && widgetsRight < startLeft) left = widgetsRight;
                }

                return new Zone(left, startLeft);
            }
            catch
            {
                return null;
            }
        }

        private static Rect? FindRect(AutomationElement root, string automationId)
        {
            var element = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (element == null) return null;

            Rect r = element.Current.BoundingRectangle;
            if (r.IsEmpty || double.IsInfinity(r.Width) || r.Width <= 0) return null;
            return r;
        }
    }
}
