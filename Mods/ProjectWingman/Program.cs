using System.Net.Http.Json;
using System.Text.Json;
using QuadroSense.ProjectWingman;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
if (args.Contains("--self-test")) { SelfTest.Run(); return; }
if (args.Contains("--probe")) { MemoryProbe.Run(); return; }
var configIndex = Array.IndexOf(args, "--config");
if (configIndex >= 0 && configIndex + 1 == args.Length) throw new ArgumentException("--config requires a path");
var configPath = configIndex >= 0 ? args[configIndex + 1] : Path.Combine(AppContext.BaseDirectory, "project-wingman.json");
var config = JsonSerializer.Deserialize<AdapterConfig>(await File.ReadAllTextAsync(configPath), options)!;
if (args.Contains("--telemetry"))
{
    using var diagnostic = new WingmanMemoryReader(config.Memory);
    for (var i = 0; i < 240; i++)
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} {diagnostic.Sample()} [{diagnostic.Status}]");
        await Task.Delay(250);
    }
    return;
}
var liveTest = args.Contains("--live-test");
var sessionTest = args.Contains("--session-test");
if (!config.Enabled && !liveTest && !sessionTest) { Console.WriteLine("Integration disabled: configure verified memory chains, then set enabled=true. Use --self-test to verify effect logic."); return; }
if (!Uri.TryCreate(config.QuadSenseBaseUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
    throw new InvalidDataException("QuadroSense endpoint must be loopback.");
using var lifetime = new CancellationTokenSource();
if (liveTest) lifetime.CancelAfter(TimeSpan.FromSeconds(60));
if (sessionTest) lifetime.CancelAfter(TimeSpan.FromMinutes(5));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
using var http = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(2) };
using var reader = new WingmanMemoryReader(config.Memory);
var engine = new EffectEngine();
string? modId = null;
var registrationId = Guid.NewGuid().ToString();
string? lastStatus = null;
using var sessionLog = sessionTest ? new StreamWriter(Path.Combine(AppContext.BaseDirectory, "session-test.jsonl"), append: true) { AutoFlush = true } : null;
WingmanState? lastLoggedState = null;
WingmanState? lastConsoleState = null;
long lastLoggedAt = 0;
try
{
    while (!lifetime.IsCancellationRequested)
    {
        try
        {
            var state = reader.Sample();
            if (sessionTest && (state != lastLoggedState || Environment.TickCount64 - lastLoggedAt >= 2000))
            {
                var line = JsonSerializer.Serialize(new { Timestamp = DateTimeOffset.UtcNow, State = state, reader.Status });
                await sessionLog!.WriteLineAsync(line);
                // Keep full telemetry on disk, but print only semantic transitions.
                var consoleState = state with { GForce = 1, Throttle = 0, Brake = 0, SpeedKmh = 0 };
                if (consoleState != lastConsoleState)
                {
                    Console.WriteLine(line);
                    lastConsoleState = consoleState;
                }
                lastLoggedState = state;
                lastLoggedAt = Environment.TickCount64;
            }
            if (reader.Status != lastStatus) { Console.WriteLine(reader.Status); lastStatus = reader.Status; }
            if (modId is null)
            {
                using var registration = await http.PostAsJsonAsync("/api/mods/register", new { Id = registrationId, Name = "Project Wingman 2.1.0", Version = "0.2.0", ApiContract = "1.0" }, lifetime.Token);
                registration.EnsureSuccessStatusCode();
                modId = (await registration.Content.ReadFromJsonAsync<Registration>(options, lifetime.Token))!.Id;
            }
            var frame = engine.Compose(state, Environment.TickCount64);
            using var response = await http.PostAsJsonAsync($"/api/mods/{modId}/event", frame, options, lifetime.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) modId = null;
            else response.EnsureSuccessStatusCode();
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !lifetime.IsCancellationRequested)
        {
            Console.WriteLine($"QuadroSense unavailable: {e.Message}");
            modId = null;
            await Task.Delay(1000, lifetime.Token);
        }
        await Task.Delay(Math.Clamp(config.PollIntervalMs, 16, 100), lifetime.Token);
    }
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
finally
{
    if (modId is not null)
    {
        try { using var response = await http.DeleteAsync($"/api/mods/{modId}"); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Console.WriteLine(e.Message); }
    }
}
record Registration(string Id);
