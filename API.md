# QuadroSense API — AI-Optimized Reference

> **Format note for LLMs:** This document is structured for machine consumption.
> Every section is self-contained. All schemas are inline. All valid values are enumerated.
> No external links are required to understand any endpoint.

---

## 1. System Model

```
Physical DS5 Controller  ←──HID output report──  ControllerOutputService  (singleton, serialized writes)
        ↑                                                  ↑
        │  HID input report (async, not yet implemented)   │
        │                                     ┌────────────┴────────────┐
   ControllerDeviceManager             AudioHapticService          ModService
   (HidSharp, USB or BT)              (WASAPI loopback DSP)        (TTL override engine)
                                             ↑                          ↑
                                      PUT /api/haptics          POST /api/mods/{id}/event
                                                    \                  /
                                             REST API (HTTP/JSON)
                                             WebSocket  /ws/controller  (SignalR)
```

**Key invariant:** All HID writes are serialized through one `SemaphoreSlim(1,1)`.
Concurrent callers (audio pipeline, mod events, profile activation, trigger endpoint)
never interleave bytes in a report.

**Base URL:** `http://localhost:{PORT}` — default port set in `launchSettings.json`.
No auth. No TLS required (local only). No versioning prefix.

---

## 2. Shared Types

All requests and responses use `application/json`. All field names are **PascalCase**.

### 2.1 AdaptiveTriggerConfig

```jsonc
{
  "Mode":          "Off",    // string — see TriggerMode enum (§2.1.1)
  "StartPosition": 0,        // integer [0, 255] — actuator position units
  "EndPosition":   0,        // integer [0, 255] — only used in ranged/pulse modes
  "Strength":      0,        // integer [0, 8]   — 0 = none, 8 = maximum
  "Frequency":     0,        // integer [1, 255]  — Hz, only used in Pulse* modes
  "SnapForce":     0         // integer [0, 8]   — snap-back resistance
}
```

#### 2.1.1 TriggerMode Enum

| Value | Hex | Behavior | Fields used |
|---|---|---|---|
| `"Off"` | 0x00 | No effect | — |
| `"Rigid"` | 0x01 | Firm stop at StartPosition | StartPosition, Strength |
| `"Pulse"` | 0x02 | Vibrate between Start and End | StartPosition, EndPosition, Strength, Frequency, SnapForce |
| `"Rigid_A"` | 0x21 | Rigid, section A only | StartPosition, Strength |
| `"Rigid_B"` | 0x22 | Rigid, section B only | StartPosition, Strength |
| `"Rigid_AB"` | 0x23 | Rigid, both sections | StartPosition, Strength |
| `"Pulse_A"` | 0x25 | Pulse, section A only | StartPosition, EndPosition, Strength, Frequency, SnapForce |
| `"Pulse_B"` | 0x26 | Pulse, section B only | StartPosition, EndPosition, Strength, Frequency, SnapForce |
| `"Pulse_AB"` | 0x27 | Pulse, both sections | StartPosition, EndPosition, Strength, Frequency, SnapForce |
| `"Calibration"` | 0xFC | Hardware calibration mode | — (all set to 0xFF) |

**Rule:** Fields not listed for a mode are ignored but must be present (can be 0).

---

### 2.2 HapticConfig

```jsonc
{
  "RumbleLow":           0,       // integer [0, 255] — left motor (low-freq / body)
  "RumbleHigh":          0,       // integer [0, 255] — right motor (high-freq / detail)
  "AudioToHapticEnabled": false,  // bool — starts/stops WASAPI loopback capture pipeline
  "AudioBand":           "bass",  // string — "bass" | "mid" | "treble" | "all"
  "HapticGain":          1.0      // float [0.0, 4.0] — multiplier on audio RMS → rumble
}
```

**Constraint:** When `AudioToHapticEnabled=true`, `RumbleLow`/`RumbleHigh` are overwritten
by the pipeline on each audio callback (~every 10ms). Set them to 0 if audio-driven.

**AudioBand semantics:**
| Value | Approx frequency range | Use case |
|---|---|---|
| `"bass"` | ~0–300 Hz | Explosions, engines, low impacts |
| `"mid"` | ~300–3000 Hz | Footsteps, voices, mid textures |
| `"treble"` | ~3000+ Hz | UI sounds, sparks, high-pitched effects |
| `"all"` | Full spectrum | Generic / broadband |

---

### 2.3 HapticState (read-only)

```jsonc
{
  "AudioPipelineRunning": false,     // bool
  "AudioDevice":          "Speakers (Realtek...)",  // string | null — null if pipeline stopped
  "CurrentRms":           0.0,       // float [0.0, 1.0] — current audio RMS sample
  "LastReportLeft":       0,         // byte — last rumble byte sent to left motor
  "LastReportRight":      0          // byte — last rumble byte sent to right motor
}
```

---

### 2.4 LightingConfig

```jsonc
{
  "LightbarColor": { "R": 0, "G": 0, "B": 255 },  // RgbColor — default blue
  "PlayerLeds":    1,        // byte bitmask — bits 0–3 map to P1–P4 LEDs
  "MicLedEnabled": false,    // bool — microphone mute indicator LED
  "DynamicEffect": "none",   // string — see effect table below
  "EffectSpeed":   128       // byte [0, 255] — animation speed
}
```

**PlayerLeds bitmask:**
| Value | Active LEDs |
|---|---|
| `0b0001` = 1 | P1 only |
| `0b0011` = 3 | P1 + P2 |
| `0b0111` = 7 | P1 + P2 + P3 |
| `0b1111` = 15 | All four |

**DynamicEffect values:**
| Value | Behavior | Overrides LightbarColor? |
|---|---|---|
| `"none"` | Static LightbarColor | No |
| `"battery"` | Green→Red based on charge level | Yes |
| `"audio_reactive"` | Pulses with audio RMS (requires AudioHapticService running) | Yes |
| `"breathing"` | LightbarColor fades in/out at EffectSpeed | No |
| `"rainbow"` | Cycles hue, ignores LightbarColor | Yes |

---

### 2.5 TuningConfig

```jsonc
{
  "LeftStick":  { ... },  // StickConfig
  "RightStick": { ... },  // StickConfig
  "Touchpad":   { ... },  // TouchpadConfig
  "Gyro":       { ... }   // GyroConfig
}
```

#### StickConfig
```jsonc
{
  "Deadzone":      0.08,   // float [0.0, 1.0] — circular inner deadzone radius (normalized)
  "AntiDeadzone":  0.0,    // float [0.0, 1.0] — outer edge minimum output (prevents mushiness)
  "CurveExponent": 1.0     // float — 1.0 = linear, >1 = sluggish near center, <1 = aggressive
}
```

#### TouchpadConfig
```jsonc
{
  "MouseEmulation": false,  // bool — treat touchpad as relative mouse input
  "MouseDpi":       1600,   // integer [100, 32000]
  "InvertX":        false,
  "InvertY":        false
}
```

#### GyroConfig
```jsonc
{
  "Enabled":      false,
  "Sensitivity":  1.0,            // float [0.1, 5.0]
  "DeadzoneDps":  0.5,            // float — degrees/sec below which gyro is zeroed
  "OutputTarget": "rightStick"    // string — "rightStick" | "mouse" | "none"
}
```

---

### 2.6 Profile

```jsonc
{
  "Id":           "uuid-string",    // string — set by server on first create; route ID wins on upsert
  "Name":         "Default",        // string
  "ColorTag":     "#0099FF",        // string — CSS hex color for grouping
  "IsActive":     false,            // bool — read-only in GET responses
  "LeftTrigger":  { ... },          // AdaptiveTriggerConfig
  "RightTrigger": { ... },          // AdaptiveTriggerConfig
  "Haptics":      { ... },          // HapticConfig
  "Lighting":     { ... },          // LightingConfig
  "Tuning":       { ... },          // TuningConfig
  "Bindings":     [ ... ],          // ButtonBinding[]
  "AutoSwitchProcesses": ["eldenring.exe"]  // string[] — process names for auto-activation
}
```

**Persistence:** Profiles are saved as `%AppData%\QuadroSense\profiles\{id}.json`.
Deleted profiles remove the file. Backup = copy the folder.

---

### 2.7 ButtonBinding

```jsonc
{
  "PhysicalButton": "Cross",    // string — physical button name (see §2.7.1)
  "MappedTo":       "Square",   // string — virtual button name OR macro id
  "TurboEnabled":   false,      // bool — rapid-fire mode
  "TurboRateHz":    10,         // integer — fires per second in turbo mode
  "HoldToggle":     false       // bool — press once to hold, press again to release
}
```

#### 2.7.1 Valid PhysicalButton names
`Cross` `Circle` `Square` `Triangle` `L1` `R1` `L2` `R2` `L3` `R3`
`DPadUp` `DPadDown` `DPadLeft` `DPadRight`
`Options` `Create` `PS` `Mute` `TouchPadClick`

---

### 2.8 Macro

```jsonc
{
  "Id":    "uuid-string",
  "Name":  "Combo A",
  "Steps": [
    { "Button": "Cross",  "Pressed": true,  "DelayMs": 0  },
    { "Button": "Cross",  "Pressed": false, "DelayMs": 50 },
    { "Button": "Square", "Pressed": true,  "DelayMs": 0  },
    { "Button": "Square", "Pressed": false, "DelayMs": 100 }
  ]
}
```

**MacroStep fields:**
| Field | Type | Description |
|---|---|---|
| `Button` | string | Any PhysicalButton name (§2.7.1) |
| `Pressed` | bool | `true` = press down, `false` = release |
| `DelayMs` | integer ≥ 0 | Milliseconds to wait **after** this step |

**Execution:** Fire-and-forget. `POST /api/remapping/macros/{id}/trigger` returns `202 Accepted`
immediately. Steps play sequentially on a background thread.

---

### 2.9 ModRegistration

```jsonc
{
  "Id":          "uuid-string",  // assigned by server; ignore if sending, present in response
  "Name":        "MyGameMod",
  "Version":     "1.0.0",
  "ApiContract": "1.0"           // must be "1.0" for current server version
}
```

---

### 2.10 ModEvent

```jsonc
{
  "ModId":        "uuid-string",        // set from route param by server; can be omitted in body
  "LeftTrigger":  null,                 // AdaptiveTriggerConfig | null — null = don't override
  "RightTrigger": null,                 // AdaptiveTriggerConfig | null
  "Haptics":      null,                 // HapticConfig | null
  "Lighting":     null,                 // LightingConfig | null
  "DurationMs":   0                     // integer — 0 = one-shot, -1 = persistent, >0 = TTL
}
```

**DurationMs semantics:**
| Value | Behavior |
|---|---|
| `0` | Applied for exactly one HID report frame, then reverted |
| `-1` | Persists until the same mod sends another event |
| `N > 0` | Persists for N milliseconds, then auto-expires (watchdog sweep every 50ms) |

---

### 2.11 ControllerDiagnostics (read-only)

```jsonc
{
  "DevicePath":      "\\\\?\\HID#VID_054C&PID_0CE6...",
  "ConnectionType":  "USB",         // "USB" | "Bluetooth"
  "BatteryPercent":  -1,            // integer [0, 100] | -1 (unknown — input report not yet read)
  "IsCharging":      false,
  "BluetoothRssi":   null,          // integer (dBm) | null (always null for USB)
  "FirmwareVersion": "",            // string — populated from DS5 feature report (not yet read)
  "HardwareVersion": "",
  "UptimeSeconds":   42,            // integer — seconds since TryConnect()
  "ReportsSent":     1337           // integer — total HID output reports written this session
}
```

> [!NOTE]
> `BatteryPercent` and `BluetoothRssi` return sentinel values (`-1` / `null`) until the
> async input report reader is implemented. Use the SignalR hub for live telemetry when available.

---

### 2.12 ProfileSummary (list view only)

```jsonc
{
  "Id":       "uuid-string",
  "Name":     "Elden Ring",
  "ColorTag": "#FF5733",
  "IsActive": true
}
```

---

## 3. Endpoints

### 3.1 Controller Device

#### `GET /api/controller/devices`
Returns all connected DualSense (VID 0x054C, PID 0x0CE6) device paths.

**Response 200:**
```json
["\\\\?\\HID#VID_054C&PID_0CE6&...\\0000", "\\\\?\\HID#VID_054C&PID_0CE6&...\\0001"]
```
Empty array `[]` = no DS5 detected.

---

#### `POST /api/controller/connect`
Opens HID stream to a DS5. Must be called before any output endpoint works.

**Query param:** `devicePath` (optional string) — path from `/devices`. Omit for auto-detect (first found).

**Response 200** — `ControllerDiagnostics` — success, stream open.
**Response 404** — no DS5 found at the given path or at all.

**Side effect:** Auto-detect runs at app startup in `Program.cs`. Only call manually if
you want to target a specific device or reconnect after disconnect.

---

#### `GET /api/controller/diagnostics`
Returns live session stats. Does not require a connected controller (returns empty strings/zeros if disconnected).

**Response 200** — `ControllerDiagnostics`

---

#### `POST /api/controller/sound/tone`
Synthesizes and renders an audible sine wave tone. On DualSense quadraphonic endpoints, audio is explicitly mapped to channels 3 and 4 (indices 2 & 3) for the built-in controller speaker, and channels 1 and 2 (indices 0 & 1) for the 3.5mm headphone jack.

**Body (optional):**
```jsonc
{
  "FrequencyHz": 880,   // integer [50, 10000] - pitch in Hz (default 880)
  "DurationMs":  1500,  // integer [100, 10000] - duration in ms (default 1500)
  "Volume":      0.7    // float [0.05, 1.0] - volume gain (default 0.6)
}
```

**Response 200:**
```json
{
  "Success": true,
  "Message": "Played 880Hz tone on controller speaker"
}
```

---

### 3.2 Adaptive Triggers

#### `GET /api/controller/configuration`

Returns the current base output configuration used by the desktop editors.
The response contains `LeftTrigger`, `RightTrigger`, `Haptics`, and `Lighting`
with the shared schemas above. It is available without a connected controller.
Temporary mod overrides and audio samples do not replace this base configuration.

---

#### `PUT /api/triggers/{side}`
Applies a trigger effect immediately. The HID output report is written synchronously before the response is returned.

**Path param:** `side` = `Left` | `Right`
**Body:** `AdaptiveTriggerConfig`
**Response 204** — effect applied.
**Response 400** — invalid body or unknown side.

**Latency:** ≤ 2ms on USB (HID write round-trip). Bluetooth adds ~1–3ms CRC + BT transmission.

---

#### `DELETE /api/triggers`
Resets both triggers to `Mode: "Off"`. All other fields zeroed.

**Response 204**

---

### 3.3 Haptics

#### `PUT /api/haptics`
Configures rumble and optionally starts the audio-to-haptic pipeline.

**Body:** `HapticConfig`
**Response 204**

**Side effects:**
- Sets rumble motor levels immediately via HID report.
- If `AudioToHapticEnabled` changed to `true`: starts WASAPI loopback on the system default
  render device. `RumbleLow`/`RumbleHigh` values in this config are overwritten by the pipeline.
- If `AudioToHapticEnabled` changed to `false`: stops the WASAPI capture and pipeline.
- Both `ControllerOutputService` and `AudioHapticService` are updated atomically.

---

#### `GET /api/haptics/state`
Returns the live state of the audio pipeline.

**Response 200** — `HapticState`

---

### 3.4 Lighting

#### `PUT /api/lighting`
Applies lightbar, player LED, and dynamic effect settings immediately.

**Body:** `LightingConfig`
**Response 204**

**Note:** `"audio_reactive"` effect reads `AudioHapticService.State.CurrentRms` on each
lighting update cycle. The audio pipeline (`PUT /api/haptics` with `AudioToHapticEnabled=true`)
must be running for reactive lighting to animate. If the pipeline is off, the lightbar stays static.

---

#### `POST /api/lighting/player/{playerNumber}`
Sets active player indicator count (1–4) and sets the lightbar to default PlayStation Blue. Cancels hardware charging animation.

**Path param:** `playerNumber` = integer [1, 4]
- 1: Center LED (`0b00100`)
- 2: 2 LEDs (`0b01010`)
- 3: 3 LEDs (`0b10101`)
- 4: 4 LEDs (`0b11011`)

**Response 204**

---

#### `POST /api/lighting/reset`
Resets controller lighting to default PlayStation Blue, Player 1 indicator LED, mic LED off. Cancels any orange charging animation.

**Response 204**

---

### 3.5 Profiles

#### `GET /api/profiles`
**Response 200** — `ProfileSummary[]` — all profiles, ordered by file system enumeration.

---

#### `GET /api/profiles/{id}`
**Response 200** — full `Profile`
**Response 404** — no profile with that ID

---

#### `PUT /api/profiles/{id}`
Create or update. The route `{id}` overrides `Id` in the body.

**Body:** `Profile` (all fields; omitted fields use defaults)
**Response 200** — the saved `Profile` (with `Id` set to route param)

**Persistence:** Written synchronously to `%AppData%\QuadroSense\profiles\{id}.json`.

---

#### `DELETE /api/profiles/{id}`
**Response 204** — deleted (file removed)
**Response 404** — not found

---

#### `POST /api/profiles/{id}/activate`
Applies all settings in the profile to the controller immediately: triggers, haptics, lighting, bindings.
Sets this profile as the active one for tuning mutations.

**Response 204** — activated
**Response 404** — profile not found

**Idempotent:** Activating an already-active profile re-applies all its settings.

---

#### `GET /api/profiles/active`
**Response 200** — full `Profile` — the currently active profile
**Response 204** — no profile is active

> [!IMPORTANT]
> `GET /api/profiles/active` is a **static route** and must be listed before `GET /api/profiles/{id}`
> in routing priority. ASP.NET Core resolves it correctly by route specificity.

---

### 3.6 Button Remapping

#### `GET /api/remapping/bindings`
**Response 200** — `ButtonBinding[]`

---

#### `PUT /api/remapping/bindings`
Replaces the entire binding table. Send an empty array `[]` to clear all remaps.

**Body:** `ButtonBinding[]`
**Response 204**

---

#### `GET /api/remapping/macros`
**Response 200** — `Macro[]`

---

#### `GET /api/remapping/macros/{id}`
**Response 200** — `Macro`
**Response 404**

---

#### `PUT /api/remapping/macros/{id}`
Create or update. Route `{id}` overrides `Id` in body.

**Body:** `Macro`
**Response 200** — saved `Macro`

---

#### `DELETE /api/remapping/macros/{id}`
**Response 204** | **404**

---

#### `POST /api/remapping/macros/{id}/trigger`
Starts macro playback asynchronously. Does **not** block the HTTP response.

**Response 202 Accepted** — playback started
**Response 404** — macro not found

**Concurrency:** Multiple macros can run simultaneously. Steps within a single macro are sequential.

---

### 3.7 Stick / Touchpad / Gyro Tuning

#### `GET /api/tuning`
**Response 200** — `TuningConfig` from the active profile
**Response 204** — no active profile

---

#### `PUT /api/tuning`
Updates the tuning sub-document of the active profile and persists it.

**Body:** `TuningConfig`
**Response 200** — the applied `TuningConfig`
**Response 409 Conflict** — no profile is active (`"No active profile. Activate a profile first."`)

**Prerequisite:** Call `POST /api/profiles/{id}/activate` first.

---

### 3.8 Mod System

#### `GET /api/mods`
**Response 200** — `ModRegistration[]`

---

#### `POST /api/mods/register`
Registers a mod plugin. Returns the registration with server-assigned `Id`.

**Body:** `ModRegistration` (`Id` optional — server assigns one if missing)
**Response 201** — `ModRegistration` with `Id` populated

---

#### `DELETE /api/mods/{modId}`
Unregisters the mod and cancels any active override from it.

**Response 204** | **404**

---

#### `POST /api/mods/{modId}/event`
Push a real-time override. All override fields are optional — omit (or set to `null`) any
subsystem you do not want to control. The HID write happens before `202` is returned.

**Body:** `ModEvent` (`ModId` in body is ignored — route param is authoritative)
**Response 202 Accepted** — HID write dispatched
**Response 404** — mod not registered

**Performance contract:** ≤ 5ms from HTTP receive to HID report byte reaching the controller
(USB). Designed for per-frame game event calls at 60–120 Hz.

---

## 4. WebSocket — SignalR Hub

**URL:** `ws://localhost:{PORT}/ws/controller`
**Protocol:** SignalR (JSON over WebSocket)

### 4.1 Server → Client pushes (every 1 second)

| Message name | Payload type | Description |
|---|---|---|
| `"diagnostics"` | `ControllerDiagnostics` | Battery, RSSI, report counters |
| `"hapticState"` | `HapticState` | Audio pipeline RMS, motor bytes |

### 4.2 Client → Server invocations

| Method name | Returns | Description |
|---|---|---|
| `GetDiagnostics()` | `ControllerDiagnostics` | Immediate snapshot (no wait for next push) |
| `GetHapticState()` | `HapticState` | Immediate snapshot |

### 4.3 JavaScript client example

```js
import * as signalR from "@microsoft/signalr";

const conn = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/ws/controller")
  .build();

conn.on("diagnostics", (d) => console.log("Battery:", d.BatteryPercent));
conn.on("hapticState", (s) => console.log("RMS:", s.CurrentRms));

await conn.start();
const diag = await conn.invoke("GetDiagnostics");
```

---

## 5. Error Catalogue

| Status | When it occurs | Body |
|---|---|---|
| 204 No Content | Success, no body (triggers, haptics, lighting, delete, activate) | — |
| 400 Bad Request | Invalid JSON body or enum value | ASP.NET Core ProblemDetails |
| 404 Not Found | Resource ID not found, or no DS5 detected on connect | String or ProblemDetails |
| 409 Conflict | `PUT /api/tuning` with no active profile | `"No active profile. Activate a profile first."` |
| 202 Accepted | Async operations (macro trigger, mod event) | — |

---

## 6. Task-Oriented Decision Trees

### 6.1 "I want to set a bow-draw tension effect on the right trigger"

```
Use Mode = "Rigid"
  StartPosition = how far in before resistance starts (e.g. 90 ≈ 35% pull)
  Strength = resistance force (try 6–8 for strong bow tension)
  EndPosition, Frequency, SnapForce = 0 (unused for Rigid)

PUT /api/triggers/Right
Body: { "Mode": "Rigid", "StartPosition": 90, "Strength": 7, "EndPosition": 0, "Frequency": 0, "SnapForce": 0 }
```

### 6.2 "I want a machine-gun vibration effect on both triggers"

```
Use Mode = "Pulse"
  StartPosition = where vibration begins (e.g. 20)
  EndPosition   = where it ends (e.g. 100)
  Frequency     = rounds per second illusion (try 20–40)
  Strength      = intensity (5–8)
  SnapForce     = recoil snap-back (3–5)

PUT /api/triggers/Left  with the same config
PUT /api/triggers/Right with the same config
```

### 6.3 "I want the controller to rumble with game audio"

```
1. PUT /api/haptics
   Body: { "AudioToHapticEnabled": true, "AudioBand": "bass", "HapticGain": 1.5,
           "RumbleLow": 0, "RumbleHigh": 0 }

2. GET /api/haptics/state   →  verify AudioPipelineRunning = true

3. To stop: PUT /api/haptics  Body: { "AudioToHapticEnabled": false, ... }
```

### 6.4 "I want a game mod to flash red on player damage"

```
1. POST /api/mods/register
   Body: { "Name": "DamageFlash", "Version": "1.0.0", "ApiContract": "1.0" }
   → save returned Id

2. On damage event in game:
   POST /api/mods/{id}/event
   Body: {
     "Lighting": { "LightbarColor": { "R": 255, "G": 0, "B": 0 },
                   "DynamicEffect": "none", "PlayerLeds": 1,
                   "MicLedEnabled": false, "EffectSpeed": 0 },
     "DurationMs": 300
   }
   → lightbar turns red for 300ms, then auto-reverts

3. On game exit: DELETE /api/mods/{id}
```

### 6.5 "I want auto-switching profiles per game"

```
1. Create the profile:
   PUT /api/profiles/{uuid}
   Body: { "Name": "Elden Ring", "AutoSwitchProcesses": ["eldenring.exe"],
           "RightTrigger": { "Mode": "Rigid", "StartPosition": 80, "Strength": 7, ... },
           ... }

2. That's it. ProfileService polls running processes every 2 seconds.
   When eldenring.exe is detected, the profile is activated automatically.

3. To verify: GET /api/profiles/active
```

### 6.6 "I want a combo macro on a single button"

```
1. Create macro:
   PUT /api/remapping/macros/{uuid}
   Body: {
     "Name": "RB Combo",
     "Steps": [
       { "Button": "Cross",    "Pressed": true,  "DelayMs": 0  },
       { "Button": "Cross",    "Pressed": false, "DelayMs": 60 },
       { "Button": "Triangle", "Pressed": true,  "DelayMs": 0  },
       { "Button": "Triangle", "Pressed": false, "DelayMs": 60 }
     ]
   }

2. Bind a button to it:
   PUT /api/remapping/bindings
   Body: [{ "PhysicalButton": "R1", "MappedTo": "{uuid}", "TurboEnabled": false,
            "TurboRateHz": 10, "HoldToggle": false }]

3. Fire it manually (testing):
   POST /api/remapping/macros/{uuid}/trigger
```

---

## 7. HID Report Internals (for mod developers)

The API abstracts this. This section is for understanding latency and limits.

### DS5 USB Output Report (48 bytes, Report ID 0x02)

| Byte offset | Content |
|---|---|
| 0 | Report ID = `0x02` |
| 1 | Feature flags: rumble enable (`0x01`), extended rumble (`0x02`) |
| 2 | Trigger + lightbar enable flags |
| 3 | RumbleHigh (right, high-freq motor) |
| 4 | RumbleLow (left, low-freq motor) |
| 5–8 | Audio volume (headphone, speaker, mic, aux) |
| 9 | Mute LED byte |
| 11–21 | Right trigger effect (11 bytes) |
| 22–32 | Left trigger effect (11 bytes) |
| 39 | Player LED bitmask |
| 40 | Lightbar R |
| 41 | Lightbar G |
| 42 | Lightbar B |

### DS5 Bluetooth Output Report (78 bytes, Report ID 0x31)

Wraps the USB payload starting at byte 2. Bytes 74–77 = CRC32 with Sony seed `0xA1`.
Detection: auto, based on `HidDevice.GetMaxOutputReportLength() > 64`.

### Maximum safe output rate

| Connection | Safe rate | Notes |
|---|---|---|
| USB | 250 Hz (4ms) | USB HID polling interval |
| Bluetooth | 60 Hz (16ms) | BT connection interval |

Exceeding these rates causes reports to queue in the kernel HID driver buffer.
The serialization lock in `ControllerOutputService` prevents corruption but does not rate-limit.

---

## 8. Startup Checklist (for LLM-driven automation)

```
1. GET  /api/controller/devices          → verify DS5 is listed
2. POST /api/controller/connect          → open HID stream (auto-detect)
3. GET  /api/controller/diagnostics      → verify ReportsSent increments (or ConnectionType is set)
4. GET  /api/profiles                    → list existing profiles
5. POST /api/profiles/{id}/activate      → activate a profile (required before PUT /api/tuning)
6. PUT  /api/triggers/Left               → test trigger effect
7. PUT  /api/triggers/Right              → test trigger effect
8. DELETE /api/triggers                  → reset to Off
9. Subscribe ws://host/ws/controller     → start receiving diagnostics push
```
