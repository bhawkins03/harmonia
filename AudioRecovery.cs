using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text.Json;

namespace Harmonia;

/// <summary>
/// Records user-visible playback endpoint and mixer properties for recovery diagnostics.
/// This is deliberately not a Windows audio format/role restoration implementation:
/// changing endpoint format or default role requires additional verified platform support.
/// </summary>
internal static class AudioRecovery
{
    public sealed record Snapshot(DateTime CapturedUtc, string DefaultDeviceId,
        string DefaultDeviceName, int MixChannels, int MixSampleRate,
        string MixEncoding, string[] ActivePlaybackDevices);

    public static string SnapshotPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Harmonia", "recovery", "last-audio-snapshot.json");

    public static Snapshot Capture()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var capture = new WasapiLoopbackCapture(device);
        var format = capture.WaveFormat;
        return new Snapshot(DateTime.UtcNow, device.ID, device.FriendlyName,
            format.Channels, format.SampleRate, format.Encoding.ToString(),
            enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => d.FriendlyName + " | " + d.ID).ToArray());
    }

    public static Snapshot Save()
    {
        var snapshot = Capture();
        var directory = Path.GetDirectoryName(SnapshotPath)!;
        Directory.CreateDirectory(directory);
        var temporary = SnapshotPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SnapshotPath, true);
        return snapshot;
    }

    public static Snapshot? Load()
    {
        if (!File.Exists(SnapshotPath)) return null;
        return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SnapshotPath));
    }
}
