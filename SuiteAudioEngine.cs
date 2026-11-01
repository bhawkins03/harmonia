using System.IO.Pipes;
using System.Security.Principal;
using Harmonia.Suite;

namespace Harmonia;

internal sealed class SuiteAudioEngine : IDisposable {
    private readonly AudioEngine diagnostic = new();
    private readonly SemaphoreSlim requests = new(1);
    private readonly Func<(string Id, string Name)[]>? discovery;
    private ServiceStatus? snapshot;
    public bool Diagnostic { get; set; }
    public System.Collections.Concurrent.ConcurrentDictionary<SpeakerRole, float> Gains { get; } = new();
    private RoutingPlan? lastPlan;
    public SuiteAudioEngine(Func<(string Id, string Name)[]>? discovery = null) { this.discovery = discovery; diagnostic.GainProvider = role => Gains.GetValueOrDefault(role, 1); }
    public async Task ApplyGains() {
        if (!IsRunning || Diagnostic || lastPlan is null) return;
        lastPlan = lastPlan with { Outputs = lastPlan.Outputs.Select(o => o with { Gain = Gains.GetValueOrDefault(Enum.Parse<SpeakerRole>(o.Role), 1) }).ToArray() };
        await Send(new("gains", lastPlan));
    }
    public bool IsRunning { get; private set; }
    public string InputFormatDescription => Diagnostic ? diagnostic.InputFormatDescription : snapshot?.Detail ?? "Virtual driver service not connected";
    public string Versions => $"App {SuiteUpdates.AppVersion} | Service {snapshot?.ServiceVersion ?? "unavailable"} | Driver {snapshot?.DriverVersion ?? "unavailable"}";
    public string DefaultDeviceId => diagnostic.DefaultDeviceId;
    public (string Id, string Name)[] GetDevices() {
        if (discovery is not null) return discovery();
        if (Diagnostic) return diagnostic.GetDevices().Where(d => !d.Name.Contains("Harmonia", StringComparison.OrdinalIgnoreCase)).ToArray();
        var reply = Send(new("devices")).GetAwaiter().GetResult();
        return reply.Status.Devices?.Select(d => (d.Id, d.Name)).ToArray() ?? [];
    }
    public float[] GetSignalPeaks() => Diagnostic ? diagnostic.GetSignalPeaks() : snapshot?.Peaks ?? new float[6];
    public async Task<ControlReply> Send(ControlRequest request) {
        await requests.WaitAsync().ConfigureAwait(false);
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", ControlWire.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await ControlWire.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            var reply = await ControlWire.ReadAsync<ControlReply>(pipe, timeout.Token).ConfigureAwait(false);
            snapshot = reply.Status;
            if (!reply.Success) throw new IOException(reply.Error ?? "Service request failed.");
            return reply;
        } finally { requests.Release(); }
    }
    public async Task Poll() {
        if (Diagnostic) return;
        try {
            await Send(new("status"));
            if (IsRunning && lastPlan is not null && snapshot?.Detail == "Stopped") await Send(new("start", lastPlan));
        }
        catch (Exception ex) { snapshot = new(false, "Service unavailable: " + ex.Message, "unavailable", "unavailable", new float[6]); }
    }
    public void Start(SpeakerLayout layout, IReadOnlyDictionary<SpeakerRole, string> assignments, bool strictInput) {
        if (IsRunning) return;
        if (Diagnostic) diagnostic.Start(layout, assignments, strictInput);
        else {
            var plan = new RoutingPlan(layout.ToString(), AudioEngine.Roles(layout).Where(assignments.ContainsKey)
                .Select(r => new OutputRoute(r.ToString(), assignments[r], Gains.GetValueOrDefault(r, 1))).ToArray());
            Send(new("start", plan)).ConfigureAwait(false).GetAwaiter().GetResult();
            lastPlan = plan;
        }
        IsRunning = true;
    }
    public void Stop() {
        if (Diagnostic) diagnostic.Stop();
        else if (IsRunning) Send(new("stop")).ConfigureAwait(false).GetAwaiter().GetResult();
        IsRunning = false;
    }
    public void Dispose() { try { Stop(); } catch { } diagnostic.Dispose(); }
}
