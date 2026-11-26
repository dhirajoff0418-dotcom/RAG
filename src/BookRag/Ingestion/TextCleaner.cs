using System.Text.RegularExpressions;

namespace BookRag.Ingestion;

/// <summary>
/// Cleans raw TXT file content before chunking.
///
/// Handles Project Gutenberg-style files that wrap the actual content
/// between START / END markers, and removes common noise like page number
/// lines and excessive blank lines.
/// </summary>
public static class TextCleaner
{
    private const string StartMarker = "*** START OF THE BOOK ***";
    private const string EndMarker   = "*** END OF THE BOOK ***";

    public static string Clean(string raw)
    {
        // 1. Normalise line endings so all subsequent regex work on \n only.
        var text = raw.Replace("\r\n", "\n");

        // 2. If the file has START / END markers (Gutenberg style), extract
        //    only the content between them and discard the boilerplate.
        var start = text.IndexOf(StartMarker, StringComparison.Ordinal);
        var end   = text.IndexOf(EndMarker,   StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            text = text.Substring(
                start + StartMarker.Length,
                end   - (start + StartMarker.Length));
        }

        // 3. Remove "Page N" lines that add no semantic content.
        text = Regex.Replace(text, @"^\s*Page \d+\s*$", "", RegexOptions.Multiline);

        // 4. Collapse runs of three or more blank lines to a single blank line
        //    so the chunker sees clean paragraph breaks.
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }
}
