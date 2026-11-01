using NAudio.CoreAudioApi;
using NAudio.Wave;
using Harmonia.Suite;

internal sealed class CaptureVerifier : IDisposable {
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly MMDevice device;
    private readonly WasapiLoopbackCapture capture;
    private readonly List<float> samples = new();
    private readonly int left, right;
    public string Role { get; }
    public bool Passed { get; private set; }
    public string Error { get; private set; } = "No matching non-silent 4096-frame window captured";
    public CaptureVerifier(OutputRoute route) {
        Role = route.Role;
        (left, right) = Role switch { "FrontLeft" => (0, 0), "FrontRight" => (1, 1), "Center" => (2, 2), "Subwoofer" => (3, 3), "RearStereo" => (4, 5), _ => throw new ArgumentException("Role") };
        if (route.Gain <= 0) throw new ArgumentException("Physical isolation probes require nonzero gains.");
        device = enumerator.GetDevice(route.DeviceId); capture = new WasapiLoopbackCapture(device);
        if (capture.WaveFormat.BitsPerSample != 32 || capture.WaveFormat.Channels < 2) throw new IOException("Probe expects float32 stereo-or-wider loopback.");
        capture.DataAvailable += (_, e) => {
            if (Passed) return;
            try {
                for (int offset = 0; offset + capture.WaveFormat.BlockAlign <= e.BytesRecorded; offset += capture.WaveFormat.BlockAlign) {
                    samples.Add(BitConverter.ToSingle(e.Buffer, offset)); samples.Add(BitConverter.ToSingle(e.Buffer, offset + 4));
                    if (samples.Count < 8192) continue;
                    bool valid = true;
                    for (int c = 0; c < 2; c++) {
                        int expected = c == 0 ? left : right; double own = 0, others = 0;
                        for (int tone = 0; tone < 6; tone++) {
                            double real = 0, imaginary = 0;
                            for (int f = 0; f < 4096; f++) {
                                double phase = 2 * Math.PI * (220 + 110 * tone) * f / capture.WaveFormat.SampleRate;
                                real += samples[f * 2 + c] * Math.Cos(phase); imaginary += samples[f * 2 + c] * Math.Sin(phase);
                            }
                            double magnitude = Math.Sqrt(real * real + imaginary * imaginary) / 4096;
                            if (tone == expected) own = magnitude; else others = Math.Max(others, magnitude);
                        }
                        valid &= own > .00001 && own > others * 4;
                    }
                    samples.Clear(); if (valid) { Passed = true; Error = ""; break; }
                }
            } catch (Exception ex) { Error = ex.Message; }
        };
        capture.StartRecording();
    }
    public void Dispose() { capture.StopRecording(); capture.Dispose(); device.Dispose(); enumerator.Dispose(); }
}
