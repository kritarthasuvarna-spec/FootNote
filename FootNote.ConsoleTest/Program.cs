using FootNote.Core;

// Isolated proving ground for the storage layer (spec build-order step 1). No UI.
//
// Extra live-probe mode for real-world checks:
//   FootNote.ConsoleTest <path>            → show routing + current comment for a real file
//   FootNote.ConsoleTest <path> <text...>  → save a comment on a real file, then show it
if (args.Length >= 1)
{
    string p = Path.GetFullPath(args[0]);
    Console.WriteLine($"path:      {p}");
    Console.WriteLine($"cloud:     {CloudFolderDetector.IsInCloudFolder(p)}  (roots: {string.Join(" | ", CloudFolderDetector.GetRoots())})");
    Console.WriteLine($"ntfs:      {StorageRouter.IsNtfs(p)}");
    Console.WriteLine($"backend:   {StorageRouter.RouteFor(p).GetType().Name}");
    if (args.Length >= 2)
    {
        StorageRouter.Save(p, string.Join(' ', args.Skip(1)));
        // Mirror what App.xaml.cs does after a real GUI save, so this live-probe
        // path also populates the local backup that Search reads from.
        var savedHistory = StorageRouter.ReadHistory(p);
        if (savedHistory?.Latest is not null) NotesBackup.RecordSave(p, savedHistory);
        Console.WriteLine("saved.");
    }
    Console.WriteLine($"hasComment: {StorageRouter.HasComment(p)}");
    var n = StorageRouter.ReadLatest(p);
    Console.WriteLine(n is null ? "comment:   (none)" : $"comment:   \"{n.Text}\" @ {n.ModifiedUtc:u}");
    var hist = StorageRouter.ReadHistory(p);
    Console.WriteLine($"history:   {hist?.History.Count ?? 0} entries");
    return 0;
}

int failed = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok) failed++;
}

string dir = Path.Combine(Path.GetTempPath(), "FootNoteConsoleTest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
string file = Path.Combine(dir, "victim.txt");
File.WriteAllText(file, "original file content");
var originalWrite = File.GetLastWriteTimeUtc(file);

NotesBackup.SetPathForTesting(Path.Combine(dir, "backup-suite.json.gz"));
try
{
    // ---------- routing on a plain local NTFS path ----------
    CloudFolderDetector.SetRootsForTesting([]); // no cloud roots: pure NTFS routing
    Check("local NTFS routes to ADS", StorageRouter.RouteFor(file) is AdsHelper);

    // ---------- ADS backend basics ----------
    Check("no comment initially", !StorageRouter.HasComment(file));
    Check("read on no comment -> null", StorageRouter.ReadLatest(file) is null);

    StorageRouter.Save(file, "hello from FootNote");
    Check("HasComment after save", StorageRouter.HasComment(file));
    Check("stored as ADS stream", File.Exists(file + ":" + AdsHelper.StreamName));
    Check("no sidecar created", !File.Exists(file + SidecarHelper.Suffix));
    var note = StorageRouter.ReadLatest(file);
    Check("read back text", note?.Text == "hello from FootNote");
    Check("timestamp recent", note is not null && (DateTime.UtcNow - note.ModifiedUtc).TotalMinutes < 1);
    Check("host file content untouched", File.ReadAllText(file) == "original file content");
    Check("host file LastWriteTime preserved", File.GetLastWriteTimeUtc(file) == originalWrite);

    // ---------- history semantics ----------
    StorageRouter.Save(file, "second version");
    Check("latest wins", StorageRouter.ReadLatest(file)?.Text == "second version");
    Check("history keeps both", StorageRouter.ReadHistory(file)?.History.Count == 2);
    Check("history order oldest-first", StorageRouter.ReadHistory(file)?.History[0].Text == "hello from FootNote");
    for (int i = 0; i < 25; i++) StorageRouter.Save(file, $"rev {i}");
    Check("history capped at 20", StorageRouter.ReadHistory(file)?.History.Count == NoteHistory.MaxEntries);
    Check("cap drops oldest", StorageRouter.ReadHistory(file)?.History[0].Text != "hello from FootNote");

    // ---------- rename / move carry the ADS comment ----------
    string renamed = Path.Combine(dir, "renamed.txt");
    File.Move(file, renamed);
    Check("comment survives rename", StorageRouter.ReadLatest(renamed)?.Text == "rev 24");
    string subDir = Path.Combine(dir, "sub");
    Directory.CreateDirectory(subDir);
    string moved = Path.Combine(subDir, "moved.txt");
    File.Move(renamed, moved);
    Check("comment survives move", StorageRouter.ReadLatest(moved)?.Text == "rev 24");
    StorageRouter.Delete(moved);

    // ---------- legacy v1 format + plain text ----------
    File.WriteAllText(moved + ":" + AdsHelper.StreamName,
        "{\"Text\":\"old v1 note\",\"ModifiedUtc\":\"2026-01-01T00:00:00Z\"}");
    Check("legacy v1 JSON readable", StorageRouter.ReadLatest(moved)?.Text == "old v1 note");
    StorageRouter.Save(moved, "upgraded");
    Check("legacy upgrade keeps old entry", StorageRouter.ReadHistory(moved)?.History[0].Text == "old v1 note");
    Check("legacy upgrade appends new", StorageRouter.ReadLatest(moved)?.Text == "upgraded");
    StorageRouter.Delete(moved);

    File.WriteAllText(moved + ":" + AdsHelper.StreamName, "raw plain text");
    Check("plain-text stream readable", StorageRouter.ReadLatest(moved)?.Text == "raw plain text");
    StorageRouter.Delete(moved);

    // ---------- cap / empty-delete ----------
    StorageRouter.Save(moved, new string('x', 600));
    Check("500-char cap enforced", StorageRouter.ReadLatest(moved)?.Text.Length == 500);
    StorageRouter.Save(moved, "   ");
    Check("empty save deletes comment", !StorageRouter.HasComment(moved));
    StorageRouter.Delete(moved);
    Check("delete when absent is no-op", true);

    // ---------- sidecar backend (simulated cloud folder) ----------
    CloudFolderDetector.SetRootsForTesting([dir]);
    Check("cloud folder routes to sidecar", StorageRouter.RouteFor(moved) is SidecarHelper);

    StorageRouter.Save(moved, "cloud note");
    string sidecar = moved + SidecarHelper.Suffix;
    Check("sidecar file created", File.Exists(sidecar));
    Check("sidecar is hidden", (File.GetAttributes(sidecar) & FileAttributes.Hidden) != 0);
    Check("no ADS stream created", !File.Exists(moved + ":" + AdsHelper.StreamName));
    Check("sidecar read back", StorageRouter.ReadLatest(moved)?.Text == "cloud note");

    StorageRouter.Save(moved, "cloud note edited");
    Check("hidden sidecar overwrite works", StorageRouter.ReadLatest(moved)?.Text == "cloud note edited");
    Check("sidecar history grows", StorageRouter.ReadHistory(moved)?.History.Count == 2);

    // ---------- migration: ADS comment on a file that moved into a cloud folder ----------
    CloudFolderDetector.SetRootsForTesting([]);
    string migrant = Path.Combine(subDir, "migrant.txt");
    File.WriteAllText(migrant, "content");
    StorageRouter.Save(migrant, "written as ADS");
    Check("migrant starts as ADS", File.Exists(migrant + ":" + AdsHelper.StreamName));
    CloudFolderDetector.SetRootsForTesting([dir]); // "the folder is now cloud-synced"
    Check("fallback read finds ADS comment", StorageRouter.ReadLatest(migrant)?.Text == "written as ADS");
    StorageRouter.Save(migrant, "now synced");
    Check("save migrates to sidecar", File.Exists(migrant + SidecarHelper.Suffix));
    Check("stale ADS copy removed", !File.Exists(migrant + ":" + AdsHelper.StreamName));
    Check("history preserved across migration", StorageRouter.ReadHistory(migrant)?.History.Count == 2);

    // ---------- delete cleans both backends; orphaned sidecar cleanup ----------
    StorageRouter.Delete(migrant);
    Check("delete removes sidecar", !StorageRouter.HasComment(migrant));
    StorageRouter.Save(moved, "orphan me");
    File.Delete(moved); // original gone, sidecar orphaned
    StorageRouter.Delete(moved);
    Check("orphaned sidecar cleaned up", !File.Exists(sidecar));

    // ---------- folder comments (ADS on a directory) ----------
    CloudFolderDetector.SetRootsForTesting([]);
    string taggedDir = Path.Combine(dir, "tagged-folder");
    Directory.CreateDirectory(taggedDir);
    var dirWrite = Directory.GetLastWriteTimeUtc(taggedDir);
    Check("folder: no comment initially", !StorageRouter.HasComment(taggedDir));
    StorageRouter.Save(taggedDir, "note on a folder");
    Check("folder: HasComment after save", StorageRouter.HasComment(taggedDir));
    Check("folder: read back", StorageRouter.ReadLatest(taggedDir)?.Text == "note on a folder");
    Check("folder: no sidecar on NTFS", !File.Exists(taggedDir + SidecarHelper.Suffix));
    Check("folder: timestamps preserved", Directory.GetLastWriteTimeUtc(taggedDir) == dirWrite);
    StorageRouter.Delete(taggedDir);
    Check("folder: delete works", !StorageRouter.HasComment(taggedDir));

    // ---------- folder comments (sidecar in a cloud folder) ----------
    CloudFolderDetector.SetRootsForTesting([dir]);
    StorageRouter.Save(taggedDir, "cloud folder note");
    Check("folder: cloud sidecar created", File.Exists(taggedDir + SidecarHelper.Suffix));
    Check("folder: cloud read back", StorageRouter.ReadLatest(taggedDir)?.Text == "cloud folder note");
    StorageRouter.Delete(taggedDir);
    Check("folder: cloud delete works", !StorageRouter.HasComment(taggedDir));
    CloudFolderDetector.SetRootsForTesting([]);

    // ---------- cloud-root readme drop ----------
    CloudFolderDetector.SetRootsForTesting([dir]);
    string readme = Path.Combine(dir, StorageRouter.CloudReadmeName);
    string cloudFile = Path.Combine(dir, "readme-trigger.txt");
    File.WriteAllText(cloudFile, "x");
    StorageRouter.Save(cloudFile, "first cloud note");
    Check("readme dropped at sync root", File.Exists(readme));
    var readmeTime = File.GetLastWriteTimeUtc(readme);
    StorageRouter.Save(cloudFile, "second note");
    Check("readme not rewritten", File.GetLastWriteTimeUtc(readme) == readmeTime);
    Check("readme mentions .footnote", File.ReadAllText(readme).Contains(".footnote"));
    StorageRouter.Delete(cloudFile);
    CloudFolderDetector.SetRootsForTesting([]);

    // ---------- orphan sweep (index-loss safety net) ----------
    string sweepRoot = Path.Combine(dir, "syncroot");
    Directory.CreateDirectory(Path.Combine(sweepRoot, "sub"));
    string userFile = Path.Combine(sweepRoot, "keep-me.txt");
    File.WriteAllText(userFile, "user data");
    string orphanHost = Path.Combine(sweepRoot, "sub", "orphan.txt");
    File.WriteAllText(orphanHost, "x");
    CloudFolderDetector.SetRootsForTesting([sweepRoot]);
    StorageRouter.Save(orphanHost, "note the index forgot");
    Check("sweep: sidecar present before", File.Exists(orphanHost + SidecarHelper.Suffix));
    Check("sweep: readme present before", File.Exists(Path.Combine(sweepRoot, StorageRouter.CloudReadmeName)));
    int swept = StorageRouter.SweepSidecars(sweepRoot);
    Check("sweep: removed the orphan", swept == 1);
    Check("sweep: sidecar gone", !File.Exists(orphanHost + SidecarHelper.Suffix));
    Check("sweep: readme gone", !File.Exists(Path.Combine(sweepRoot, StorageRouter.CloudReadmeName)));
    Check("sweep: host file untouched", File.Exists(orphanHost));
    Check("sweep: unrelated user file untouched", File.ReadAllText(userFile) == "user data");
    Check("sweep: rerun is a no-op", StorageRouter.SweepSidecars(sweepRoot) == 0);
    Check("sweep: missing root is safe", StorageRouter.SweepSidecars(Path.Combine(dir, "nope")) == 0);
    CloudFolderDetector.SetRootsForTesting([]);

    // ---------- sidecar guard ----------
    Check("sidecar path detected", SidecarHelper.IsSidecar(sidecar));
    Check("normal path not sidecar", !SidecarHelper.IsSidecar(migrant));

    // ---------- notes backup (last-resort recovery) ----------
    string bkFile = Path.Combine(dir, "backed-up.txt");
    File.WriteAllText(bkFile, "x");
    StorageRouter.Save(bkFile, "first version");
    var bk1 = NotesBackup.LoadAll();
    Check("backup: entry created on save", bk1.Any(e => e.Path == bkFile));
    Check("backup: not marked deleted after save", bk1.First(e => e.Path == bkFile).DeletedAtUtc is null);
    Check("backup: text matches", bk1.First(e => e.Path == bkFile).History.Latest?.Text == "first version");

    StorageRouter.Save(bkFile, "second version");
    var bk2 = NotesBackup.LoadAll();
    Check("backup: updates on re-save", bk2.First(e => e.Path == bkFile).History.Latest?.Text == "second version");
    Check("backup: history accumulates", bk2.First(e => e.Path == bkFile).History.History.Count == 2);

    StorageRouter.Delete(bkFile);
    var bk3 = NotesBackup.LoadAll();
    var deletedEntry = bk3.FirstOrDefault(e => e.Path == bkFile);
    Check("backup: entry SURVIVES delete (the whole point)", deletedEntry is not null);
    Check("backup: marked deleted with a timestamp", deletedEntry?.DeletedAtUtc is not null);
    Check("backup: text still recoverable after delete", deletedEntry?.History.Latest?.Text == "second version");
    Check("live comment actually gone", !StorageRouter.HasComment(bkFile));

    // restoring un-deletes it in the backup too
    StorageRouter.Save(bkFile, "restored");
    Check("backup: un-deleted after restore save", NotesBackup.LoadAll().First(e => e.Path == bkFile).DeletedAtUtc is null);

    // gzip round-trip integrity of the file already in use
    Check("backup: gzip file actually written", File.Exists(NotesBackup.BackupPath));
    Check("backup: gzip round-trip readable", NotesBackup.LoadAll().Any(e => e.History.Latest?.Text == "gzip check" || e.History.Latest?.Text == "restored"));

    // ---------- IndexStore round-trip ----------
    var index = new IndexStore();
    index.AddPath(migrant);
    Check("index add", index.GetPaths().Contains(migrant));
    index.RemovePath(migrant);
    Check("index remove", !index.GetPaths().Contains(migrant));
}
finally
{
    CloudFolderDetector.SetRootsForTesting(null);
    NotesBackup.SetPathForTesting(null);
    try { Directory.Delete(dir, true); } catch { }
}

Console.WriteLine();
Console.WriteLine(failed == 0 ? "ALL TESTS PASSED" : $"{failed} TEST(S) FAILED");
return failed;
