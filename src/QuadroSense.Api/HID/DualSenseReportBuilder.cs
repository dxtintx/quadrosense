using QuadroSense.Api.Models;

namespace QuadroSense.Api.HID;

/// <summary>
/// Builds byte-exact DualSense USB (Report 0x02, 64-byte payload) and
/// Bluetooth (Report 0x31, 78-byte payload with 0xA2 header seed and CRC32) output reports.
/// Follows Sony DS5 specification and Linux hid-playstation kernel driver.
/// </summary>
public static class DualSenseReportBuilder
{
    public const int UsbReportLength = 48; // Report ID (0x02) + 47 bytes payload
    public const int BtReportLength  = 78;

    private const byte UsbReportId = 0x02;
    private const byte BtReportId  = 0x31;

    // Valid Flag 0 (Byte 1)
    private const byte FLAG0_RUMBLE_COMPAT      = 0x01; // Enable classic rumble emulation (weak/strong)
    private const byte FLAG0_RUMBLE_HAPTIC      = 0x02; // Enable haptic rumble
    private const byte FLAG0_TRIGGER_RIGHT      = 0x04; // Enable Right Adaptive Trigger update
    private const byte FLAG0_TRIGGER_LEFT       = 0x08; // Enable Left Adaptive Trigger update
    private const byte FLAG0_HEADPHONE_VOL      = 0x10;
    private const byte FLAG0_SPEAKER_VOL        = 0x20;
    private const byte FLAG0_MIC_VOL            = 0x40;
    private const byte FLAG0_AUDIO_CONTROL      = 0x80;

    // Valid Flag 1 (Byte 2)
    private const byte FLAG1_MIC_MUTE_LED       = 0x01;
    private const byte FLAG1_POWER_SAVE         = 0x02;
    private const byte FLAG1_LIGHTBAR_COLOR     = 0x04; // Enable Lightbar RGB update
    private const byte FLAG1_PLAYER_LEDS_FADE   = 0x08;
    private const byte FLAG1_PLAYER_LEDS        = 0x10; // Enable Player Indicator LEDs update
    private const byte FLAG1_MOTOR_POWER        = 0x20;
    private const byte FLAG1_LIGHTBAR_BRIGHT    = 0x40;

    // Sequence counter for Bluetooth packets to prevent duplicate drop by firmware
    private static int _btSequence = 0;

    /// <summary>
    /// Builds a full 64-byte USB output report for the DualSense.
    /// </summary>
    public static byte[] Build(
        AdaptiveTriggerConfig leftTrigger,
        AdaptiveTriggerConfig rightTrigger,
        HapticConfig haptics,
        LightingConfig lighting)
    {
        var report = new byte[UsbReportLength];
        report[0] = UsbReportId;

        // Byte 1: Valid Flag 0 - Rumble, Adaptive Triggers & Audio Volumes
        report[1] = (byte)(FLAG0_RUMBLE_COMPAT | FLAG0_RUMBLE_HAPTIC | FLAG0_TRIGGER_RIGHT | FLAG0_TRIGGER_LEFT |
                           FLAG0_HEADPHONE_VOL | FLAG0_SPEAKER_VOL | FLAG0_MIC_VOL);

        // Byte 2: Valid Flag 1 - Lightbar & Player LEDs & Mic Mute LED
        report[2] = (byte)(FLAG1_LIGHTBAR_COLOR | FLAG1_PLAYER_LEDS | FLAG1_MIC_MUTE_LED);

        // Bytes 3-4: Rumble motors
        report[3] = haptics.RumbleHigh; // Right motor (weak / high-frequency)
        report[4] = haptics.RumbleLow;  // Left motor (strong / low-frequency)

        // Bytes 5-7: Volume controls (0x7F max headphone, 0xFF max speaker)
        report[5] = 0x7F; // Headphone volume
        report[6] = 0xFF; // Speaker volume
        report[7] = 0x40; // Mic volume

        // Byte 9: Mic mute LED (0 = off, 1 = solid on, 2 = pulse)
        report[9] = lighting.MicLedEnabled ? (byte)0x01 : (byte)0x00;

        // ── Bytes 11-21: Right Adaptive Trigger Block (11 bytes) ─────
        WriteTrigger(report, offset: 11, rightTrigger);

        // ── Bytes 22-32: Left Adaptive Trigger Block (11 bytes) ──────
        WriteTrigger(report, offset: 22, leftTrigger);

        // Byte 39: Valid Flag 2 (0x02 = Lightbar color valid flag)
        report[39] = 0x02;

        // Byte 42: Lightbar setup flag (0x02 = Set lightbar color, not reset)
        report[42] = 0x02;

        // Byte 43: LED brightness (0x00 = High, 0x01 = Medium, 0x02 = Low)
        report[43] = 0x00;

        // Byte 44: Player LEDs bitmask (bits 0-4 map to 5 player LEDs)
        report[44] = lighting.PlayerLeds;

        // Bytes 45-47: Lightbar Red, Green, Blue
        report[45] = lighting.LightbarColor.R;
        report[46] = lighting.LightbarColor.G;
        report[47] = lighting.LightbarColor.B;

        return report;
    }

    private static void WriteTrigger(byte[] report, int offset, AdaptiveTriggerConfig cfg)
    {
        report[offset] = (byte)cfg.Mode;

        switch (cfg.Mode)
        {
            case TriggerMode.Rigid:
            case TriggerMode.Rigid_A:
            case TriggerMode.Rigid_B:
            case TriggerMode.Rigid_AB:
                report[offset + 1] = cfg.StartPosition;
                report[offset + 2] = cfg.Strength;
                break;

            case TriggerMode.Pulse:
            case TriggerMode.Pulse_A:
            case TriggerMode.Pulse_B:
            case TriggerMode.Pulse_AB:
                report[offset + 1] = cfg.StartPosition;
                report[offset + 2] = cfg.EndPosition;
                report[offset + 3] = (byte)((cfg.Strength & 0x07) << 4 | (cfg.SnapForce & 0x07));
                report[offset + 4] = cfg.Frequency;
                break;

            case TriggerMode.Calibration:
                report[offset + 1] = 0xFF;
                report[offset + 2] = 0xFF;
                report[offset + 3] = 0xFF;
                break;

            default: // Off
                break;
        }
    }

    /// <summary>
    /// Builds a 78-byte Bluetooth output report (Report ID 0x31) with CRC32.
    /// Sony DS5 expects virtual byte 0xA2 prepended during CRC calculation.
    /// </summary>
    public static byte[] BuildBluetooth(
        AdaptiveTriggerConfig leftTrigger,
        AdaptiveTriggerConfig rightTrigger,
        HapticConfig haptics,
        LightingConfig lighting)
    {
        var bt = new byte[BtReportLength];
        bt[0] = BtReportId; // 0x31

        // Byte 1: Upper nibble = sequence counter, lower nibble = tag (0x02)
        int seq = Interlocked.Increment(ref _btSequence) & 0x0F;
        bt[1] = (byte)((seq << 4) | 0x02);

        // Common report body (bytes 1-47 of USB report map directly to bytes 2-48 of BT report)
        var usb = Build(leftTrigger, rightTrigger, haptics, lighting);
        Buffer.BlockCopy(usb, 1, bt, 2, 47);

        // CRC32 calculation:
        // Sony Bluetooth output CRC is calculated over virtual seed 0xA2 + bt[0..73] (74 bytes)
        uint crc = CalculateSonyBluetoothCrc(bt, 74);
        bt[74] = (byte)(crc & 0xFF);
        bt[75] = (byte)((crc >> 8) & 0xFF);
        bt[76] = (byte)((crc >> 16) & 0xFF);
        bt[77] = (byte)((crc >> 24) & 0xFF);

        return bt;
    }

    private static uint CalculateSonyBluetoothCrc(byte[] buffer, int length)
    {
        // 0xA2 is Sony Bluetooth Output Report header byte (0x50 | 0x02 = DATA OUTPUT)
        uint crc = 0xFFFFFFFF;

        // Feed virtual byte 0xA2 first
        crc = UpdateCrc32(crc, 0xA2);

        // Feed report bytes 0 through length-1
        for (int i = 0; i < length; i++)
        {
            crc = UpdateCrc32(crc, buffer[i]);
        }

        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte b)
    {
        crc ^= b;
        for (int j = 0; j < 8; j++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc;
    }
}
