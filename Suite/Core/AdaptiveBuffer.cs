namespace Harmonia.Suite;

// Each physical endpoint pulls independently. Fractional interpolation with a
// bounded occupancy servo follows its hardware clock, rather than dropping
// complete packets periodically. All positions are measured in stereo frames.
public sealed class AdaptiveBuffer {
    private readonly float[] samples;
    private readonly object gate = new();
    private readonly int target;
    private long write;
    private double read;
    private bool primed;
    public long Underruns { get; private set; }
    public long Overruns { get; private set; }
    public double DriftPpm { get; private set; }
    public int BufferedFrames { get { lock (gate) return (int)(write - read); } }
    public AdaptiveBuffer(int capacity = 24000, int targetFrames = 2400) {
        if (targetFrames < 2 || capacity < targetFrames * 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        samples = new float[capacity * 2]; target = targetFrames;
    }
    public void Clear() { lock (gate) { write = 0; read = 0; primed = false; DriftPpm = 0; Array.Clear(samples); } }
    public void Add(ReadOnlySpan<float> stereo) {
        if (stereo.Length % 2 != 0) throw new ArgumentException("Whole stereo frames required.");
        lock (gate) {
            int frames = stereo.Length / 2, capacity = samples.Length / 2;
            if (frames >= capacity) { stereo = stereo[^((capacity - 1) * 2)..]; frames = capacity - 1; }
            if (write - read + frames >= capacity) { Overruns++; read = Math.Max(read, write + frames - target); primed = false; }
            for (int f = 0; f < frames; f++) {
                int i = (int)((write + f) % capacity) * 2;
                samples[i] = stereo[f * 2]; samples[i + 1] = stereo[f * 2 + 1];
            }
            write += frames;
        }
    }
    public void Read(Span<float> output) {
        if (output.Length % 2 != 0) throw new ArgumentException("Whole stereo frames required.");
        lock (gate) {
            output.Clear();
            if (!primed) { if (write - read < target) return; primed = true; }
            double desired = Math.Clamp((write - read - target) * 2, -2000, 2000);
            DriftPpm = DriftPpm * 0.98 + desired * 0.02;
            double step = 1 + DriftPpm / 1_000_000;
            int capacity = samples.Length / 2;
            for (int f = 0; f < output.Length / 2; f++) {
                if (read + 1 >= write) { Underruns++; primed = false; break; }
                long first = (long)read; float fraction = (float)(read - first);
                int a = (int)(first % capacity) * 2, b = (int)((first + 1) % capacity) * 2;
                for (int c = 0; c < 2; c++) output[f * 2 + c] = samples[a + c] + (samples[b + c] - samples[a + c]) * fraction;
                read += step;
            }
        }
    }
}

public static class ChannelRouter {
    public static float[] Map(ReadOnlySpan<float> input, int channels, string role, float gain, Span<float> peaks) {
        if ((channels != 2 && channels != 6) || input.Length % channels != 0 || !float.IsFinite(gain) || gain < 0 || gain > 2)
            throw new InvalidDataException("Invalid PCM shape or gain.");
        (int left, int right) = role switch {
            "FrontLeft" => (0, 0), "FrontRight" => (1, 1), "Center" => (2, 2),
            "Subwoofer" => (3, 3), "RearStereo" => (4, 5), _ => throw new InvalidDataException("Unknown role.")
        };
        var result = new float[input.Length / channels * 2];
        for (int f = 0; f < result.Length / 2; f++) {
            for (int c = 0; c < 2; c++) {
                int source = c == 0 ? left : right;
                float sample = source < channels ? input[f * channels + source] : 0;
                if (!float.IsFinite(sample)) throw new InvalidDataException("Nonfinite driver PCM.");
                sample = Math.Clamp(sample * gain, -1, 1); result[f * 2 + c] = sample;
                if (source < peaks.Length) peaks[source] = Math.Max(peaks[source], Math.Abs(sample));
            }
        }
        return result;
    }
}
