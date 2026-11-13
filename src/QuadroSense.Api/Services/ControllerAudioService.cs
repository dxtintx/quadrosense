using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace QuadroSense.Api.Services;

public sealed record ToneRequest(int FrequencyHz = 880, int DurationMs = 1500, float Volume = 0.6f);

public sealed class ControllerAudioService
{
    private readonly ILogger<ControllerAudioService> _log;

    public ControllerAudioService(ILogger<ControllerAudioService> log)
    {
        _log = log;
    }

    /// <summary>
    /// Finds the DualSense audio playback device if connected, or falls back to the system default audio device.
    /// </summary>
    public (MMDevice? Device, bool IsDualSense) GetAudioDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            var dsDevice = devices.FirstOrDefault(d =>
                d.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
                d.FriendlyName.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase));

            if (dsDevice is not null)
                return (dsDevice, true);

            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return (defaultDevice, false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to enumerate audio devices");
            return (null, false);
        }
    }

    /// <summary>
    /// Synthesizes and plays a clean audible tone directly through the DualSense speaker (or fallback device).
    /// Uses the exact device MixFormat (IEEE Float 4-channel or 2-channel) expected by WASAPI Shared Mode.
    /// </summary>
    public async Task<bool> PlayToneAsync(ToneRequest request, CancellationToken ct = default)
    {
        var (device, isDualSense) = GetAudioDevice();
        if (device is null)
        {
            _log.LogWarning("No audio render device available to play tone.");
            return false;
        }

        _log.LogInformation("Playing {Freq}Hz tone on {Device} (IsDualSense={IsDualSense})",
            request.FrequencyHz, device.FriendlyName, isDualSense);

        return await Task.Run(() =>
        {
            try
            {
                var mixFormat = device.AudioClient.MixFormat;
                using var wasapi = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);

                var durationMs = Math.Clamp(request.DurationMs, 100, 10000);
                var freqHz = Math.Clamp(request.FrequencyHz, 50, 10000);
                var gain = Math.Clamp(request.Volume, 0.05f, 1.0f);

                var waveProvider = new MixFormatToneProvider(mixFormat, freqHz, durationMs, gain);
                wasapi.Init(waveProvider);
                wasapi.Play();

                var duration = TimeSpan.FromMilliseconds(durationMs);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed < duration && !ct.IsCancellationRequested && wasapi.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }

                wasapi.Stop();
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to play audio tone");
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates audio samples matching the exact WaveFormat required by the audio endpoint's MixFormat.
    /// Supports both 32-bit IEEE Float (DualSense standard) and 16-bit integer PCM.
    /// Outputs signal to channels 0 and 1 (left and right speakers of the controller).
    /// </summary>
    private sealed class MixFormatToneProvider : IWaveProvider
    {
        private readonly WaveFormat _format;
        private readonly double _phaseStep;
        private readonly float _gain;
        private double _phase;
        private long _samplesRemaining;

        public WaveFormat WaveFormat => _format;

        public MixFormatToneProvider(WaveFormat mixFormat, int frequencyHz, int durationMs, float gain)
        {
            _format = mixFormat;
            _gain = gain;
            _phaseStep = 2.0 * Math.PI * frequencyHz / mixFormat.SampleRate;

            long totalFrames = (long)(mixFormat.SampleRate * (durationMs / 1000.0));
            _samplesRemaining = totalFrames * mixFormat.Channels;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_samplesRemaining <= 0) return 0;

            int bytesRead = 0;
            int channels = _format.Channels;
            bool isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat ||
                           _format.BitsPerSample == 32;

            if (isFloat)
            {
                int bytesPerFrame = channels * 4;
                while (bytesRead + bytesPerFrame <= count && _samplesRemaining > 0)
                {
                    float sample = (float)(Math.Sin(_phase) * _gain);
                    _phase += _phaseStep;
                    if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        // On DualSense (4 channels):
                        // Channels 0 & 1 (1st & 2nd) = 3.5mm Headphone jack
                        // Channels 2 & 3 (3rd & 4th) = Internal Speaker / Voice Coil Actuators
                        // Send to channels 2 & 3 for speaker, and also channels 0 & 1 for headphones
                        float chSample = (channels >= 4)
                            ? ((ch == 2 || ch == 3 || ch == 0 || ch == 1) ? sample : 0f)
                            : sample;

                        byte[] b = BitConverter.GetBytes(chSample);
                        buffer[offset + bytesRead++] = b[0];
                        buffer[offset + bytesRead++] = b[1];
                        buffer[offset + bytesRead++] = b[2];
                        buffer[offset + bytesRead++] = b[3];
                        _samplesRemaining--;
                    }
                }
            }
            else // 16-bit PCM fallback
            {
                int bytesPerFrame = channels * 2;
                while (bytesRead + bytesPerFrame <= count && _samplesRemaining > 0)
                {
                    short sample = (short)(Math.Sin(_phase) * _gain * short.MaxValue);
                    _phase += _phaseStep;
                    if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        short chSample = (channels >= 4)
                            ? ((ch == 2 || ch == 3 || ch == 0 || ch == 1) ? sample : (short)0)
                            : sample;

                        buffer[offset + bytesRead++] = (byte)(chSample & 0xFF);
                        buffer[offset + bytesRead++] = (byte)((chSample >> 8) & 0xFF);
                        _samplesRemaining--;
                    }
                }
            }

            return bytesRead;
        }
    }
}
