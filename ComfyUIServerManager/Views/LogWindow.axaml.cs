using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace ComfyUIServerManager.Views;

public partial class LogWindow : Window
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*m", RegexOptions.Compiled);
    private static readonly IBrush DefaultFg = new SolidColorBrush(Color.FromRgb(204, 204, 204));
    private const int MaxLines = 5000; // ring-buffer cap; older lines fall off

    private readonly ObservableCollection<TextBlock> _lines = new();
    private bool _scrollFrozen;

    public LogWindow()
    {
        InitializeComponent();

        var items = this.FindControl<ItemsControl>("logItems")!;
        items.ItemsSource = _lines;

        var scroll = this.FindControl<ScrollViewer>("logScroll")!;
        scroll.ScrollChanged += OnScrollChanged;

        Closing += (_, e) =>
        {
            // Hide-to-tray instead of dispose. The App owns this window and will
            // dispose it on Exit.
            if (e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void SetInitialLogContent(string fullLog)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetInitialLogContent(fullLog));
            return;
        }
        _lines.Clear();
        foreach (var line in fullLog.Split('\n'))
            AppendLineInternal(line.TrimEnd('\r'), scrollIfNotFrozen: false);
        ScrollToEndIfNotFrozen();
    }

    public void AppendLog(string line)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(line));
            return;
        }
        AppendLineInternal(line, scrollIfNotFrozen: true);
    }

    private void AppendLineInternal(string text, bool scrollIfNotFrozen)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace"),
            FontSize = 12.5,
            Foreground = DefaultFg,
            TextWrapping = TextWrapping.NoWrap
        };

        var lastIndex = 0;
        var currentFg = DefaultFg;
        foreach (Match m in AnsiRegex.Matches(text))
        {
            if (m.Index > lastIndex)
                tb.Inlines!.Add(new Run(text.Substring(lastIndex, m.Index - lastIndex)) { Foreground = currentFg });
            currentFg = ColorFromAnsi(m.Value);
            lastIndex = m.Index + m.Length;
        }
        if (lastIndex < text.Length)
            tb.Inlines!.Add(new Run(text.Substring(lastIndex)) { Foreground = currentFg });

        // Empty line: leave inline list empty but render the line so spacing is preserved.
        if (tb.Inlines!.Count == 0) tb.Text = " ";

        _lines.Add(tb);
        while (_lines.Count > MaxLines) _lines.RemoveAt(0);

        if (scrollIfNotFrozen) ScrollToEndIfNotFrozen();
    }

    private void ScrollToEndIfNotFrozen()
    {
        if (_scrollFrozen) return;
        // Defer to next layout tick so the new item is measured first.
        Dispatcher.UIThread.Post(() =>
        {
            var scroll = this.FindControl<ScrollViewer>("logScroll")!;
            scroll.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scroll = (ScrollViewer)sender!;
        // 32px from bottom counts as "at bottom" — accounts for fractional-pixel offsets
        // when new lines are added during a fast burst.
        var atBottom = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 32;
        _scrollFrozen = !atBottom;
        var hint = this.FindControl<TextBlock>("lblFrozenHint")!;
        hint.IsVisible = _scrollFrozen;
    }

    // The WinForms version supported only the basic 16-color SGR codes; mirror that
    // set (with Avalonia colors). Any unrecognised code falls back to default foreground.
    private static IBrush ColorFromAnsi(string ansi) => ansi switch
    {
        "\x1B[31m" => Brushes.DarkRed,
        "\x1B[32m" => Brushes.DarkGreen,
        "\x1B[33m" => Brushes.DarkGoldenrod,
        "\x1B[34m" => Brushes.DarkBlue,
        "\x1B[35m" => Brushes.DarkMagenta,
        "\x1B[36m" => Brushes.DarkCyan,
        "\x1B[91m" => Brushes.Red,
        "\x1B[92m" => Brushes.LimeGreen,
        "\x1B[93m" => Brushes.Yellow,
        "\x1B[94m" => Brushes.DodgerBlue,
        "\x1B[95m" => Brushes.Magenta,
        "\x1B[96m" => Brushes.Cyan,
        "\x1B[37m" => Brushes.Gray,
        "\x1B[90m" => Brushes.DimGray,
        "\x1B[97m" => Brushes.White,
        _ => DefaultFg
    };
}
