using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Harmonia.Transport;
using Microsoft.Win32;

namespace Harmonia.Suite.Service;

internal sealed class RoutingHost : IDisposable {
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private RoutingPlan? currentPlan;
    private IReadOnlyDictionary<string, float> gains = new Dictionary<string, float>();
    private static string ServiceVersion => typeof(RoutingHost).Assembly.GetName().Version!.ToString(3);
    private ServiceStatus status = new(false, "Stopped", ServiceVersion, ReadDriverVersion(), new float[6]);
    public ServiceStatus Snapshot { get { lock (gate) return status with { Peaks = (float[])status.Peaks.Clone() }; } }
    public PlaybackDevice[] Devices() {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Where(d => !IsVirtual(d)).Select(d => new PlaybackDevice(d.ID, d.FriendlyName)).ToArray();
    }
    private static bool IsVirtual(MMDevice device) => device.DeviceFriendlyName.Contains("Harmonia", StringComparison.OrdinalIgnoreCase) ||
        device.FriendlyName.Contains("Harmonia", StringComparison.OrdinalIgnoreCase);
    public void Start(RoutingPlan plan) {
        plan.Validate(); Stop(); currentPlan = plan;
        Volatile.Write(ref gains, plan.Outputs.ToDictionary(o => o.Role, o => o.Gain));
        cancellation = new(); var token = cancellation.Token;
        worker = Task.Run(() => Run(plan, token));
    }
    public void UpdateGains(RoutingPlan plan) {
        plan.Validate();
        if (currentPlan is null || currentPlan.Layout != plan.Layout ||
            !currentPlan.Outputs.Select(o => (o.Role, o.DeviceId)).OrderBy(o => o.Role).SequenceEqual(plan.Outputs.Select(o => (o.Role, o.DeviceId)).OrderBy(o => o.Role)))
            throw new ArgumentException("Stop routing before changing device assignments.");
        Volatile.Write(ref gains, plan.Outputs.ToDictionary(o => o.Role, o => o.Gain));
    }
    public void Probe() {
        Stop(); cancellation = new(); var token = cancellation.Token;
        worker = Task.Run(() => Run(new RoutingPlan("TransportProbe", []), token));
    }
    public void VerifyDriver() { using var driver = new DriverClient(); if (driver.Call(Operation.State).Message.Result != Status.Ok) throw new IOException("Driver health check failed."); }
    private void Run(RoutingPlan plan, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            var sinks = new List<PhysicalSink>();
            try {
                using var devices = new MMDeviceEnumerator();
                foreach (var route in plan.Outputs) {
                    using var device = devices.GetDevice(route.DeviceId);
                    // Driver endpoints must never become a routed sink (feedback).
                    if (IsVirtual(device)) throw new ArgumentException("A virtual Harmonia endpoint cannot be an output.");
                    if (device.State != DeviceState.Active) throw new IOException("Output device is unavailable: " + device.FriendlyName);
                    sinks.Add(new PhysicalSink(device, route));
                }
                using var driver = new DriverClient();
                ulong generation = 0, sequence = 0, nextFrame = 0, frames = 0, discontinuities = 0;
                bool accepting = false;
                while (!token.IsCancellationRequested) {
                    var state = driver.Call(Operation.State).Message;
                    if (state.Result != Status.Ok) throw new IOException("Driver state: " + state.Result);
                    if (state.State != StreamState.Running || state.Generation == 0) {
                        Flush(); accepting = false;
                        Publish(state.State == StreamState.Protected ? "Protected content: export denied" : "Waiting for virtual endpoint playback", false);
                        token.WaitHandle.WaitOne(20); continue;
                    }
                    var packet = driver.Call(Operation.ReadFrames, state.Generation);
                    if (packet.Message.Result == Status.NoData) { token.WaitHandle.WaitOne(2); continue; }
                    if (packet.Message.Result != Status.Ok) {
                        Flush(); accepting = false;
                        if (packet.Message.Result is Status.Removed or Status.NotAttached or Status.CounterExhausted) throw new IOException("Driver read: " + packet.Message.Result);
                        Publish("Driver: " + packet.Message.Result, false); token.WaitHandle.WaitOne(10); continue;
                    }
                    var m = packet.Message;
                    if (!accepting || generation != m.Generation || (m.Flags & TransportV1.Discontinuity) != 0 ||
                        (sequence != 0 && (m.Sequence != sequence + 1 || m.FirstFrame != nextFrame))) {
                        Flush(); discontinuities++;
                    }
                    accepting = true; generation = m.Generation; sequence = m.Sequence; nextFrame = m.FirstFrame + m.FrameCount;
                    var samples = MemoryMarshal.Cast<byte, float>(packet.Pcm.AsSpan());
                    var peaks = new float[6];
                    var packetGains = Volatile.Read(ref gains);
                    foreach (var sink in sinks) sink.Buffer.Add(ChannelRouter.Map(samples, m.Channels, sink.Route.Role, packetGains.GetValueOrDefault(sink.Route.Role, 1), peaks));
                    foreach (var sink in sinks) if (sink.Failure is not null) throw new IOException("Physical output stopped.", sink.Failure);
                    frames += m.FrameCount;
                    lock (gate) status = new(true, $"Virtual driver: {m.Channels} channels / {m.SampleRate} Hz", ServiceVersion, status.DriverVersion,
                        peaks, frames, discontinuities, m.OverflowFrames, sinks.Sum(s => s.Buffer.Underruns), sinks.Select(s => Math.Abs(s.Buffer.DriftPpm)).DefaultIfEmpty(0).Max());
                    if (sinks.Count == 0) {
                        var probePeaks = new float[6];
                        foreach (string role in new[] { "FrontLeft", "FrontRight", "Center", "Subwoofer", "RearStereo" }) ChannelRouter.Map(samples, m.Channels, role, 1, probePeaks);
                        lock (gate) status = status with { Peaks = probePeaks };
                    }
                }
                void Flush() { foreach (var sink in sinks) sink.Buffer.Clear(); generation = 0; sequence = 0; nextFrame = 0; }
                void Publish(string detail, bool running) { lock (gate) status = status with { Running = running, Detail = detail, Peaks = new float[6] }; }
            } catch (Exception ex) {
                lock (gate) status = status with { Running = false, Detail = "Reconnecting: " + ex.Message, Peaks = new float[6] };
            } finally { foreach (var sink in sinks) sink.Dispose(); }
            token.WaitHandle.WaitOne(1000);
        }
    }
    public void Stop() {
        cancellation?.Cancel(); worker?.GetAwaiter().GetResult(); worker = null;
        cancellation?.Dispose(); cancellation = null;
        currentPlan = null;
        lock (gate) status = status with { Running = false, Detail = "Stopped", Peaks = new float[6] };
    }
    public void Dispose() => Stop();
    private static string ReadDriverVersion() {
        try {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT");
            foreach (string category in root?.GetSubKeyNames() ?? []) {
                using var parent = root!.OpenSubKey(category);
                foreach (string instance in parent?.GetSubKeyNames() ?? []) {
                    using var device = parent!.OpenSubKey(instance);
                    if (device?.GetValue("HardwareID") is not string[] ids || !ids.Contains(@"ROOT\HarmoniaVirtualAudio", StringComparer.OrdinalIgnoreCase)) continue;
                    using var driver = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + (device.GetValue("Driver") as string));
                    return driver?.GetValue("DriverVersion") as string ?? "Unknown";
                }
            }
            return "Not installed";
        }
        catch { return "Not installed"; }
    }
    private sealed class PhysicalSink : IDisposable {
        public OutputRoute Route { get; }
        public AdaptiveBuffer Buffer { get; } = new();
        private readonly WasapiOut output;
        public Exception? Failure { get; private set; }
        public PhysicalSink(MMDevice device, OutputRoute route) {
            Route = route;
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
            try {
                ISampleProvider source = new BufferedSource(Buffer);
                using var formatClient = device.AudioClient;
                int rate = formatClient.MixFormat.SampleRate;
                if (rate != 48000) source = new WdlResamplingSampleProvider(source, rate);
                output.Init(source); output.PlaybackStopped += (_, e) => Failure = e.Exception ?? new IOException("Playback stopped unexpectedly.");
                output.Play();
            } catch { output.Dispose(); throw; }
        }
        public void Dispose() { try { output.Stop(); } finally { output.Dispose(); Buffer.Clear(); } }
    }
    private sealed class BufferedSource(AdaptiveBuffer buffer) : ISampleProvider {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] samples, int offset, int count) { buffer.Read(samples.AsSpan(offset, count)); return count; }
    }
}
