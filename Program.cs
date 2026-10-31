using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text.Json;

// Harmonia: non-invasive loopback mirror of current default Windows playback.
using var devices = new MMDeviceEnumerator();
using var source = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
var outputs = devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harmonia", "settings.json");
var config = Load(configPath);

if (args.Contains("--reset", StringComparer.OrdinalIgnoreCase))
{
    if (File.Exists(configPath)) File.Delete(configPath);
    Console.WriteLine("Harmonia selections cleared. Windows audio settings unchanged.");
    return;
}

Console.WriteLine("Harmonia — experimental stereo mirror\n");
Console.WriteLine($"Default Windows audio source: {source.FriendlyName}");
Console.WriteLine("The default output remains active while mirroring.\n");
for (int i = 0; i < outputs.Count; i++)
    Console.WriteLine($"{i + 1,2}: {outputs[i].FriendlyName}{(outputs[i].ID == source.ID ? " [capture source]" : "")}");
if (args.Contains("--list", StringComparer.OrdinalIgnoreCase)) return;

if (outputs.Count(x => x.ID != source.ID) < 2)
{
    Console.Error.WriteLine("Two active playback devices other than the Windows default output are needed.");
    return;
}

var leftDevice = Choose("LEFT monitor", outputs, config.LeftDeviceId, source.ID);
var rightDevice = Choose("RIGHT monitor", outputs, config.RightDeviceId, source.ID, leftDevice.ID);
Save(configPath, new Settings(leftDevice.ID, rightDevice.ID));

using var capture = new WasapiLoopbackCapture(source);
var format = capture.WaveFormat;
bool supportedEncoding = (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
    || ((format.Encoding == WaveFormatEncoding.Pcm || format.Encoding == WaveFormatEncoding.Extensible)
    && (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32));
if (format.Channels != 2 || !supportedEncoding || format.BlockAlign != format.BitsPerSample / 8 * 2)
    throw new NotSupportedException($"Unsupported format: {format}. Only stereo PCM 16/24/32 and 32-bit float are currently handled.");

var leftBuffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(650), DiscardOnBufferOverflow = true, ReadFully = true };
var rightBuffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(650), DiscardOnBufferOverflow = true, ReadFully = true };
using var leftOut = new WasapiOut(leftDevice, AudioClientShareMode.Shared, true, 100);
using var rightOut = new WasapiOut(rightDevice, AudioClientShareMode.Shared, true, 100);
leftOut.Init(leftBuffer);
rightOut.Init(rightBuffer);

int bytesPerSample = format.BitsPerSample / 8;
int frameSize = format.BlockAlign;
object gate = new();
capture.DataAvailable += (_, e) =>
{
    int length = e.BytesRecorded / frameSize * frameSize;
    if (length == 0) return;
    var l = new byte[length];
    var r = new byte[length];
    for (int off = 0; off < length; off += frameSize)
    {
        Buffer.BlockCopy(e.Buffer, off, l, off, bytesPerSample);
        Buffer.BlockCopy(e.Buffer, off, l, off + bytesPerSample, bytesPerSample);
        Buffer.BlockCopy(e.Buffer, off + bytesPerSample, r, off, bytesPerSample);
        Buffer.BlockCopy(e.Buffer, off + bytesPerSample, r, off + bytesPerSample, bytesPerSample);
    }
    lock (gate)
    {
        if (leftBuffer.BufferedDuration.TotalMilliseconds > 350) leftBuffer.ClearBuffer();
        if (rightBuffer.BufferedDuration.TotalMilliseconds > 350) rightBuffer.ClearBuffer();
        leftBuffer.AddSamples(l, 0, length);
        rightBuffer.AddSamples(r, 0, length);
    }
};

Console.WriteLine($"\nCapture format: {format}");
Console.WriteLine($"LEFT → {leftDevice.FriendlyName}");
Console.WriteLine($"RIGHT → {rightDevice.FriendlyName}");
Console.WriteLine("Press Enter to stop. No Windows audio settings are changed.");
try
{
    leftOut.Play();
    rightOut.Play();
    capture.StartRecording();
    Console.ReadLine();
}
finally
{
    capture.StopRecording();
    leftOut.Stop();
    rightOut.Stop();
}

static MMDevice Choose(string prompt, List<MMDevice> outputs, string? savedId, string sourceId, string? excludedId = null)
{
    var index = outputs.FindIndex(x => x.ID == savedId && x.ID != sourceId && x.ID != excludedId);
    while (true)
    {
        string defaultText = index >= 0 ? $", Enter to reuse {index + 1}" : "";
        Console.Write($"Select {prompt} [1-{outputs.Count}{defaultText}]: ");
        var answer = Console.ReadLine();
        int selection;
        if (string.IsNullOrWhiteSpace(answer) && index >= 0) selection = index;
        else if (int.TryParse(answer, out var number) && number >= 1 && number <= outputs.Count) selection = number - 1;
        else { Console.WriteLine("Invalid selection."); continue; }
        var device = outputs[selection];
        if (device.ID == sourceId) { Console.WriteLine("Cannot select the captured source as a target (feedback)."); continue; }
        if (device.ID == excludedId) { Console.WriteLine("Please choose distinct outputs."); continue; }
        return device;
    }
}

static Settings Load(string path)
{
    try { return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings() : new Settings(); }
    catch (Exception ex) { Console.Error.WriteLine($"Ignoring invalid saved preferences: {ex.Message}"); return new Settings(); }
}
static void Save(string path, Settings settings)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception ex) { Console.Error.WriteLine($"Could not save preferences: {ex.Message}"); }
}
record Settings(string? LeftDeviceId = null, string? RightDeviceId = null);
