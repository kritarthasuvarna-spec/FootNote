namespace FootNote.Core;

/// <summary>
/// The seam between the free build and the Pro build. The real
/// implementation lives in FootNote.Pro — a project that is never part of
/// the public repo — so gating here is a project reference, not a runtime
/// flag: a Free build simply never links a real implementation in, and
/// there is nothing to flip or recompile your way around from public
/// source. <see cref="Current"/> defaults to <see cref="NullProFeatures"/>;
/// the Pro app entry point is the only place that ever replaces it.
/// </summary>
public interface IProFeatures
{
    /// <summary>True for the Store-Paid (after purchase) and Owner builds;
    /// false for Git, Store-Free, and a not-yet-purchased Store-Paid install.
    /// For the Store channel this reflects the last completed
    /// <see cref="InitializeAsync"/> — call that once at startup before
    /// trusting this value.</summary>
    bool Unlocked { get; }

    /// <summary>Runs once at startup, before anything reads <see cref="Unlocked"/>.
    /// The Owner build's implementation returns immediately (already unlocked,
    /// no external check needed); the Store channel's awaits a license check.
    /// No-op on the Free build.</summary>
    Task InitializeAsync();

    /// <summary>Shows the Store's purchase UI for the unlock add-on and
    /// updates <see cref="Unlocked"/> on success. Only meaningful on the
    /// Store channel — no-op (returns false) everywhere else, including the
    /// Owner build, which has nothing to purchase.</summary>
    Task<bool> TryPurchaseUnlockAsync();

    /// <summary>Mirrors a note's current history into the local backup safety net.
    /// No-op on a build that doesn't have backup/Recover Notes.</summary>
    void RecordSave(string path, NoteHistory history);

    /// <summary>Marks a note as deleted in the backup (keeps last known text, doesn't erase it).</summary>
    void RecordDelete(string path);

    /// <summary>Opens the version-history browser for a note. No-op (returns false)
    /// on a build that doesn't have it — callers should hide the entry point
    /// entirely rather than rely on this silently doing nothing.</summary>
    bool TryOpenHistory(string path);
}

/// <summary>The Free-build default: every Pro feature is simply absent.</summary>
public sealed class NullProFeatures : IProFeatures
{
    public bool Unlocked => false;
    public Task InitializeAsync() => Task.CompletedTask;
    public Task<bool> TryPurchaseUnlockAsync() => Task.FromResult(false);
    public void RecordSave(string path, NoteHistory history) { }
    public void RecordDelete(string path) { }
    public bool TryOpenHistory(string path) => false;
}

public static class ProFeatures
{
    public static IProFeatures Current { get; set; } = new NullProFeatures();
}
