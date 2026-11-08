using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lumina;

/// <summary>
/// Mouse-wheel scrolling that eases to its target instead of jumping a notch at a time, installed once for every ScrollViewer.
/// Sideways rows scroll with a touchpad's horizontal swipe (WM_MOUSEHWHEEL, which WPF ignores) or Shift+wheel.
/// </summary>
public static class SmoothScroll
{
    sealed class State { public double Target; public bool Running; public TimeSpan Last; }
    static readonly ConditionalWeakTable<ScrollViewer, State> Vertical = new(), Horizontal = new();
    const int WM_MOUSEHWHEEL = 0x020E;

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        // Touch: a scroll viewer only follows a finger when its panning mode says so, and the default is "none". Each one pans in the directions it can
        // really scroll right now: a viewer that cannot scroll must not take the gesture (a list inside a page), or the page never gets it.
        EventManager.RegisterClassHandler(typeof(ScrollViewer), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => UpdatePanning((ScrollViewer)s)));
        EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, _) => UpdatePanning((ScrollViewer)s)));
    }

    static void UpdatePanning(ScrollViewer sv)
    {
        bool v = sv.ScrollableHeight > 0 && sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
        bool h = sv.ScrollableWidth > 0 && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
        var mode = v && h ? PanningMode.Both : v ? PanningMode.VerticalOnly : h ? PanningMode.HorizontalOnly : PanningMode.None;
        if (ScrollViewer.GetPanningMode(sv) != mode) ScrollViewer.SetPanningMode(sv, mode);
    }

    /// <summary>Let a window receive horizontal wheel and touchpad swipes.</summary>
    public static void Attach(Window w)
    {
        void Hook() => HwndSource.FromHwnd(new WindowInteropHelper(w).Handle)?.AddHook(WndProc);
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Hook(); else w.SourceInitialized += (_, _) => Hook();
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL) return IntPtr.Zero;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.RootVisual is not UIElement root) return IntPtr.Zero;
        int delta = (short)((long)wParam >> 16);
        var screen = new Point((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
        var hit = VisualTreeHelper.HitTest(root, root.PointFromScreen(screen))?.VisualHit;
        if (FindSideways(hit) is { } sv) { By(sv, delta, true); handled = true; }
        return IntPtr.Zero;
    }

    /// <summary>The nearest ancestor that has more content to the side.</summary>
    static ScrollViewer? FindSideways(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ScrollViewer sv && sv.ScrollableWidth > 0 && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled) return sv;
        return null;
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv) return;
        if (Keyboard.Modifiers == ModifierKeys.Shift && sv.ScrollableWidth > 0 && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
        {
            By(sv, -e.Delta, true); e.Handled = true; return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (sv.CanContentScroll || sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled || sv.ScrollableHeight <= 0) return;
        By(sv, -e.Delta);
        e.Handled = true;
    }

    /// <summary>Scroll the viewer by this many pixels, eased. Positive goes down, or right when horizontal.</summary>
    public static void By(ScrollViewer sv, double pixels, bool horizontal = false)
    {
        // a list scrolling by item counts would take pixel offsets as items and fly to the end: scroll it by pixels
        if (horizontal && sv.CanContentScroll) sv.CanContentScroll = false;
        var st = (horizontal ? Horizontal : Vertical).GetOrCreateValue(sv);
        double Offset() => horizontal ? sv.HorizontalOffset : sv.VerticalOffset;
        void Set(double v) { if (horizontal) sv.ScrollToHorizontalOffset(v); else sv.ScrollToVerticalOffset(v); }
        double max = horizontal ? sv.ScrollableWidth : sv.ScrollableHeight;
        if (!st.Running) st.Target = Offset();
        st.Target = Math.Clamp(st.Target + pixels, 0, max);
        if (st.Running) return;
        st.Running = true; st.Last = TimeSpan.MinValue;
        void Tick(object? _, EventArgs a)
        {
            var now = ((RenderingEventArgs)a).RenderingTime;
            double dt = st.Last == TimeSpan.MinValue ? 1.0 / 60 : Math.Min(0.05, (now - st.Last).TotalSeconds);
            st.Last = now;
            double diff = st.Target - Offset();
            if (Math.Abs(diff) < 0.5 || !sv.IsVisible)
            {
                if (sv.IsVisible) Set(st.Target);
                st.Running = false; CompositionTarget.Rendering -= Tick; return;
            }
            Set(Offset() + diff * (1 - Math.Exp(-dt * 16)));
        }
        CompositionTarget.Rendering += Tick;
    }
}
