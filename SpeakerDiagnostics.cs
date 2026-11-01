using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Harmonia;

/// <summary>Independent physical-output diagnostic: bypasses the system capture and all upmix logic.</summary>
internal static class SpeakerDiagnostics
{
    public static async Task PlayToneAsync(string endpointId, bool left, bool right, CancellationToken cancellationToken = default)
    {
        if (!left && !right) return;
        using var devices = new MMDeviceEnumerator();
        using var endpoint = devices.GetDevice(endpointId);
        const int rate = 48000;
        const int ms = 750;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
        var bytes = new byte[rate * ms / 1000 * 8];
        for (int frame = 0; frame < bytes.Length / 8; frame++)
        {
            float envelope = Math.Min(1f, Math.Min(frame / 480f, ((bytes.Length / 8) - frame) / 480f));
            float tone = (float)(Math.Sin(2 * Math.PI * 440 * frame / rate) * 0.15) * envelope;
            BitConverter.GetBytes(left ? tone : 0f).CopyTo(bytes, frame * 8);
            BitConverter.GetBytes(right ? tone : 0f).CopyTo(bytes, frame * 8 + 4);
        }
        var provider = new BufferedWaveProvider(format) { ReadFully = true, BufferDuration = TimeSpan.FromSeconds(2) };
        provider.AddSamples(bytes, 0, bytes.Length);
        using var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 100);
        output.Init(provider);
        output.Play();
        try { await Task.Delay(ms + 250, cancellationToken); }
        finally { output.Stop(); }
    }
}
