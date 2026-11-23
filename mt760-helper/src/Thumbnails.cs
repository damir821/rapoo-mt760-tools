using System;
using System.Windows.Automation;

namespace MT760Helper
{
    // Asks UI Automation whether a screen point is on a window thumbnail of the Alt+Tab switcher.
    // Windows 11 draws the whole switcher as one full-screen window, so the window under the cursor
    // tells nothing, and Windows does not always expose the thumbnails to UIA either.
    static class Thumbnails
    {
        // true: on a thumbnail, false: empty space, null: unknown. Takes ~70 ms, so call it off the UI thread.
        public static bool? HitTest(IntPtr switcher, int x, int y)
        {
            try
            {
                AutomationElementCollection items = AutomationElement.FromHandle(switcher).FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                if (items.Count == 0) return null;
                foreach (AutomationElement item in items)
                {
                    System.Windows.Rect r = item.Current.BoundingRectangle;
                    if (!r.IsEmpty && r.Contains(x, y)) return true;
                }
                return false;
            }
            catch (Exception)
            {
                // The switcher closed while being read.
                return null;
            }
        }
    }
}
