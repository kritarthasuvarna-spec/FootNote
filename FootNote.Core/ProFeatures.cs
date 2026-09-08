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
/// <summary>How a locked build's Settings upsell should ask the user to
/// unlock — determines which control the Free-tier upsell UI shows.</summary>
public enum UnlockMethod
{
    /// <summary>Nothing to show — already unlocked (Owner), or there is no
    /// unlock path at all (this is the Free build itself).</summary>
    None,
    /// <summary>Show a "Buy" button that opens the Store's own purchase UI.</summary>
    StorePurchase,
    /// <summary>Show a license-key entry box (the GitHub/direct-sale build).</summary>
    LicenseKey,
}

public interface IProFeatures
{
    /// <summary>True for the Store-Paid (after purchase), Direct (after a
    /// validated license key), and Owner builds; false for Git-Free, an
    /// unpurchased Store-Paid install, and a Direct install with no key yet.
    /// For Store/Direct this reflects the last completed
    /// <see cref="InitializeAsync"/> — call that once at startup before
    /// trusting this value.</summary>
    bool Unlocked { get; }

    /// <summary>Which control the Settings upsell should show when locked.
    /// Irrelevant when <see cref="Unlocked"/> is already true.</summary>
    UnlockMethod Method { get; }

    /// <summary>Runs once at startup, before anything reads <see cref="Unlocked"/>.
    /// The Owner build's implementation returns immediately (already unlocked,
    /// no external check needed); Store and Direct await a license check —
    /// Direct's has an offline grace period, so this only truly blocks on
    /// network the first run or once that grace period has lapsed.
    /// No-op on the Free build.</summary>
    Task InitializeAsync();

    /// <summary>Shows the Store's purchase UI for the unlock add-on and
    /// updates <see cref="Unlocked"/> on success. Only meaningful when
    /// <see cref="Method"/> is <see cref="UnlockMethod.StorePurchase"/> —
    /// no-op (returns false) everywhere else.</summary>
    Task<bool> TryPurchaseUnlockAsync();

    /// <summary>Activates a license key purchased outside the Store (Lemon
    /// Squeezy) and updates <see cref="Unlocked"/> on success. Only
    /// meaningful when <see cref="Method"/> is
    /// <see cref="UnlockMethod.LicenseKey"/> — no-op everywhere else.</summary>
    Task<(bool Success, string? Error)> TryActivateLicenseAsync(string licenseKey);

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
    public UnlockMethod Method => UnlockMethod.None;
    public Task InitializeAsync() => Task.CompletedTask;
    public Task<bool> TryPurchaseUnlockAsync() => Task.FromResult(false);
    public Task<(bool Success, string? Error)> TryActivateLicenseAsync(string licenseKey) =>
        Task.FromResult<(bool, string?)>((false, "Not supported in this build."));
    public void RecordSave(string path, NoteHistory history) { }
    public void RecordDelete(string path) { }
    public bool TryOpenHistory(string path) => false;
}

public static class ProFeatures
{
    public static IProFeatures Current { get; set; } = new NullProFeatures();
}
