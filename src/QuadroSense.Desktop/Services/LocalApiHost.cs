using Microsoft.AspNetCore.Builder;
using QuadroSense.Api;
using Microsoft.Extensions.DependencyInjection;
using QuadroSense.Api.Services;

namespace QuadroSense.Desktop.Services;

/// <summary>Owns only the API instance started by this desktop process.</summary>
public sealed class LocalApiHost : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnsureRunningAsync(QuadroSenseApi api)
    {
        await _gate.WaitAsync();
        try
        {
            try { await api.GetDiagnosticsAsync(); return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            // A custom server address is managed by its owner.
            if (!string.Equals(api.BaseUrl.TrimEnd('/'), QuadroSenseApi.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Service unavailable at {api.BaseUrl}. Check the address and start that API service.");

            if (_app is not null) throw new InvalidOperationException("The local API stopped responding. Restart QuadroSense to reconnect.");
            var app = ApiApplication.Build(["--urls", QuadroSenseApi.DefaultBaseUrl], AppContext.BaseDirectory);
            try { await app.StartAsync(); _app = app; }
            catch
            {
                await app.DisposeAsync();
                // Another desktop instance may have started the server during our check.
                await api.GetDiagnosticsAsync();
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_app is null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _app.StopAsync(timeout.Token); }
            finally
            {
                try { await _app.Services.GetRequiredService<ControllerOutputService>().ResetForShutdownAsync(); }
                finally { await _app.DisposeAsync(); _app = null; }
            }
        }
        finally { _gate.Release(); }
    }
}
