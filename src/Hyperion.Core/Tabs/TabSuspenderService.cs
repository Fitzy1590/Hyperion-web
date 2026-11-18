namespace Hyperion.Core.Tabs;

/// <summary>Host-supplied view of one tab. Delegates keep this service free of WebView2/UI types.</summary>
public sealed class SuspendableTab(
    Guid id,
    Func<bool> isPinned,
    Func<bool> isPlayingMedia,
    Func<Task<bool>> suspendAsync,
    Action resume,
    DateTimeOffset now)
{
    public Guid Id { get; } = id;
    public bool IsActive { get; internal set; }
    public bool IsSuspended { get; internal set; }
    public DateTimeOffset LastActive { get; internal set; } = now;
    internal Func<bool> IsPinned { get; } = isPinned;
    internal Func<bool> IsPlayingMedia { get; } = isPlayingMedia;
    internal Func<Task<bool>> SuspendAsync { get; } = suspendAsync;
    internal Action Resume { get; } = resume;
}

public sealed class TabSuspenderService(TimeProvider? clock = null) : IDisposable
{
    public static readonly int[] AllowedMinutes = [5, 15, 30, 60];

    readonly TimeProvider _clock = clock ?? TimeProvider.System;
    readonly Dictionary<Guid, SuspendableTab> _tabs = [];
    CancellationTokenSource? _cts;

    public TimeSpan SuspendAfter { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>(tab id, isSuspended)</summary>
    public event Action<Guid, bool>? SuspendedChanged;
    public event Action<Guid, Exception>? SuspendFailed;

    public SuspendableTab Register(Guid id, Func<bool> isPinned, Func<bool> isPlayingMedia, Func<Task<bool>> suspendAsync, Action resume)
    {
        var tab = new SuspendableTab(id, isPinned, isPlayingMedia, suspendAsync, resume, _clock.GetUtcNow());
        _tabs[id] = tab;
        return tab;
    }

    public void Unregister(Guid id) => _tabs.Remove(id);

    /// <summary>Marks the tab as the visible one; resumes it if it was suspended.</summary>
    public void Activate(Guid id)
    {
        if (!_tabs.TryGetValue(id, out var t)) return;
        t.IsActive = true;
        t.LastActive = _clock.GetUtcNow();
        if (t.IsSuspended)
        {
            t.Resume();
            t.IsSuspended = false;
            SuspendedChanged?.Invoke(id, false);
        }
    }

    public void Deactivate(Guid id)
    {
        if (!_tabs.TryGetValue(id, out var t)) return;
        t.IsActive = false;
        t.LastActive = _clock.GetUtcNow();
    }

    public static bool ShouldSuspend(SuspendableTab t, DateTimeOffset now, TimeSpan after) =>
        !t.IsActive && !t.IsSuspended && !t.IsPinned() && !t.IsPlayingMedia() && now - t.LastActive >= after;

    public async Task SweepAsync()
    {
        var now = _clock.GetUtcNow();
        foreach (var t in _tabs.Values.ToArray())
        {
            if (!ShouldSuspend(t, now, SuspendAfter)) continue;
            try
            {
                if (!await t.SuspendAsync()) continue; // retried on the next sweep
                t.IsSuspended = true;
                SuspendedChanged?.Invoke(t.Id, true);
            }
            catch (Exception ex)
            {
                SuspendFailed?.Invoke(t.Id, ex);
            }
        }
    }

    /// <summary>Call from the UI thread: the loop continues on the captured context, as TrySuspendAsync requires.</summary>
    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) await SweepAsync();
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
