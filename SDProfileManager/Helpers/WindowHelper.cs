using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace SDProfileManager.Helpers;

public static class WindowHelper
{
    private static nint _hwnd;

    public static void SetWindow(Window window)
    {
        _hwnd = WindowNative.GetWindowHandle(window);
    }

    public static nint GetHwnd() => _hwnd;

    public static void InitializePicker(object picker)
    {
        InitializeWithWindow.Initialize(picker, _hwnd);
    }
}
