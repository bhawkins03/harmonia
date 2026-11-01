using System.Buffers.Binary;
using System.Text.Json;

namespace Harmonia.Suite;

public sealed record OutputRoute(string Role, string DeviceId, float Gain = 1);
public sealed record RoutingPlan(string Layout, OutputRoute[] Outputs) {
    public void Validate() {
        string[] roles = Layout switch {
            "DualStereo" => ["FrontLeft", "FrontRight"],
            "TripleStereoCenter" => ["FrontLeft", "FrontRight", "Center"],
            "Surround51Beta" => ["FrontLeft", "FrontRight", "Center", "Subwoofer", "RearStereo"],
            _ => throw new ArgumentException("Unknown speaker layout.")
        };
        if (Outputs is null || Outputs.Length < 2 || Outputs.Length > 5 ||
            Outputs.Any(o => o is null || !roles.Contains(o.Role) || string.IsNullOrWhiteSpace(o.DeviceId) ||
                o.DeviceId.Length > 1024 || !float.IsFinite(o.Gain) || o.Gain < 0 || o.Gain > 2) ||
            Outputs.Select(o => o.Role).Distinct().Count() != Outputs.Length ||
            Outputs.Select(o => o.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Outputs.Length ||
            roles.Where(r => r != "Subwoofer").Any(r => !Outputs.Any(o => o.Role == r)))
            throw new ArgumentException("Assign distinct physical devices to each required role; gains must be 0–2.");
    }
}
public sealed record ControlRequest(string Command, RoutingPlan? Plan = null);
public sealed record ServiceStatus(bool Running, string Detail, string ServiceVersion,
    string DriverVersion, float[] Peaks, ulong Frames = 0, ulong Discontinuities = 0,
    ulong OverflowFrames = 0, long Underruns = 0, double MaximumDriftPpm = 0);
public sealed record ControlReply(bool Success, string? Error, ServiceStatus Status);

public static class ControlWire {
    public const string PipeName = "Harmonia.Suite.Control.v1";
    public const int MaximumBytes = 32768;
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Control message too large.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token) {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 2 || size > MaximumBytes) throw new InvalidDataException("Invalid control length.");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty control request.");
    }
}

// One controller lease: another desktop user cannot modify an active route.
public sealed class ControllerLease {
    public string? Owner { get; private set; }
    public void Acquire(string sid) {
        if (Owner is not null && Owner != sid) throw new UnauthorizedAccessException("Routing belongs to another signed-in user.");
        Owner = sid;
    }
    public void Require(string sid) {
        if (Owner is not null && Owner != sid) throw new UnauthorizedAccessException("Routing belongs to another signed-in user.");
    }
    public void Release() => Owner = null;
}
