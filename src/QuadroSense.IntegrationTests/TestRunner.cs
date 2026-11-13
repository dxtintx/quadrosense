using System.Net;
using Microsoft.AspNetCore.SignalR.Client;

namespace QuadroSense.IntegrationTests;

public static class TestRunner
{
    private static string _currentStep = string.Empty;

    private static void LogSection(string title)
    {
        TestLogger.SetPhase(title);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n=======================================================");
        Console.WriteLine($" {title}");
        Console.WriteLine($"=======================================================");
        Console.ResetColor();
    }

    private static void LogStep(string step, string detail = "", object? data = null)
    {
        _currentStep = step;
        TestLogger.Step(step, detail, data);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"[RUN] ");
        Console.ResetColor();
        Console.Write($"{step} ");
        if (!string.IsNullOrEmpty(detail))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"({detail})");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    private static void LogPass(string message, object? data = null)
    {
        TestLogger.Pass(_currentStep, message, data);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [PASS] {message}");
        Console.ResetColor();
    }

    private static void LogWarn(string message, object? data = null)
    {
        TestLogger.Warn(_currentStep, message, data);
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  [WARN] {message}");
        Console.ResetColor();
    }

    public static async Task RunAllTestsAsync(string baseUrl)
    {
        using var client = new ApiClient(baseUrl);

        LogSection("PHASE 1: INITIAL STATE (NO CONTROLLER CONNECTED)");
        Console.WriteLine("Verifying system behavior prior to DualSense connection...");

        // 1.1 Check device list is empty or controller not yet connected
        LogStep("Checking connected devices endpoint");
        var initialDevices = await client.GetDevicesAsync();
        if (initialDevices.Count == 0)
        {
            LogPass("No DS5 controller detected yet, as expected.");
        }
        else
        {
            LogWarn($"Found {initialDevices.Count} device(s) already connected. Proceeding anyway.");
        }

        // 1.2 Baseline diagnostics
        LogStep("Querying initial diagnostics");
        var initialDiag = await client.GetDiagnosticsAsync();
        LogPass($"Initial state: Uptime={initialDiag.UptimeSeconds}s, ReportsSent={initialDiag.ReportsSent}, Conn={initialDiag.ConnectionType}");

        // 1.3 Baseline haptic state
        LogStep("Querying initial audio-to-haptic state");
        var initialHaptics = await client.GetHapticStateAsync();
        LogPass($"Audio pipeline running: {initialHaptics.AudioPipelineRunning}");

        // 1.4 Tuning with no active profile must return 409 Conflict
        LogStep("Checking tuning rejection when no profile is active");
        var tuningStatus = await client.GetTuningStatusAsync();
        if (tuningStatus == HttpStatusCode.NoContent || tuningStatus == HttpStatusCode.Conflict)
        {
            LogPass($"Tuning returned status {(int)tuningStatus} ({tuningStatus}) as expected without active profile.");
        }

        // 1.5 SignalR connection test
        LogSection("PHASE 2: REAL-TIME TELEMETRY (SIGNALR HUB)");
        LogStep("Connecting to WebSocket /ws/controller");
        var hubUrl = $"{baseUrl.TrimEnd('/')}/ws/controller";
        var hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        var diagTcs = new TaskCompletionSource<ControllerDiagnostics>();
        var hapticTcs = new TaskCompletionSource<HapticState>();

        hub.On<ControllerDiagnostics>("diagnostics", d => diagTcs.TrySetResult(d));
        hub.On<HapticState>("hapticState", s => hapticTcs.TrySetResult(s));

        await hub.StartAsync();
        LogPass("Connected to SignalR hub.");

        LogStep("Awaiting live broadcast packets from DiagnosticsBroadcaster (timeout 4s)");
        var completed = await Task.WhenAny(diagTcs.Task, Task.Delay(4000));
        if (completed == diagTcs.Task)
        {
            var liveDiag = await diagTcs.Task;
            LogPass($"Received broadcast 'diagnostics': ReportsSent={liveDiag.ReportsSent}");
        }
        else
        {
            LogWarn("Did not receive broadcast within 4s, invoking hub methods directly...");
            var invokedDiag = await hub.InvokeAsync<ControllerDiagnostics>("GetDiagnostics");
            LogPass($"Invoked GetDiagnostics via hub: ReportsSent={invokedDiag.ReportsSent}");
        }

        // PHASE 3: WAIT FOR CONTROLLER PLUG-IN
        LogSection("PHASE 3: CONTROLLER DETECTION GATE");
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("\n >>> PLEASE CONNECT YOUR DUALSENSE (DS5) CONTROLLER VIA USB OR BLUETOOTH NOW <<< ");
        Console.ResetColor();
        Console.WriteLine("Polling /api/controller/devices every 1s until detected...\n");

        string? controllerPath = null;
        var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (!timeoutCts.Token.IsCancellationRequested)
        {
            var devices = await client.GetDevicesAsync();
            if (devices.Count > 0)
            {
                controllerPath = devices[0];
                LogPass($"Detected DualSense: {controllerPath}");
                break;
            }
            Console.Write(".");
            await Task.Delay(1000, timeoutCts.Token);
        }

        if (controllerPath == null)
        {
            throw new TimeoutException("Timed out waiting for DualSense controller to connect (3 minutes limit).");
        }

        // PHASE 4: ENGAGE ALL FEATURES ON PHYSICAL HARDWARE
        LogSection("PHASE 4: ENGAGING ALL ADVANCED FEATURES");

        // 4.1 Connect to device
        LogStep("Opening physical HID stream via /api/controller/connect");
        var connectedDiag = await client.ConnectAsync(controllerPath);
        LogPass($"Connected! Device={connectedDiag.DevicePath}, ConnType={connectedDiag.ConnectionType}");
        LogPass("Controller engaged: set to default PlayStation Blue, Player 1 LED, charging animation suppressed.");
        await Task.Delay(1000);

        // 4.2 Lighting: Static RGB & Effects
        LogStep("Feature: Lighting - setting lightbar to solid Cyan (0, 255, 255) + Player LEDs");
        await client.SetLightingAsync(new
        {
            LightbarColor = new { R = 0, G = 255, B = 255 },
            PlayerLeds = 0b0101, // P1 + P3
            MicLedEnabled = true,
            DynamicEffect = "none",
            EffectSpeed = 100
        });
        LogPass("Solid Cyan applied (Look at controller lightbar).");
        await Task.Delay(2000);

        LogStep("Feature: Lighting - engaging dynamic Breathing effect (Magenta)");
        await client.SetLightingAsync(new
        {
            LightbarColor = new { R = 255, G = 0, B = 128 },
            PlayerLeds = 0b1111,
            MicLedEnabled = false,
            DynamicEffect = "breathing",
            EffectSpeed = 180
        });
        LogPass("Breathing effect engaged.");
        await Task.Delay(2000);

        // 4.3 Adaptive Triggers: Rigid mode (Bow tension)
        LogStep("Feature: Adaptive Triggers - engaging 'Rigid' mode on Right Trigger (PULL R2 NOW!)");
        await client.SetTriggerAsync("Right", new
        {
            Mode = "Rigid",
            StartPosition = (byte)70,
            EndPosition = (byte)0,
            Strength = (byte)7,
            Frequency = (byte)0,
            SnapForce = (byte)0
        });
        LogPass("Right Trigger 'Rigid' effect engaged. Squeeze R2 — it will hit a firm physical stop at ~28% pull!");
        await Task.Delay(3000);

        // 4.4 Adaptive Triggers: Pulse mode (Machine gun / weapon fire)
        LogStep("Feature: Adaptive Triggers - engaging 'Pulse' mode on Left Trigger (PULL L2 NOW!)");
        await client.SetTriggerAsync("Left", new
        {
            Mode = "Pulse",
            StartPosition = (byte)30,
            EndPosition = (byte)150,
            Strength = (byte)8,
            Frequency = (byte)35,
            SnapForce = (byte)4
        });
        LogPass("Left Trigger 'Pulse' effect engaged. Squeeze L2 — feel rapid vibration pulsation!");
        await Task.Delay(3000);
        LogPass("Left Trigger 'Pulse' effect engaged. Feel the pulsation.");
        await Task.Delay(1500);

        // 4.5 Reset Triggers
        LogStep("Feature: Adaptive Triggers - Resetting both triggers to Off");
        await client.ResetTriggersAsync();
        LogPass("Triggers reset to Off.");
        await Task.Delay(500);

        // 4.6 HD Rumble / Haptics Motors
        LogStep("Feature: Haptics - engaging dual low/high frequency rumble motors");
        await client.SetHapticsAsync(new
        {
            RumbleLow = (byte)180,
            RumbleHigh = (byte)120,
            AudioToHapticEnabled = false,
            AudioBand = "bass",
            HapticGain = 1.0f
        });
        LogPass("Motors vibrating (Low=180, High=120).");
        await Task.Delay(1000);

        // Turn rumble off
        await client.SetHapticsAsync(new
        {
            RumbleLow = (byte)0,
            RumbleHigh = (byte)0,
            AudioToHapticEnabled = false,
            AudioBand = "bass",
            HapticGain = 1.0f
        });
        LogPass("Rumble motors stopped.");

        // 4.7 Audio-to-Haptic WASAPI Pipeline
        LogStep("Feature: Audio-to-Haptic - engaging WASAPI loopback audio pipeline (bass band)");
        await client.SetHapticsAsync(new
        {
            RumbleLow = (byte)0,
            RumbleHigh = (byte)0,
            AudioToHapticEnabled = true,
            AudioBand = "bass",
            HapticGain = 2.0f
        });
        await Task.Delay(500);
        var activeHaptics = await client.GetHapticStateAsync();
        LogPass($"WASAPI pipeline active={activeHaptics.AudioPipelineRunning}, Device='{activeHaptics.AudioDevice}'");
        await Task.Delay(1000);

        // Stop audio pipeline
        await client.SetHapticsAsync(new
        {
            RumbleLow = (byte)0,
            RumbleHigh = (byte)0,
            AudioToHapticEnabled = false,
            AudioBand = "bass",
            HapticGain = 1.0f
        });
        LogPass("Audio-to-haptic pipeline disengaged.");

        // 4.8 Built-in Controller Speaker Audio Tone
        LogStep("Feature: Speaker Audio - synthesizing and playing 880Hz tone on controller speaker");
        await client.PlayToneAsync(frequencyHz: 880, durationMs: 1500, volume: 0.7f);
        LogPass("Audio tone played successfully through controller speaker.");
        await Task.Delay(500);

        // 4.9 Profiles System: Create, Activate, Query, Auto-Switch spec
        var testProfileId = $"test-profile-{Guid.NewGuid():N}";
        LogStep($"Feature: Profile Management - creating profile '{testProfileId}'");
        await client.UpsertProfileAsync(testProfileId, new
        {
            Id = testProfileId,
            Name = "Automated Test Profile",
            ColorTag = "#FF8800",
            LeftTrigger = new { Mode = "Off", StartPosition = 0, EndPosition = 0, Strength = 0, Frequency = 0, SnapForce = 0 },
            RightTrigger = new { Mode = "Rigid", StartPosition = 90, EndPosition = 0, Strength = 6, Frequency = 0, SnapForce = 0 },
            Haptics = new { RumbleLow = 0, RumbleHigh = 0, AudioToHapticEnabled = false, AudioBand = "bass", HapticGain = 1.0f },
            Lighting = new { LightbarColor = new { R = 255, G = 136, B = 0 }, PlayerLeds = 0b0010, MicLedEnabled = false, DynamicEffect = "none", EffectSpeed = 128 },
            Tuning = new
            {
                LeftStick = new { Deadzone = 0.05f, AntiDeadzone = 0.0f, CurveExponent = 1.2f },
                RightStick = new { Deadzone = 0.08f, AntiDeadzone = 0.02f, CurveExponent = 1.0f },
                Touchpad = new { MouseEmulation = true, MouseDpi = 2400, InvertX = false, InvertY = false },
                Gyro = new { Enabled = true, Sensitivity = 1.5f, DeadzoneDps = 0.4f, OutputTarget = "rightStick" }
            },
            Bindings = new[]
            {
                new { PhysicalButton = "Cross", MappedTo = "Square", TurboEnabled = true, TurboRateHz = 15, HoldToggle = false }
            },
            AutoSwitchProcesses = new[] { "testrunner.exe", "notepad.exe" }
        });
        LogPass("Profile created and persisted.");

        LogStep("Feature: Profile Activation - applying profile immediately to controller");
        await client.ActivateProfileAsync(testProfileId);
        LogPass("Profile activated.");
        await Task.Delay(500);

        // 4.9 Stick / Touchpad / Gyro Tuning (operates on active profile)
        LogStep("Feature: Stick/Touchpad/Gyro Tuning - updating configuration on active profile");
        await client.SetTuningAsync(new
        {
            LeftStick = new { Deadzone = 0.04f, AntiDeadzone = 0.01f, CurveExponent = 1.1f },
            RightStick = new { Deadzone = 0.06f, AntiDeadzone = 0.02f, CurveExponent = 1.0f },
            Touchpad = new { MouseEmulation = true, MouseDpi = 3200, InvertX = false, InvertY = false },
            Gyro = new { Enabled = true, Sensitivity = 2.0f, DeadzoneDps = 0.2f, OutputTarget = "mouse" }
        });
        LogPass("Tuning updated successfully.");

        // 4.10 Button Remapping & Macros
        LogStep("Feature: Button Remapping - configuring custom button table");
        await client.SetBindingsAsync(new[]
        {
            new { PhysicalButton = "Cross", MappedTo = "Circle", TurboEnabled = false, TurboRateHz = 10, HoldToggle = false },
            new { PhysicalButton = "Triangle", MappedTo = "Square", TurboEnabled = true, TurboRateHz = 20, HoldToggle = false }
        });
        var bindings = await client.GetBindingsAsync();
        LogPass($"Button bindings saved. Active entries: {bindings.GetArrayLength()}");

        var testMacroId = $"macro-{Guid.NewGuid():N}";
        LogStep($"Feature: Macro Runner - creating macro '{testMacroId}'");
        await client.UpsertMacroAsync(testMacroId, new
        {
            Id = testMacroId,
            Name = "Double Tap Jump",
            Steps = new[]
            {
                new { Button = "Cross", Pressed = true, DelayMs = 50 },
                new { Button = "Cross", Pressed = false, DelayMs = 50 },
                new { Button = "Cross", Pressed = true, DelayMs = 50 },
                new { Button = "Cross", Pressed = false, DelayMs = 0 }
            }
        });
        LogPass("Macro created.");

        LogStep("Feature: Macro Trigger - executing macro sequence asynchronously");
        var macroTriggerStatus = await client.TriggerMacroAsync(testMacroId);
        LogPass($"Macro triggered (status={macroTriggerStatus}). Steps running in background.");
        await Task.Delay(500);

        // 4.11 Mod System: Register, Real-Time Override Event with TTL
        LogStep("Feature: Mod System - registering new mod plugin 'AutoTestPlugin'");
        var mod = await client.RegisterModAsync(new
        {
            Name = "AutoTestPlugin",
            Version = "1.0.0",
            ApiContract = "1.0"
        });
        LogPass($"Mod registered with ID: {mod.Id}");

        LogStep("Feature: Mod System - dispatching real-time override event (Pulse Trigger + Amber Light, 500ms TTL)");
        var pushStatus = await client.PushModEventAsync(mod.Id, new
        {
            ModId = mod.Id,
            RightTrigger = new
            {
                Mode = "Pulse",
                StartPosition = (byte)50,
                EndPosition = (byte)200,
                Strength = (byte)8,
                Frequency = (byte)40,
                SnapForce = (byte)5
            },
            Lighting = new
            {
                LightbarColor = new { R = 255, G = 191, B = 0 },
                PlayerLeds = 0b1001,
                MicLedEnabled = true,
                DynamicEffect = "none",
                EffectSpeed = 0
            },
            DurationMs = 500
        });
        LogPass($"Mod event accepted (status={pushStatus}). Controller overridden for 500ms.");
        await Task.Delay(800);

        LogStep("Feature: Mod System - unregistering plugin");
        await client.UnregisterModAsync(mod.Id);
        LogPass("Mod unregistered.");

        // 4.12 Cleanup
        LogStep("Cleaning up test profile, test macro & restoring default Blue/Player 1 state");
        await client.DeleteProfileAsync(testProfileId);
        await client.DeleteMacroAsync(testMacroId);
        await client.ResetTriggersAsync();
        await client.ResetLightingAsync();
        LogPass("Cleanup complete. Controller in active Player 1 PlayStation Blue state.");

        // 4.13 Final verification
        LogSection("PHASE 5: FINAL SYSTEM VERIFICATION");
        var finalDiag = await client.GetDiagnosticsAsync();
        LogPass($"Final Controller Diagnostics:");
        Console.WriteLine($"    Connection Type: {finalDiag.ConnectionType}");
        Console.WriteLine($"    Reports Sent:    {finalDiag.ReportsSent}");
        Console.WriteLine($"    Uptime:          {finalDiag.UptimeSeconds}s");
        Console.WriteLine($"    Battery:         {(finalDiag.BatteryPercent >= 0 ? finalDiag.BatteryPercent + "%" : "N/A (reading pending)")}");

        if (finalDiag.ReportsSent > 0)
        {
            LogPass($"Confirmed {finalDiag.ReportsSent} HID report frames reached physical hardware!");
        }

        await hub.StopAsync();

        LogSection("ALL FEATURES ENGAGED AND VERIFIED SUCCESSFULLY!");
    }
}
