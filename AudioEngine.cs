using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Harmonia;

internal sealed class AudioEngine : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private WasapiLoopbackCapture? capture;
    private WasapiOut? leftOut, rightOut;
    private BufferedWaveProvider? leftBuffer, rightBuffer;
    private readonly object gate = new();

    public bool IsRunning { get; private set; }

    public (string Id, string Name)[] GetDevices() =>
        enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                  .Select(d => (d.ID, d.FriendlyName)).ToArray();

    public string DefaultDeviceId =>
        enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;

    public void Start(string leftId, string rightId)
    {
        if (IsRunning) return;
        if (leftId == rightId) throw new ArgumentException("Select two different monitors.");
        using var source = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (source.ID == leftId || source.ID == rightId)
            throw new ArgumentException("Monitor outputs cannot be the current Windows default audio device. This prevents feedback.");

        try
        {
            using var left = enumerator.GetDevice(leftId);
            using var right = enumerator.GetDevice(rightId);
            capture = new WasapiLoopbackCapture(source);
            var format = capture.WaveFormat;
            int bps = format.BitsPerSample / 8;
            bool supported = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32
                || (format.Encoding == WaveFormatEncoding.Pcm || format.Encoding == WaveFormatEncoding.Extensible)
                   && (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32);
            if (format.Channels != 2 || !supported || format.BlockAlign != 2 * bps)
                throw new NotSupportedException($"Unsupported source format: {format}. Requires stereo PCM or 32-bit float.");

            leftBuffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(650), ReadFully = true, DiscardOnBufferOverflow = true };
            rightBuffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(650), ReadFully = true, DiscardOnBufferOverflow = true };
            leftOut = new WasapiOut(left, AudioClientShareMode.Shared, true, 100);
            rightOut = new WasapiOut(right, AudioClientShareMode.Shared, true, 100);
            leftOut.Init(leftBuffer);
            rightOut.Init(rightBuffer);

            capture.DataAvailable += (_, args) =>
            {
                int len = args.BytesRecorded / format.BlockAlign * format.BlockAlign;
                if (len == 0) return;
                var l = new byte[len];
                var r = new byte[len];
                for (int off = 0; off < len; off += format.BlockAlign)
                {
                    Buffer.BlockCopy(args.Buffer, off, l, off, bps);
                    Buffer.BlockCopy(args.Buffer, off, l, off + bps, bps);
                    Buffer.BlockCopy(args.Buffer, off + bps, r, off, bps);
                    Buffer.BlockCopy(args.Buffer, off + bps, r, off + bps, bps);
                }
                lock (gate)
                {
                    if (leftBuffer.BufferedDuration.TotalMilliseconds > 350) leftBuffer.ClearBuffer();
                    if (rightBuffer.BufferedDuration.TotalMilliseconds > 350) rightBuffer.ClearBuffer();
                    leftBuffer.AddSamples(l, 0, len);
                    rightBuffer.AddSamples(r, 0, len);
                }
            };

            leftOut.Play();
            rightOut.Play();
            capture.StartRecording();
            IsRunning = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try { capture?.StopRecording(); } catch { }
        try { leftOut?.Stop(); } catch { }
        try { rightOut?.Stop(); } catch { }
        capture?.Dispose();
        leftOut?.Dispose();
        rightOut?.Dispose();
        capture = null;
        leftOut = rightOut = null;
        leftBuffer = rightBuffer = null;
    }

    public void Dispose()
    {
        Stop();
        enumerator.Dispose();
    }
}
