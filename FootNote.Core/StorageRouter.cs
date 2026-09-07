using System.Collections.Concurrent;

namespace FootNote.Core;

/// <summary>
/// Picks the storage backend per path and exposes the only comment API the
/// rest of the app uses. Routing rule (spec):
///
///   inside a cloud-sync folder → sidecar   (survives sync, shows up cross-device)
///   local NTFS drive           → ADS       (invisible, zero extra files)
///   non-NTFS drive             → sidecar   (FAT32/exFAT USB sticks etc.)
///
/// Reads are resilient to files that moved between worlds (e.g. dragged from a
/// local folder into OneDrive with the ADS comment still attached): the routed
/// backend is tried first, then the other. Saving migrates the comment to the
/// correct backend for where the file lives now.
/// </summary>
public static class StorageRouter
{
    public const int MaxCommentLength = 500;

    private static readonly AdsHelper Ads = new();
    private static readonly SidecarHelper Sidecar = new();
    private static readonly ConcurrentDictionary<string, bool> NtfsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ICommentStore RouteFor(string filePath)
    {
        if (CloudFolderDetector.IsInCloudFolder(filePath)) return Sidecar;
        return IsNtfs(filePath) ? Ads : Sidecar;
    }

    private static ICommentStore OtherThan(ICommentStore store) =>
        ReferenceEquals(store, Ads) ? Sidecar : Ads;

    /// <summary>Rebrand migration for a single already-tagged path: moves a legacy
    /// FileTag-named stream or sidecar to its FootNote-named equivalent. No-op
    /// once migrated, or if the path never had a legacy comment. Never throws.</summary>
    public static bool MigrateLegacy(string filePath)
    {
        try { return Ads.MigrateLegacy(filePath) | Sidecar.MigrateLegacy(filePath); }
        catch { return false; }
    }

    public static bool HasComment(string filePath)
    {
        var routed = RouteFor(filePath);
        return routed.HasComment(filePath) || OtherThan(routed).HasComment(filePath);
    }

    /// <summary>Newest entry as the UI-facing <see cref="Note"/>, or null.</summary>
    public static Note? ReadLatest(string filePath)
    {
        var latest = ReadHistory(filePath)?.Latest;
        return latest is null ? null : new Note { Text = latest.Text, ModifiedUtc = latest.At };
    }

    public static NoteHistory? ReadHistory(string filePath)
    {
        var routed = RouteFor(filePath);
        return routed.ReadHistory(filePath) ?? OtherThan(routed).ReadHistory(filePath);
    }

    /// <summary>Appends a new history entry and persists. Empty text deletes the comment.</summary>
    public static void Save(string filePath, string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0) { Delete(filePath); return; }
        if (text.Length > MaxCommentLength) text = text[..MaxCommentLength];

        var routed = RouteFor(filePath);
        var other = OtherThan(routed);

        var history = routed.ReadHistory(filePath) ?? other.ReadHistory(filePath) ?? new NoteHistory();
        history.Append(text, DateTime.UtcNow);
        routed.WriteHistory(filePath, history);

        // The comment now lives in the right backend — drop any stale copy.
        if (other.HasComment(filePath)) other.Delete(filePath);

        if (ReferenceEquals(routed, Sidecar)) EnsureCloudRootReadme(filePath);

        ProFeatures.Current.RecordSave(filePath, history);
    }

    /// <summary>
    /// Removes every sidecar note under a sync root, plus the explanatory
    /// readme FootNote dropped there. The index is only a cache and can be lost,
    /// which would strand notes with no app left to manage them; ADS notes are
    /// invisible and unfindable without a full-drive scan, but sidecars are
    /// visible to anyone sharing the folder — and sync roots are enumerable, so
    /// these are exactly the leftovers worth sweeping. Returns how many were
    /// removed. Never throws.
    /// </summary>
    public static int SweepSidecars(string root, Action<string>? onEach = null)
    {
        int removed = 0;
        try
        {
            if (!Directory.Exists(root)) return 0;
            foreach (string sidecar in Directory.EnumerateFiles(
                         root, "*" + SidecarHelper.Suffix, SearchOption.AllDirectories))
            {
                try
                {
                    onEach?.Invoke(sidecar);
                    File.SetAttributes(sidecar, FileAttributes.Normal);
                    File.Delete(sidecar);
                    removed++;
                }
                catch { /* locked/denied — leave it, keep sweeping */ }
            }

            // Google Drive letter-mounts keep the readme under "My Drive".
            foreach (string r in new[] { Path.Combine(root, CloudReadmeName),
                                         Path.Combine(root, "My Drive", CloudReadmeName) })
            {
                try { if (File.Exists(r)) File.Delete(r); } catch { }
            }
        }
        catch { /* unreadable root — nothing to do */ }
        return removed;
    }

    public const string CloudReadmeName = "_FootNote_ReadMe.txt";

    /// <summary>
    /// Sidecar files aren't invisible to collaborators — anyone sharing the
    /// Drive/OneDrive folder sees ".footnote" files on the web, on a phone, on
    /// a Mac. So the first write into a detected sync root drops a one-time
    /// explanatory readme there (only if absent), turning a confusing stray
    /// file into a self-explanatory one.
    /// </summary>
    private static void EnsureCloudRootReadme(string filePath)
    {
        try
        {
            string? root = CloudFolderDetector.GetRootFor(filePath);
            if (root is null) return; // non-cloud sidecar (USB stick) — skip

            // Google Drive letter-mounts: the virtual drive root (I:\) is not
            // writable — "My Drive" beneath it is the real writable root.
            if (string.Equals(Path.GetPathRoot(root), root, StringComparison.OrdinalIgnoreCase))
            {
                string myDrive = Path.Combine(root, "My Drive");
                if (Directory.Exists(myDrive)) root = myDrive;
            }

            string readme = Path.Combine(root, CloudReadmeName);
            if (File.Exists(readme)) return;
            File.WriteAllText(readme,
                "About the \".footnote\" files in this folder\r\n" +
                "===========================================\r\n" +
                "\r\n" +
                "These small hidden files are created by FootNote, a free Windows utility\r\n" +
                "that lets people attach personal notes to their files.\r\n" +
                "\r\n" +
                "Each \"<name>.footnote\" holds a short note about the file \"<name>\" sitting\r\n" +
                "next to it, so the note can sync between the owner's computers along with\r\n" +
                "the file itself.\r\n" +
                "\r\n" +
                "They are safe to ignore. Deleting a .footnote file only deletes the note,\r\n" +
                "never the file it belongs to.\r\n");
            Logger.Info($"dropped {CloudReadmeName} at sync root: {root}");
        }
        catch { /* read-only share etc. — the note itself still saved */ }
    }

    /// <summary>Removes the comment from both backends. Files are untouched.
    /// The backup mirror keeps the note (marked deleted) — that's the point.</summary>
    public static void Delete(string filePath)
    {
        ProFeatures.Current.RecordDelete(filePath);
        Ads.Delete(filePath);
        Sidecar.Delete(filePath);
    }

    public static bool IsNtfs(string filePath)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(filePath));
            if (string.IsNullOrEmpty(root)) return false;
            return NtfsCache.GetOrAdd(root, r =>
            {
                try { return new DriveInfo(r).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
        }
        catch { return false; }
    }
}
