// (In the Views folder)

using System.Threading.Tasks;
using ComfyUIServerManagerModern.Helpers;
using ComfyUIServerManagerModern.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace ComfyUIServerManagerModern.Views;

public sealed partial class LogWindow : Window
{
    public LogViewModel ViewModel { get; }

    public LogWindow(LogViewModel viewModel)
    {
        this.InitializeComponent();
        ViewModel = viewModel;

        // When the ViewModel receives a new log entry, call our local method to update the UI
        ViewModel.LogEntries.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (Models.LogEntry newItem in e.NewItems)
                {
                    AppendLogEntry(newItem);
                }
            }
        };

        // Populate with any existing logs that might have been received before the window was opened
        foreach (var entry in ViewModel.LogEntries)
        {
            AppendLogEntry(entry);
        }
    }

    private void AppendLogEntry(Models.LogEntry logEntry)
    {
        // All UI updates must happen on the UI thread.
        DispatcherQueue.TryEnqueue(() =>
        {
            // Create a new Paragraph for each line of output.
            var paragraph = new Paragraph();

            // Use our parser to generate the colored runs from the raw text.
            var runs = AnsiColorParser.Parse(logEntry.RawText);
            foreach (var run in runs)
            {
                paragraph.Inlines.Add(run);
            }

            // Add the newly created paragraph to the RichTextBlock.
            LogRichTextBlock.Blocks.Add(paragraph);

            // Auto-scroll to the bottom if the user hasn't scrolled up manually.
            // A small delay allows the UI to render the new content before scrolling.
            Task.Delay(50).ContinueWith(_ => AutoScrollView(), TaskScheduler.FromCurrentSynchronizationContext());
        });
    }

    private void AutoScrollView()
    {
        // Check if the scroll viewer is near the bottom.
        var isAtBottom = LogScrollViewer.VerticalOffset >= LogScrollViewer.ScrollableHeight - 5;
        if (isAtBottom)
        {
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        }
    }
}