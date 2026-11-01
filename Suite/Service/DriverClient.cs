using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Harmonia.Transport;

namespace Harmonia.Suite.Service;

internal sealed class DriverClient : IDisposable {
    private readonly SafeFileHandle handle;
    private ulong session;
    public DriverClient() {
        handle = CreateFile(@"\\.\HarmoniaAudioTransport", 0xc0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open Harmonia driver transport.");
        try {
            var attached = Call(Operation.Attach);
            if (attached.Message.Result != Status.Ok) throw new IOException("Driver attach: " + attached.Message.Result);
            session = attached.Message.Session;
        } catch { handle.Dispose(); throw; }
    }
    public (Message Message, byte[] Pcm) Call(Operation operation, ulong generation = 0) {
        var request = new Message { Op = operation, Type = Kind.Request,
            Session = session, Generation = generation,
            RequestedFrames = operation == Operation.ReadFrames ? TransportV1.MaxFrames : 0 };
        var input = new byte[TransportV1.HeaderBytes]; var output = new byte[TransportV1.MaxMessage];
        if (!TransportV1.TryEncode(request, [], input, out int count)) throw new InvalidDataException("Invalid driver request.");
        if (!DeviceIoControl(handle, TransportV1.Ioctl(operation), input, count, output, output.Length, out int returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (returned < 0 || returned > output.Length || !TransportV1.TryDecode(output.AsSpan(0, returned), out var message, out var payload) ||
            !TransportV1.ValidReply(request, message, payload)) throw new InvalidDataException("Invalid driver reply.");
        return (message, payload.ToArray());
    }
    public void Dispose() {
        if (!handle.IsClosed) { try { if (session != 0) Call(Operation.Detach); } catch { } handle.Dispose(); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
