using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Harmonia;

internal enum SpeakerLayout { DualStereo, TripleStereoCenter, Surround51Beta }
internal enum SpeakerRole { FrontLeft, FrontRight, Center, Subwoofer, RearStereo }

internal sealed class AudioEngine : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly List<(WasapiOut Output, BufferedWaveProvider Buffer, SpeakerRole Role)> sinks = new();
    private WasapiLoopbackCapture? capture;
    private readonly object gate = new();
    private double lfeState;
    private readonly float[] levels = new float[6];
    // [front L, front R, center, LFE, rear L, rear R], pre-device sample peaks.
    public float[] GetSignalPeaks()
    {
        lock (gate) return (float[])levels.Clone();
    }

    public bool IsRunning { get; private set; }
    public string InputFormatDescription { get; private set; } = "Not running";
    public string DefaultDeviceId => enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
    public (string Id, string Name)[] GetDevices() =>
        enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => (d.ID, d.FriendlyName)).ToArray();

    public static SpeakerRole[] Roles(SpeakerLayout layout) => layout switch
    {
        SpeakerLayout.DualStereo => [SpeakerRole.FrontLeft, SpeakerRole.FrontRight],
        SpeakerLayout.TripleStereoCenter => [SpeakerRole.FrontLeft, SpeakerRole.FrontRight, SpeakerRole.Center],
        _ => [SpeakerRole.FrontLeft, SpeakerRole.FrontRight, SpeakerRole.Center, SpeakerRole.Subwoofer, SpeakerRole.RearStereo]
    };

    public void Start(SpeakerLayout layout, IReadOnlyDictionary<SpeakerRole, string> assignments, bool strictInput = false)
    {
        if (IsRunning) return;
        var required = Roles(layout).Where(r => r != SpeakerRole.Subwoofer).ToArray();
        foreach (var role in required)
            if (!assignments.TryGetValue(role, out var id) || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"Assign a playback device for {role}.");
        using var source = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var selected = Roles(layout).Where(assignments.ContainsKey).ToArray();
        var ids = selected.Select(r => assignments[r]).ToArray();
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
            throw new ArgumentException("Each speaker endpoint must be a different Windows playback device.");
        if (ids.Contains(source.ID))
            throw new ArgumentException("A selected output is the default Windows capture device; this risks audio feedback.");

        try
        {
            capture = new WasapiLoopbackCapture(source);
            var input = capture.WaveFormat;
            int bytesPerSample = input.BitsPerSample / 8;
            bool isFloat = input.Encoding == WaveFormatEncoding.IeeeFloat || (input is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00AA00389B71"));
            bool supported = isFloat && input.BitsPerSample == 32
                || (input.Encoding == WaveFormatEncoding.Pcm || input.Encoding == WaveFormatEncoding.Extensible)
                   && (input.BitsPerSample == 16 || input.BitsPerSample == 24 || input.BitsPerSample == 32);
            if ((input.Channels != 2 && input.Channels != 6) || !supported || input.BlockAlign != bytesPerSample * input.Channels)
                throw new NotSupportedException($"Only 2-channel or 6-channel PCM/float capture is supported: {input}.");
            InputFormatDescription = input.Channels == 6 ? $"Native 5.1 PCM ({input.SampleRate} Hz)" : strictInput ? $"Stereo input • no surround upmix ({input.SampleRate} Hz)" : $"Stereo input • synthesized surround ({input.SampleRate} Hz)";
            var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(input.SampleRate, 2);
            foreach (var role in selected)
            {
                using var device = enumerator.GetDevice(assignments[role]);
                var buffer = new BufferedWaveProvider(outputFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(650),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
                output.Init(buffer);
                sinks.Add((output, buffer, role));
            }

            lfeState = 0;
            double alpha = 1 - Math.Exp(-2 * Math.PI * 120 / input.SampleRate);
            capture.DataAvailable += (_, e) =>
            {
                int frames = e.BytesRecorded / input.BlockAlign;
                if (frames == 0) return;
                var buffers = new Dictionary<SpeakerRole, byte[]>();
                var peaks = new float[6];
                foreach (var sink in sinks) buffers[sink.Role] = new byte[frames * 8];
                for (int frame = 0; frame < frames; frame++)
                {
                    int off = frame * input.BlockAlign;
                    float l = ReadSample(e.Buffer, off, input, isFloat);
                    float r = ReadSample(e.Buffer, off + bytesPerSample, input, isFloat);
                    bool discrete = input.Channels == 6;
                    // Standard WAVEFORMATEXTENSIBLE 5.1 mapping: FL FR FC LFE BL BR,
                    // or FL FR FC LFE SL SR. Both have the same interleaved indices.
                    float center = discrete ? ReadSample(e.Buffer, off + bytesPerSample * 2, input, isFloat) : strictInput ? 0 : (l + r) * 0.5f;
                    lfeState += alpha * (center - lfeState);
                    float lfe = discrete ? ReadSample(e.Buffer, off + bytesPerSample * 3, input, isFloat) : strictInput ? 0 : (float)lfeState * 0.5f;
                    float rearL = discrete ? ReadSample(e.Buffer, off + bytesPerSample * 4, input, isFloat) : strictInput ? 0 : (l - r) * 0.5f;
                    float rearR = discrete ? ReadSample(e.Buffer, off + bytesPerSample * 5, input, isFloat) : strictInput ? 0 : (r - l) * 0.5f;
                    foreach (var (role, buffer) in buffers)
                    {
                        (float a, float b) = role switch
                        {
                            SpeakerRole.FrontLeft => (l, l),
                            SpeakerRole.FrontRight => (r, r),
                            SpeakerRole.Center => (center, center),
                            SpeakerRole.Subwoofer => (lfe, lfe),
                            SpeakerRole.RearStereo => (rearL, rearR),
                            _ => (0, 0)
                        };
                        int leftIndex = role switch { SpeakerRole.FrontLeft => 0, SpeakerRole.FrontRight => 1, SpeakerRole.Center => 2, SpeakerRole.Subwoofer => 3, SpeakerRole.RearStereo => 4, _ => 0 };
                        peaks[leftIndex] = Math.Max(peaks[leftIndex], Math.Abs(a));
                        if (role == SpeakerRole.RearStereo) peaks[5] = Math.Max(peaks[5], Math.Abs(b));
                        Buffer.BlockCopy(BitConverter.GetBytes(Math.Clamp(a, -1f, 1f)), 0, buffer, frame * 8, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(Math.Clamp(b, -1f, 1f)), 0, buffer, frame * 8 + 4, 4);
                    }
                }
                lock (gate)
                {
                    // The UI polls at ~20 FPS; hold a short peak and smoothly decay.
                    for (int i = 0; i < levels.Length; i++) levels[i] = Math.Max(peaks[i], levels[i] * 0.78f);
                    foreach (var sink in sinks)
                    {
                        if (sink.Buffer.BufferedDuration.TotalMilliseconds > 350) sink.Buffer.ClearBuffer();
                        sink.Buffer.AddSamples(buffers[sink.Role], 0, buffers[sink.Role].Length);
                    }
                }
            };
            foreach (var sink in sinks) sink.Output.Play();
            capture.StartRecording();
            IsRunning = true;
        }
        catch { Stop(); throw; }
    }

    private static float ReadSample(byte[] data, int offset, WaveFormat format, bool isFloat)
    {
        if (isFloat && format.BitsPerSample == 32)
            return BitConverter.ToSingle(data, offset);
        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(data, offset) / 32768f,
            24 => ((data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16) << 8 >> 8) / 8388608f,
            32 => BitConverter.ToInt32(data, offset) / 2147483648f,
            _ => 0
        };
    }

    public void Stop()
    {
        IsRunning = false;
        try { capture?.StopRecording(); } catch { }
        foreach (var s in sinks) { try { s.Output.Stop(); } catch { } s.Output.Dispose(); }
        sinks.Clear();
        capture?.Dispose();
        capture = null;
        InputFormatDescription = "Not running";
        lock (gate) Array.Clear(levels, 0, levels.Length);
    }

    public void Dispose() { Stop(); enumerator.Dispose(); }
}
