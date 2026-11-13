using System.Diagnostics;

namespace QuadroSense.Desktop.Services;

/// <summary>Starts installed game adapters when their game process appears.</summary>
public sealed class GameIntegrationLauncher : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _monitorTask;
    private Process? _ownedAdapter;

    public string? ActiveAdapterPath { get; private set; }

    public GameIntegrationLauncher()
    {
        _monitorTask = MonitorAsync(_stop.Token);
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var gameRunning = Process.GetProcessesByName("ProjectWingman-Win64-Shipping").Length > 0;
                if (gameRunning) StartIfNeeded();
                else StopOwnedAdapter();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { StopOwnedAdapter(); }
    }

    private void StartIfNeeded()
    {
        if (_ownedAdapter is { HasExited: false }) return;

        // If the player started an adapter manually, leave it alone.
        if (Process.GetProcessesByName("QuadroSense.ProjectWingman").Length > 0) return;

        var adapter = FindAdapter();
        if (adapter is null) return;

        try
        {
            _ownedAdapter = Process.Start(new ProcessStartInfo
            {
                FileName = adapter,
                WorkingDirectory = Path.GetDirectoryName(adapter)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            ActiveAdapterPath = adapter;
        }
        catch
        {
            _ownedAdapter = null;
            ActiveAdapterPath = null;
        }
    }

    private void StopOwnedAdapter()
    {
        if (_ownedAdapter is null) return;
        try
        {
            if (!_ownedAdapter.HasExited) _ownedAdapter.CloseMainWindow();
            if (!_ownedAdapter.HasExited) _ownedAdapter.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            _ownedAdapter.Dispose();
            _ownedAdapter = null;
            ActiveAdapterPath = null;
        }
    }

    private static string? FindAdapter()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoots(AppContext.BaseDirectory, roots);
        AddRoots(Directory.GetCurrentDirectory(), roots);

        foreach (var root in roots)
        foreach (var relative in new[]
        {
            Path.Combine("Mods", "ProjectWingman", "QuadroSense.ProjectWingman.exe"),
            Path.Combine("Mods", "QuadroSense.ProjectWingman", "QuadroSense.ProjectWingman.exe"),
            Path.Combine("ModsIntegrations", "ProjectWingman", "QuadroSense.ProjectWingman.exe"),
            Path.Combine("ModsIntegrations", "QuadroSense.ProjectWingman", "QuadroSense.ProjectWingman.exe")
        })
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void AddRoots(string? start, HashSet<string> roots)
    {
        if (string.IsNullOrWhiteSpace(start)) return;
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        for (var depth = 0; directory is not null && depth < 5; depth++, directory = directory.Parent)
            roots.Add(directory.FullName);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _monitorTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
