using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace TimerManager.App;

internal static class WindowAppearance
{
    public static void Apply(Window window, bool fitDialog = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        if (!fitDialog) return;
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var transform = HwndSource.FromHwnd(handle).CompositionTarget.TransformFromDevice;
        window.MaxHeight = area.Height * transform.M22 - 16;
        window.MaxWidth = area.Width * transform.M11 - 16;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
