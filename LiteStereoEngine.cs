using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Harmonia;

/// <summary>Driverless, stereo-only endpoint loopback distribution for Lite.</summary>
internal sealed class LiteStereoEngine : IDisposable
{
    private readonly MMDeviceEnumerator devices = new();
    private readonly List<(WasapiOut Output, BufferedWaveProvider Buffer, bool Left)> outputs = new();
    private WasapiLoopbackCapture? capture;
    private readonly object gate = new();
    private float leftPeak, rightPeak;
    public bool IsRunning { get; private set; }
    public string Description { get; private set; } = "Stopped";
    public float LeftGain { get; set; } = 1;
    public float RightGain { get; set; } = 1;
    public (float Left, float Right) Peaks { get { lock (gate) return (leftPeak, rightPeak); } }
    public string DefaultDeviceId => devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
    public (string Id, string Name)[] GetDevices() => devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
        .Select(d => (d.ID, d.FriendlyName)).ToArray();

    public void Start(string leftId, string rightId)
    {
        if (IsRunning) return;
        if (string.IsNullOrWhiteSpace(leftId) || string.IsNullOrWhiteSpace(rightId))
            throw new ArgumentException("Choose a playback device for both left and right.");
        using var source = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Left and right must use separate Windows playback devices.");
        if (string.Equals(source.ID, leftId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.ID, rightId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Windows default playback device is the audio source and cannot also be one of the routed outputs.");
        try
        {
            capture = new WasapiLoopbackCapture(source);
            var input = capture.WaveFormat;
            bool floating = input.Encoding == WaveFormatEncoding.IeeeFloat ||
                (input is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00AA00389B71"));
            bool pcm = input.Encoding == WaveFormatEncoding.Pcm ||
                (input is WaveFormatExtensible extPcm && extPcm.SubFormat == new Guid("00000001-0000-0010-8000-00AA00389B71"));
            int bytesPerSample = input.BitsPerSample / 8;
            if (input.Channels != 2 || !(floating && input.BitsPerSample == 32 ||
                  pcm && (input.BitsPerSample == 16 || input.BitsPerSample == 24 || input.BitsPerSample == 32)) ||
                input.BlockAlign != bytesPerSample * 2)
                throw new NotSupportedException("Lite requires a stereo PCM/Float Windows default endpoint. Format: " + input);
            var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(input.SampleRate, 2);
            foreach (var (id, left) in new[] { (leftId, true), (rightId, false) })
            {
                using var endpoint = devices.GetDevice(id);
                var buffer = new BufferedWaveProvider(outputFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(650),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                var playback = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 100);
                playback.Init(buffer);
                outputs.Add((playback, buffer, left));
            }
            capture.DataAvailable += (_, e) =>
            {
                int frames = e.BytesRecorded / input.BlockAlign;
                if (frames == 0) return;
                byte[] leftBytes = new byte[frames * 8], rightBytes = new byte[frames * 8];
                float lp = 0, rp = 0;
                for (int frame = 0; frame < frames; frame++)
                {
                    int offset = frame * input.BlockAlign;
                    float l = ReadSample(e.Buffer, offset, input.BitsPerSample, floating) * LeftGain;
                    float r = ReadSample(e.Buffer, offset + bytesPerSample, input.BitsPerSample, floating) * RightGain;
                    lp = Math.Max(lp, Math.Abs(l));
                    rp = Math.Max(rp, Math.Abs(r));
                    // Each physical stereo endpoint plays one mono source channel equally in both speakers.
                    int target = frame * 8;
                    WriteMono(leftBytes, target, l);
                    WriteMono(rightBytes, target, r);
                }
                lock (gate) { leftPeak = lp; rightPeak = rp; }
                foreach (var sink in outputs)
                    sink.Buffer.AddSamples(sink.Left ? leftBytes : rightBytes, 0, frames * 8);
            };
            capture.RecordingStopped += (_, _) => Stop();
            foreach (var sink in outputs) sink.Output.Play();
            capture.StartRecording();
            Description = $"Stereo routing • {input.SampleRate} Hz";
            IsRunning = true;
        }
        catch { Stop(); throw; }
    }

    private static void WriteMono(byte[] bytes, int offset, float sample)
    {
        sample = Math.Clamp(sample, -1f, 1f);
        BitConverter.GetBytes(sample).CopyTo(bytes, offset);
        BitConverter.GetBytes(sample).CopyTo(bytes, offset + 4);
    }

    private static float ReadSample(byte[] buffer, int offset, int bits, bool floating)
    {
        if (floating) return BitConverter.ToSingle(buffer, offset);
        if (bits == 16) return BitConverter.ToInt16(buffer, offset) / 32768f;
        if (bits == 32) return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        int n = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        if ((n & 0x800000) != 0) n |= unchecked((int)0xFF000000);
        return n / 8388608f;
    }

    public void Stop()
    {
        IsRunning = false;
        try { capture?.StopRecording(); } catch { }
        capture?.Dispose();
        capture = null;
        foreach (var sink in outputs)
        {
            try { sink.Output.Stop(); } catch { }
            sink.Output.Dispose();
        }
        outputs.Clear();
        lock (gate) { leftPeak = rightPeak = 0; }
        Description = "Stopped";
    }
    public void Dispose() { Stop(); devices.Dispose(); }
}
