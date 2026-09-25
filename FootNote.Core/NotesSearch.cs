namespace FootNote.Core;

/// <summary>One matching note, ready for display in the search UI.</summary>
public sealed class SearchResult
{
    public string Path { get; set; } = "";
    public string Snippet { get; set; } = "";
    public bool IsFolder { get; set; }
    public DateTime? NoteAt { get; set; }
}

/// <summary>
/// Searches every note FootNote has ever backed up (live or deleted — see
/// <see cref="NotesBackup"/>) by filename or note text. Purely a read over the
/// existing backup file; no separate index to keep in sync.
/// </summary>
public static class NotesSearch
{
    private const int MaxResults = 50;
    private const int SnippetContext = 35; // chars of context on each side of a match

    /// <summary>Case-insensitive substring search across filenames and note text.
    /// Never throws — an empty query or any failure yields no results.</summary>
    public static List<SearchResult> Search(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return new();
            string q = query.Trim();

            var scored = new List<(SearchResult Result, bool FilenameMatch, DateTime SortAt)>();

            foreach (var entry in NotesBackup.LoadAll())
            {
                if (entry.DeletedAtUtc is not null) continue;
                bool isFile = File.Exists(entry.Path);
                bool isFolder = !isFile && Directory.Exists(entry.Path);
                if (!isFile && !isFolder) continue;

                string text = entry.History.Latest?.Text ?? "";
                string fileName = Path.GetFileName(entry.Path);

                int textIdx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = fileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (textIdx < 0 && !nameMatch) continue;

                var result = new SearchResult
                {
                    Path = entry.Path,
                    IsFolder = isFolder,
                    NoteAt = entry.History.Latest?.At,
                    Snippet = BuildSnippet(text, textIdx, q.Length),
                };
                DateTime sortAt = entry.History.Latest?.At ?? DateTime.MinValue;
                scored.Add((result, nameMatch, sortAt));
            }

            return scored
                .OrderByDescending(s => s.FilenameMatch)
                .ThenByDescending(s => s.SortAt)
                .Select(s => s.Result)
                .Take(MaxResults)
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>~70-char window centered on the match, or a plain truncation
    /// when the query only matched the filename.</summary>
    private static string BuildSnippet(string text, int matchIndex, int matchLength)
    {
        const int maxLen = 80;
        if (string.IsNullOrEmpty(text)) return "";

        if (matchIndex < 0)
            return text.Length <= maxLen ? text : text[..maxLen] + "…";

        int start = Math.Max(0, matchIndex - SnippetContext);
        int end = Math.Min(text.Length, matchIndex + matchLength + SnippetContext);
        string snippet = text[start..end];
        if (start > 0) snippet = "…" + snippet;
        if (end < text.Length) snippet += "…";
        return snippet;
    }
}
