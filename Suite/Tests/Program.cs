using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.IO.Compression;
using Harmonia.Suite;

int checks = 0;
void Check(bool condition, string text) { checks++; if (!condition) throw new Exception(text); }
void Deny(Action action, string text) { try { action(); } catch { checks++; return; } throw new Exception("Accepted: " + text); }
if (args.Length > 0) {
    if (args[0] == "devices") {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        Console.WriteLine(JsonSerializer.Serialize(enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active)
            .Select(d => new { d.ID, d.FriendlyName, Rate = d.AudioClient.MixFormat.SampleRate, Channels = d.AudioClient.MixFormat.Channels })));
        return;
    }
    async Task<ControlReply> Call(ControlRequest request) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", ControlWire.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(timeout.Token); await ControlWire.WriteAsync(pipe, request, timeout.Token);
        var reply = await ControlWire.ReadAsync<ControlReply>(pipe, timeout.Token);
        if (!reply.Success) throw new IOException(reply.Error);
        return reply;
    }
    if (args[0] == "status") { Console.WriteLine(JsonSerializer.Serialize((await Call(new("status"))).Status)); return; }
    if (args[0] == "service-devices") { Console.WriteLine(JsonSerializer.Serialize((await Call(new("devices"))).Status.Devices)); return; }
    if (args[0] is "probe" or "protected" or "route") {
        bool routing = args[0] == "route", protectedContent = args[0] == "protected";
        var request = routing ? new ControlRequest("start", JsonSerializer.Deserialize<RoutingPlan>(File.ReadAllText(args[1]))) : new ControlRequest("probe");
        var captures = routing ? request.Plan!.Outputs.Select(o => new CaptureVerifier(o)).ToArray() : [];
        var initial = await Call(request); Console.WriteLine("Control accepted: " + JsonSerializer.Serialize(initial.Status));
        ulong frames = 0; var peaks = new float[6]; string detail = ""; bool protectedSeen = false;
        try {
            for (int i = 0; i < 150; i++) {
                var state = (await Call(new("status"))).Status; frames = Math.Max(frames, state.Frames); detail = state.Detail;
                protectedSeen |= detail.Contains("Protected", StringComparison.OrdinalIgnoreCase);
                for (int c = 0; c < 6; c++) peaks[c] = Math.Max(peaks[c], state.Peaks[c]);
                await Task.Delay(100);
            }
        } finally { await Call(new("stop")); foreach (var capture in captures) capture.Dispose(); }
        Check(protectedContent ? frames == 0 && protectedSeen : frames > 1000 && (routing ? peaks.Any(p => p > 0) : peaks.All(p => p > 0)), "Live service PCM/protection check: " + detail);
        foreach (var capture in captures) Check(capture.Passed, "Physical loopback isolation failed for " + capture.Role + ": " + capture.Error);
        Console.WriteLine(JsonSerializer.Serialize(new { frames, peaks, detail }));
        Console.WriteLine("PASS: live driver-to-service " + args[0]); return;
    }
    throw new ArgumentException("Unknown test mode.");
}

var input = Enumerable.Range(0, 60).Select(i => (i % 6 + 1) * .05f).ToArray();
foreach ((string role, int a, int b) in new[] { ("FrontLeft", 0, 0), ("FrontRight", 1, 1), ("Center", 2, 2), ("Subwoofer", 3, 3), ("RearStereo", 4, 5) }) {
    var output = ChannelRouter.Map(input, 6, role, .5f, new float[6]);
    for (int f = 0; f < 10; f++) { Check(output[f * 2] == input[f * 6 + a] * .5f, role + " left"); Check(output[f * 2 + 1] == input[f * 6 + b] * .5f, role + " right"); }
}
foreach (string role in new[] { "Center", "Subwoofer", "RearStereo" })
    Check(ChannelRouter.Map([.2f, .8f], 2, role, 1, new float[6]).All(f => f == 0), "Stereo must not synthesize " + role);
Deny(() => ChannelRouter.Map([float.NaN, 0], 2, "FrontLeft", 1, new float[6]), "NaN PCM");
Deny(() => new RoutingPlan("DualStereo", [new("FrontLeft", "same"), new("FrontRight", "same")]).Validate(), "Duplicate endpoint");
var lease = new ControllerLease(); lease.Acquire("userA"); Deny(() => lease.Acquire("userB"), "Other user start"); Deny(() => lease.Require("userB"), "Other user stop"); lease.Release(); lease.Acquire("userB");
foreach (double drift in new[] { -1000.0, 0, 1000 }) {
    var buffer = new AdaptiveBuffer(); var packet = Enumerable.Repeat(.125f, 960).ToArray(); var output = new float[960];
    for (int i = 0; i < 6; i++) buffer.Add(packet);
    double deviceFrames = 0; int low = int.MaxValue, high = 0;
    for (int tick = 0; tick < 60000; tick++) {
        buffer.Add(packet); deviceFrames += 480 * (1 + drift / 1_000_000);
        int frames = (int)deviceFrames; deviceFrames -= frames;
        var read = frames == 480 ? output : new float[frames * 2]; buffer.Read(read);
        low = Math.Min(low, buffer.BufferedFrames); high = Math.Max(high, buffer.BufferedFrames);
        Check(read.All(float.IsFinite), "Drift output must be finite");
    }
    Check(buffer.Overruns == 0 && buffer.Underruns == 0 && low > 0 && high < 6000, "Ten-minute independent clock simulation " + drift);
    buffer.Clear(); buffer.Read(output); Check(output.All(f => f == 0), "Discontinuity flush");
}
var overflow = new AdaptiveBuffer(100, 20); overflow.Add(new float[198]); overflow.Add(new float[198]); Check(overflow.BufferedFrames <= 100, "Bounded overflow");
foreach (string path in new[] { "../x", "App/../x", "C:/x", "App\\x", "/x", "App/x:", "App/x.", "App//x", "App/CON.exe", "App/LPT1" }) Deny(() => SuiteManifest.SafeRelative(path), path);
Deny(ReleaseSecurity.RequirePublisher, "Unconfigured publisher must disable production updates");
var files = new[] { new ReleaseFile("App/Harmonia.exe", new string('A', 64), 12), new ReleaseFile("Installer/Harmonia.Suite.Installer.exe", new string('B', 64), 12) };
var manifest = new SuiteManifest(1, "suite", "0.3.1", "0.3.0", "9.30.11.164", "0.3.0", "0.3.9", "9.0.0.0", "10.0.0.0", false, false, 1, files);
manifest.Validate(); checks++;
Deny(() => (manifest with { Channel = "lite" }).Validate(), "Lite release in Suite channel");
Deny(() => manifest.Compatible("1.0.0", "9.30.11.164"), "Service compatibility");
Deny(() => manifest.Compatible("0.3.0", "11.0.0.0"), "Driver compatibility");
Deny(() => (manifest with { Files = [files[0], files[0]] }).Validate(), "Duplicate manifest files");
string archiveRoot = Path.Combine(Path.GetTempPath(), "HarmoniaArchiveTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(archiveRoot);
try {
    string archivePath = Path.Combine(archiveRoot, "traversal.zip");
    using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); writer.Write("must never escape"); }
    Deny(() => ReleaseSecurity.Extract(archivePath, Path.Combine(archiveRoot, "stage")), "Archive traversal");
    Check(!File.Exists(Path.Combine(archiveRoot, "escape.txt")), "Traversal must not create outside staging");
} finally { Directory.Delete(archiveRoot, true); }
foreach (bool driver in new[] { false, true }) foreach (bool service in new[] { false, true }) {
    var platform = new FakeDeployment(); DeploymentTransaction.Run(platform, driver, service);
    Check(platform.Events.SequenceEqual(new[] { "stop" }.Concat(driver ? ["driver"] : Array.Empty<string>()).Concat(service ? ["service"] : Array.Empty<string>()).Concat(["health", "commit"])), "Coordinated component order");
    foreach (string failure in new[] { "stop", "driver", "service", "health", "commit" }.Where(f => f != "driver" || driver).Where(f => f != "service" || service)) {
        platform = new FakeDeployment { Failure = failure };
        Deny(() => DeploymentTransaction.Run(platform, driver, service), "Failed update");
        Check(platform.Events.Last() == "restore", "Rollback after " + failure);
    }
}
var reboot = new FakeDeployment { Reboot = true };
Deny(() => DeploymentTransaction.Run(reboot, true, true), "Required reboot must suspend transaction");
Check(reboot.Events.SequenceEqual(["stop", "driver"]), "No rollback or service start before required reboot");
using (var stream = new MemoryStream()) {
    await ControlWire.WriteAsync(stream, new ControlRequest("start", new("DualStereo", [new("FrontLeft", "one"), new("FrontRight", "two")])), default);
    stream.Position = 0; var request = await ControlWire.ReadAsync<ControlRequest>(stream, default);
    Check(request.Plan?.Outputs[1].DeviceId == "two", "Control configuration roundtrip");
}
Console.WriteLine($"PASS: {checks} Suite channel, gain, clock-drift, bounds, lease, release-policy and rollback checks.");

sealed class FakeDeployment : IDeploymentPlatform {
    public string? Failure; public bool Reboot; public List<string> Events { get; } = new();
    private void Step(string step) { Events.Add(step); if (Failure == step) throw new IOException("Injected " + step); }
    public void Stop() => Step("stop"); public void InstallDriver() { Step("driver"); if (Reboot) throw new DeploymentRebootRequiredException("Injected reboot"); } public void InstallService() => Step("service");
    public void VerifyHealth() => Step("health"); public void Commit() => Step("commit"); public void Restore() => Step("restore");
}
