using System.Text.Json.Serialization;
using QuadroSense.Api.HID;
using QuadroSense.Api.Hubs;
using QuadroSense.Api.Services;

namespace QuadroSense.Api;

public static class ApiApplication
{
    public static WebApplication Build(string[] args, string? contentRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(ApiApplication).Assembly.FullName,
            ContentRootPath = contentRoot ?? Directory.GetCurrentDirectory()
        });

        // ── Services ──────────────────────────────────────────────────────────────────

        // Use string enum names everywhere (request bodies, response JSON, OpenAPI schema).
        // Without this, TriggerMode and TriggerSide enums arrive as integers only.
        builder.Services.AddControllers().AddApplicationPart(typeof(ApiApplication).Assembly)
            .AddJsonOptions(o =>
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddSignalR();
        builder.Services.AddOpenApi();

        // HID device manager — singleton, owns the serial HID stream
        builder.Services.AddSingleton<ControllerDeviceManager>();

        // Controller audio service — synthesizer + speaker output
        builder.Services.AddSingleton<ControllerAudioService>();

        // Core output pipeline — serializes all HID output report writes
        builder.Services.AddSingleton<ControllerOutputService>();

        // Audio-to-haptic pipeline — WASAPI loopback + DSP background service
        builder.Services.AddSingleton<AudioHapticService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AudioHapticService>());

        // Profile manager with auto-switch background loop
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ProfileService>());

        // Mod system with TTL watchdog
        builder.Services.AddSingleton<ModService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ModService>());

        // Button remapping + macro runner
        builder.Services.AddSingleton<RemappingService>();

        // SignalR diagnostics broadcaster
        builder.Services.AddHostedService<DiagnosticsBroadcaster>();

        // ── App ───────────────────────────────────────────────────────────────────────

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.MapControllers();
        app.MapHub<ControllerHub>("/ws/controller");

        // Auto-connect on startup (best-effort)
        var outputService = app.Services.GetRequiredService<ControllerOutputService>();
        outputService.TryConnect();

        return app;


    }
}
