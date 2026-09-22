using System.Runtime.InteropServices;
using System.Text;

namespace FootNote.Core;

/// <summary>
/// <see cref="ICommentStore"/> backed by an NTFS alternate data stream —
/// "&lt;path&gt;:FootNote.txt" — the default for files (and folders: directories
/// carry ADS too) on local NTFS drives. The comment lives inside the item, so
/// renames and moves within NTFS carry it automatically, and no extra file
/// ever appears next to the original.
///
/// Writing or deleting a stream would normally bump the host's LastWriteTime,
/// so original timestamps are captured and restored around every mutation —
/// the file or folder must look untouched.
/// </summary>
public sealed class AdsHelper : ICommentStore
{
    public const string StreamName = "FootNote.txt";

    /// <summary>Stream name from the app's previous life as FileTag. Never written
    /// again, only read as a fallback and migrated away from — see <see cref="MigrateLegacy"/>.</summary>
    public const string LegacyStreamName = "FileTag.txt";

    private static string StreamPath(string filePath) => filePath + ":" + StreamName;
    private static string LegacyStreamPath(string filePath) => filePath + ":" + LegacyStreamName;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW(string lpFileName);
    private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    /// <summary>File.Exists is false for streams on <em>directories</em> (the
    /// directory attribute leaks through), so existence is checked natively.</summary>
    private static bool StreamExists(string streamPath)
    {
        try { return GetFileAttributesW(streamPath) != INVALID_FILE_ATTRIBUTES; }
        catch { return false; }
    }

    /// <summary>File.ReadAllText fails on streams rooted at a <em>directory</em> —
    /// opening a directory-backed handle needs FILE_FLAG_BACKUP_SEMANTICS, which the
    /// managed File APIs never set. Mirrors the StreamExists workaround above, for
    /// the actual content read.</summary>
    private static string ReadAllTextNative(string streamPath)
    {
        using var handle = CreateFileW(streamPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException($"Unable to open {streamPath}", Marshal.GetLastWin32Error());
        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public bool HasComment(string filePath)
    {
        try { return StreamExists(StreamPath(filePath)) || StreamExists(LegacyStreamPath(filePath)); }
        catch { return false; }
    }

    public NoteHistory? ReadHistory(string filePath)
    {
        bool isDir = Directory.Exists(filePath);

        try
        {
            string raw = isDir ? ReadAllTextNative(StreamPath(filePath)) : File.ReadAllText(StreamPath(filePath));
            return NoteFormat.Parse(raw, File.GetLastWriteTimeUtc(filePath));
        }
        catch (IOException) { /* fall through to legacy */ }
        catch (UnauthorizedAccessException) { return null; }

        try
        {
            string raw = isDir ? ReadAllTextNative(LegacyStreamPath(filePath)) : File.ReadAllText(LegacyStreamPath(filePath));
            return NoteFormat.Parse(raw, File.GetLastWriteTimeUtc(filePath));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>One-time rebrand migration: if a legacy FileTag stream exists and
    /// the new one doesn't, move the content over and remove the old stream.
    /// Safe to call repeatedly — a no-op once migrated. Never throws.</summary>
    public bool MigrateLegacy(string filePath)
    {
        try
        {
            string legacy = LegacyStreamPath(filePath);
            if (!StreamExists(legacy) || StreamExists(StreamPath(filePath))) return false;
            string raw = File.ReadAllText(legacy);
            WithTimestampsPreserved(filePath, () =>
            {
                File.WriteAllText(StreamPath(filePath), raw);
                File.Delete(legacy);
            });
            return true;
        }
        catch { return false; }
    }

    public void WriteHistory(string filePath, NoteHistory history)
    {
        string json = NoteFormat.Serialize(history);
        WithTimestampsPreserved(filePath, () => File.WriteAllText(StreamPath(filePath), json));
    }

    public void Delete(string filePath)
    {
        string sp = StreamPath(filePath);
        try
        {
            if (!StreamExists(sp)) return;
            WithTimestampsPreserved(filePath, () => File.Delete(sp));
        }
        catch { /* stream gone or inaccessible — nothing to manage */ }
    }

    private static void WithTimestampsPreserved(string filePath, Action action)
    {
        bool isDir = Directory.Exists(filePath);
        DateTime created = default, written = default, accessed = default;
        bool captured = false;
        try
        {
            created = File.GetCreationTimeUtc(filePath);
            written = File.GetLastWriteTimeUtc(filePath);
            accessed = File.GetLastAccessTimeUtc(filePath);
            captured = true;
        }
        catch { /* best effort */ }

        action();

        if (!captured) return;
        try
        {
            if (isDir)
            {
                Directory.SetCreationTimeUtc(filePath, created);
                Directory.SetLastWriteTimeUtc(filePath, written);
                Directory.SetLastAccessTimeUtc(filePath, accessed);
            }
            else
            {
                File.SetCreationTimeUtc(filePath, created);
                File.SetLastWriteTimeUtc(filePath, written);
                File.SetLastAccessTimeUtc(filePath, accessed);
            }
        }
        catch { /* best effort */ }
    }
}
