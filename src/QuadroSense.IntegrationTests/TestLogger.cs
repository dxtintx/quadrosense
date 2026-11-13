using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuadroSense.IntegrationTests;

public enum LogLevel { Trace, Debug, Info, Warning, Error, Pass }

public sealed class TestLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel Level { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? Error { get; set; }
}

public sealed class TestSessionReport
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public int TotalSteps { get; set; }
    public int PassedSteps { get; set; }
    public int FailedSteps { get; set; }
    public List<TestLogEntry> Entries { get; set; } = new();
}

/// <summary>
/// Writes structured JSON Lines (.jsonl) and summary JSON report directly to
/// the test root / workspace root so an AI assistant can inspect logs immediately.
/// </summary>
public static class TestLogger
{
    private static readonly object _lock = new();
    private static string _currentPhase = "Startup";
    private static readonly TestSessionReport _report = new();

    private static readonly string _logDir;
    private static readonly string _jsonlPath;
    private static readonly string _summaryPath;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions _reportOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static TestLogger()
    {
        // Target repository test logs directory: d:\Projects\quadrosense\test-logs\
        // and also copy to latest_test_report.json in root
        string projectDir = AppContext.BaseDirectory;
        var dirInfo = new DirectoryInfo(projectDir);

        // Find root where FEATURES.md or QuadroSense.slnx lives
        DirectoryInfo? cur = dirInfo;
        string root = projectDir;
        while (cur != null)
        {
            if (File.Exists(Path.Combine(cur.FullName, "FEATURES.md")) ||
                File.Exists(Path.Combine(cur.FullName, "QuadroSense.slnx")))
            {
                root = cur.FullName;
                break;
            }
            cur = cur.Parent;
        }

        _logDir = Path.Combine(root, "test-logs");
        Directory.CreateDirectory(_logDir);

        _jsonlPath = Path.Combine(_logDir, "latest_test_run.jsonl");
        _summaryPath = Path.Combine(_logDir, "latest_test_summary.json");

        // Reset latest file for fresh run
        try
        {
            if (File.Exists(_jsonlPath)) File.Delete(_jsonlPath);
        }
        catch { }
    }

    public static string LogFilePath => _jsonlPath;
    public static string SummaryFilePath => _summaryPath;

    public static void SetPhase(string phase)
    {
        lock (_lock)
        {
            _currentPhase = phase;
            Log(LogLevel.Info, "PHASE_START", $"Entered Phase: {phase}");
        }
    }

    public static void Step(string step, string detail = "", object? data = null)
    {
        lock (_lock)
        {
            _report.TotalSteps++;
            Log(LogLevel.Info, step, detail, data);
        }
    }

    public static void Pass(string step, string message, object? data = null)
    {
        lock (_lock)
        {
            _report.PassedSteps++;
            Log(LogLevel.Pass, step, message, data);
        }
    }

    public static void Warn(string step, string message, object? data = null)
    {
        lock (_lock)
        {
            Log(LogLevel.Warning, step, message, data);
        }
    }

    public static void Error(string step, string message, Exception? ex = null, object? data = null)
    {
        lock (_lock)
        {
            _report.FailedSteps++;
            Log(LogLevel.Error, step, message, data, ex?.ToString());
        }
    }

    public static void Complete(bool success, string? failureReason = null)
    {
        lock (_lock)
        {
            _report.FinishedAt = DateTime.UtcNow;
            _report.Success = success;
            _report.FailureReason = failureReason;

            try
            {
                var summaryJson = JsonSerializer.Serialize(_report, _reportOpts);
                File.WriteAllText(_summaryPath, summaryJson);
            }
            catch { }
        }
    }

    private static void Log(LogLevel level, string step, string message, object? data = null, string? error = null)
    {
        var entry = new TestLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Phase = _currentPhase,
            Step = step,
            Message = message,
            Data = data,
            Error = error
        };

        _report.Entries.Add(entry);

        try
        {
            var line = JsonSerializer.Serialize(entry, _jsonOpts);
            File.AppendAllText(_jsonlPath, line + Environment.NewLine);
        }
        catch { }
    }
}
