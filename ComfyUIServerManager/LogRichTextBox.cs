using System.Runtime.InteropServices;

internal class LogRichTextBox : RichTextBox
{
    private const int WM_USER = 0x400;
    private const int WM_SETREDRAW = 0x000B;

    private const int WM_VSCROLL = 0x115;
    private const int WM_MOUSEWHEEL = 0x20A;
    private const int SB_VERT = 1;

    private const uint SIF_RANGE = 0x1;
    private const uint SIF_PAGE = 0x2;
    private const uint SIF_POS = 0x4;
    private const uint SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS;

    private const int EM_GETEVENTMASK = WM_USER + 59;
    private const int EM_SETEVENTMASK = WM_USER + 69;
    private const int EM_GETSCROLLPOS = WM_USER + 221;
    private const int EM_SETSCROLLPOS = WM_USER + 222;
    private IntPtr _EventMask;
    private bool _Painting = true;

    private Point _ScrollPoint;
    private int _SuspendIndex;
    private int _SuspendLength;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, ref Point lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);

    /// <summary>
    ///     Custom event that fires when a vertical scroll occurs.
    /// </summary>
    public new event EventHandler? VScroll;

    /// <summary>
    ///     Overrides the default window procedure to capture scroll messages.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        // Intercept vertical scroll and mouse wheel messages
        if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
            // Raise the custom VScroll event to notify the parent form
            VScroll?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void SuspendPainting()
    {
        if (_Painting)
        {
            _SuspendIndex = SelectionStart;
            _SuspendLength = SelectionLength;
            SendMessage(Handle, EM_GETSCROLLPOS, 0, ref _ScrollPoint);
            SendMessage(Handle, WM_SETREDRAW, 0, IntPtr.Zero);
            _EventMask = SendMessage(Handle, EM_GETEVENTMASK, 0, IntPtr.Zero);
            _Painting = false;
        }
    }

    public void ResumePainting()
    {
        if (!_Painting)
        {
            Select(_SuspendIndex, _SuspendLength);
            SendMessage(Handle, EM_SETSCROLLPOS, 0, ref _ScrollPoint);
            SendMessage(Handle, EM_SETEVENTMASK, 0, _EventMask);
            SendMessage(Handle, WM_SETREDRAW, 1, IntPtr.Zero);
            _Painting = true;
            Invalidate();
        }
    }

    /// <summary>
    ///     Determines if the vertical scrollbar is near the bottom.
    /// </summary>
    /// <param name="thresholdPercent">The percentage from the bottom to consider "near". Defaults to 0.95 (95%).</param>
    /// <returns>True if the scroll position is at or past the threshold.</returns>
    public bool IsNearBottom(double thresholdPercent = 0.95)
    {
        var si = new SCROLLINFO();
        si.cbSize = (uint)Marshal.SizeOf(si);
        si.fMask = SIF_ALL;
        GetScrollInfo(Handle, SB_VERT, ref si);

        // Calculate the total scrollable range (where the top of the thumb can go)
        double scrollableRange = si.nMax - si.nPage;

        // If range is zero or less, there's nothing to scroll, so we're effectively at the bottom.
        if (scrollableRange <= 0) return true;

        // Check if the current position is within the specified percentage of the bottom.
        return si.nPos >= scrollableRange * thresholdPercent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }
}