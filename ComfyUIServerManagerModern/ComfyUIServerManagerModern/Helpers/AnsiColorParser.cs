// (Create this new file in the Helpers folder)

using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Text.RegularExpressions;
using Windows.UI;

namespace ComfyUIServerManagerModern.Helpers;

public static class AnsiColorParser
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*m", RegexOptions.Compiled);

    public static IEnumerable<Run> Parse(string rawText)
    {
        var runs = new List<Run>();
        var lastIndex = 0;
        var defaultColor = Colors.WhiteSmoke;
        var currentColor = defaultColor;

        foreach (Match match in AnsiRegex.Matches(rawText))
        {
            // Add the text segment before the matched ANSI code
            if (match.Index > lastIndex)
            {
                runs.Add(new Run
                {
                    Text = rawText.Substring(lastIndex, match.Index - lastIndex),
                    Foreground = new SolidColorBrush(currentColor)
                });
            }

            // Update the current color based on the ANSI code
            currentColor = GetColorFromAnsiCode(match.Value, defaultColor);
            lastIndex = match.Index + match.Length;
        }

        // Add any remaining text after the last ANSI code
        if (lastIndex < rawText.Length)
        {
            runs.Add(new Run
            {
                Text = rawText.Substring(lastIndex),
                Foreground = new SolidColorBrush(currentColor)
            });
        }

        return runs;
    }

    private static Color GetColorFromAnsiCode(string ansi, Color defaultColor)
    {
        return ansi switch
        {
            // Using System.Drawing.Color for name constants, then converting to Microsoft.UI.Color
            "\x1B[31m" => ToUiColor(System.Drawing.Color.DarkRed),
            "\x1B[32m" => ToUiColor(System.Drawing.Color.DarkGreen),
            "\x1B[33m" => ToUiColor(System.Drawing.Color.DarkGoldenrod),
            "\x1B[34m" => ToUiColor(System.Drawing.Color.DarkBlue),
            "\x1B[35m" => ToUiColor(System.Drawing.Color.DarkMagenta),
            "\x1B[36m" => ToUiColor(System.Drawing.Color.DarkCyan),
            "\x1B[91m" => ToUiColor(System.Drawing.Color.Red),
            "\x1B[92m" => ToUiColor(System.Drawing.Color.Green),
            "\x1B[93m" => ToUiColor(System.Drawing.Color.Yellow),
            "\x1B[94m" => ToUiColor(System.Drawing.Color.Blue),
            "\x1B[95m" => ToUiColor(System.Drawing.Color.Magenta),
            "\x1B[96m" => ToUiColor(System.Drawing.Color.Cyan),
            "\x1B[37m" => ToUiColor(System.Drawing.Color.Gray),
            "\x1B[90m" => ToUiColor(System.Drawing.Color.DarkGray),
            "\x1B[97m" => ToUiColor(System.Drawing.Color.White),
            "\x1B[0m" => defaultColor,
            _ => defaultColor,
        };
    }

    private static Color ToUiColor(System.Drawing.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}