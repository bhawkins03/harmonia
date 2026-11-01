using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;

namespace Harmonia.Suite.Service;

internal static class Program {
    public static void Main(string[] args) {
        if (args.Contains("--console")) {
            using var host = new ControlServer(); using var quit = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Cancel(); };
            host.Run(quit.Token).GetAwaiter().GetResult();
        } else ServiceBase.Run(new TransportService());
    }
}
internal sealed class TransportService : ServiceBase {
    private readonly ControlServer server = new();
    private readonly CancellationTokenSource quit = new();
    private Task? task;
    public TransportService() { ServiceName = "HarmoniaAudioTransport"; CanStop = true; }
    protected override void OnStart(string[] args) {
        task = server.Run(quit.Token);
        if (task.IsFaulted) task.GetAwaiter().GetResult();
        _ = task.ContinueWith(_ => Environment.Exit(1), TaskContinuationOptions.OnlyOnFaulted);
    }
    protected override void OnStop() { quit.Cancel(); task?.GetAwaiter().GetResult(); server.Dispose(); }
}
internal sealed class ControlServer : IDisposable {
    private readonly RoutingHost routing = new();
    private readonly ControllerLease lease = new();
    public async Task Run(CancellationToken token) {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        // Restricted token access must pass both its ordinary and restricted SID lists.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier("S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259"), PipeAccessRights.FullControl, AccessControlType.Allow));
        using var pipe = NamedPipeServerStreamAcl.Create(ControlWire.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
        var heartbeat = DateTime.UtcNow;
        while (!token.IsCancellationRequested) {
            bool connected = false;
            try {
                using var accept = CancellationTokenSource.CreateLinkedTokenSource(token); accept.CancelAfter(1000);
                await pipe.WaitForConnectionAsync(accept.Token);
                connected = true;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3));
                var request = await ControlWire.ReadAsync<ControlRequest>(pipe, deadline.Token);
                string? sid = null;
                pipe.RunAsClient(() => {
                    using var identity = WindowsIdentity.GetCurrent(true);
                    var interactive = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
                    var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
                    if (identity?.User is null || !identity.IsAuthenticated || identity.Groups is null ||
                        !identity.Groups.Contains(interactive) || identity.Groups.Contains(network))
                        throw new UnauthorizedAccessException("Local interactive users only.");
                    sid = identity.User.Value;
                });
                if (sid == lease.Owner) heartbeat = DateTime.UtcNow;
                ControlReply reply;
                PlaybackDevice[]? devices = null;
                try {
                    switch (request.Command) {
                        case "status": break;
                        case "devices": devices = routing.Devices(); break;
                        case "health": lease.Require(sid!); routing.VerifyDriver(); break;
                        case "probe": lease.Acquire(sid!); heartbeat = DateTime.UtcNow; routing.Probe(); break;
                        case "start":
                            if (request.Plan is null) throw new ArgumentException("Routing plan required.");
                            request.Plan.Validate(); lease.Acquire(sid!); heartbeat = DateTime.UtcNow; routing.Start(request.Plan); break;
                        case "stop": lease.Require(sid!); routing.Stop(); lease.Release(); break;
                        case "gains": lease.Require(sid!); routing.UpdateGains(request.Plan ?? throw new ArgumentException("Gains plan required.")); break;
                        default: throw new ArgumentException("Unknown command.");
                    }
                    reply = new(true, null, routing.Snapshot with { Devices = devices });
                } catch (Exception ex) { reply = new(false, ex.Message, routing.Snapshot); }
                await ControlWire.WriteAsync(pipe, reply, deadline.Token);
                // Disconnect discards unread pipe output. Wait for the client
                // to close after reading its reply, bounded against stalled peers.
                await pipe.ReadAsync(new byte[1], deadline.Token);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or OperationCanceledException) {
                if (pipe.IsConnected && ex is UnauthorizedAccessException) {
                    try {
                        using var timeout = new CancellationTokenSource(1000);
                        await ControlWire.WriteAsync(pipe, new ControlReply(false, ex.Message,
                            new(false, "Access denied", "unavailable", "unavailable", new float[6])), timeout.Token);
                        await pipe.ReadAsync(new byte[1], timeout.Token);
                    } catch (Exception denied) when (denied is IOException or OperationCanceledException) { }
                }
            }
            finally { if (connected) pipe.Disconnect(); }
            if (lease.Owner is not null && DateTime.UtcNow - heartbeat > TimeSpan.FromSeconds(15)) { routing.Stop(); lease.Release(); }
        }
    }
    public void Dispose() => routing.Dispose();
}
