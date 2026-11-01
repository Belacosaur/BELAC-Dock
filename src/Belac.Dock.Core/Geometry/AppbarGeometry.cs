using Belac.Dock.Core.Models;

namespace Belac.Dock.Core.Geometry;

public static class AppbarGeometry
{
    public static int DipToPx(double dip, int dpi)
    {
        if (dpi <= 0)
            dpi = 96;

        return Math.Max(1, (int)Math.Round(dip * dpi / 96.0, MidpointRounding.AwayFromZero));
    }

    public static PixelRect Place(PixelRect workArea, int widthPx, bool rightEdge)
    {
        if (workArea.IsEmpty)
            return workArea;

        var width = Math.Clamp(widthPx, 1, Math.Max(1, workArea.Width - 1));
        return rightEdge
            ? new PixelRect(workArea.Right - width, workArea.Top, workArea.Right, workArea.Bottom)
            : new PixelRect(workArea.Left, workArea.Top, workArea.Left + width, workArea.Bottom);
    }
}
