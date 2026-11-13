using System.Diagnostics;

namespace QuadroSense.IntegrationTests;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.Title = "QuadroSense End-to-End Automated Hardware Test";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ____                 _           ____
  / __ \__  ______ _____| |_______ / __ \____ ___  ___  ____  ___
 / / / / / / / __ `/ __  / ___/ _ \_ \/ __ `/ __ \/ _ \/ __ \/ __/
/ /_/ / /_/ / /_/ / /_/ / /  /  ___/_/ / /_/ / / / /  __/ / / / /_
\___\_\__,_/\__,_/\__,_/_/   \___/____/\__,_/_/ /_/\___/_/ /_/\__/

              AUTOMATED HARDWARE ENGAGEMENT TEST SUITE
");
        Console.ResetColor();

        // 1. Determine API endpoint or launch API if not already running
        string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
        using var client = new ApiClient(baseUrl);

        Process? apiProcess = null;
        if (!await client.IsAliveAsync())
        {
            Console.WriteLine($"QuadroSense API is not reachable at {baseUrl}.");
            Console.WriteLine("Spawning local API server instance...");

            var apiProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "QuadroSense.Api"));
            if (!Directory.Exists(apiProjectDir))
            {
                // Fallback check
                apiProjectDir = Path.GetFullPath(@"d:\Projects\quadrosense\src\QuadroSense.Api");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --urls http://localhost:5000",
                WorkingDirectory = apiProjectDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            try
            {
                apiProcess = Process.Start(startInfo);
                Console.WriteLine("Waiting for API server to become ready...");

                var ready = false;
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    if (await client.IsAliveAsync())
                    {
                        ready = true;
                        break;
                    }
                }

                if (!ready)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to start or connect to QuadroSense.Api within 30 seconds.");
                    Console.ResetColor();
                    return 1;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("QuadroSense.Api is online and responding.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error spawning API process: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Connected to running QuadroSense API at {baseUrl}");
            Console.ResetColor();
        }

        // 2. Execute end-to-end hardware tests
        try
        {
            await TestRunner.RunAllTestsAsync(baseUrl);
            TestLogger.Complete(success: true);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[RESULT] Test Suite Completed Successfully.");
            Console.WriteLine($"[LOGS] AI-optimized logs saved to: {TestLogger.SummaryFilePath}");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            TestLogger.Error("TEST_FAILURE", ex.Message, ex);
            TestLogger.Complete(success: false, failureReason: ex.Message);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] Test Suite Failed: {ex.Message}");
            Console.WriteLine($"[LOGS] AI-optimized logs saved to: {TestLogger.SummaryFilePath}");
            if (ex is not TestFailException && ex is not TimeoutException)
            {
                Console.WriteLine(ex.ToString());
            }
            Console.ResetColor();
            return 1;
        }
        finally
        {
            if (apiProcess != null && !apiProcess.HasExited)
            {
                Console.WriteLine("Stopping spawned API server...");
                try { apiProcess.Kill(entireProcessTree: true); } catch { }
            }
        }
    }
}
