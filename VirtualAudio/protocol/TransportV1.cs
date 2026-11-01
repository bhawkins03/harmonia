// Explicit transport serialization; no struct marshalling or unsafe code.
using System.Buffers.Binary;
namespace Harmonia.Transport;
public enum Operation : ushort { Capabilities=1, Attach=2, ReadFrames=3, State=4, Detach=5 }
public enum Kind : ushort { Request=1, Response=2 }
public enum Status : uint { Ok, NoData, NotAttached, Busy, GenerationMismatch, UnsupportedFormat, Protected, Stopped, Removed, CounterExhausted }
public enum StreamState : ushort { Idle, Running, Paused, Stopped, Protected, Removed }
[Flags] public enum TransportFlags : uint { Discontinuity=1, GenerationChanged=2, Overflow=4, Disconnected=8, ProtectedContent=16, EstimatedTimestamp=32 }
public struct Message {
    public Operation Op;
    public Kind Type;
    public uint Flags;
    public Status Result;
    public ulong Session;
    public ulong Generation;
    public ulong Sequence;
    public ulong FirstFrame;
    public ulong Qpc;
    public ulong QpcFrequency;
    public ulong OverflowFrames;
    public ulong DisconnectedFrames;
    public ulong TimingLostFrames;
    public uint SampleRate;
    public ushort SampleFormat;
    public ushort Channels;
    public uint SpeakerMask;
    public ushort FrameBytes;
    public StreamState State;
    public uint FrameCount;
    public uint RequestedFrames;
    public uint PayloadBytes;
    public uint QueueCapacity;
    public uint FeatureBits;
    public uint FormatCount;
}
public static class TransportV1 {
    public const uint Magic=0x31544148, MaxFrames=480, CapacityFrames=8192, MaxPayload=11520, Features=7;
    public const ushort Version=1, HeaderBytes=160;
    public const int MaxMessage=11680;
    public const uint Discontinuity=1, GenerationChanged=2, Overflow=4, Disconnected=8, ProtectedContent=16, EstimatedTimestamp=32, KnownFlags=63;
    public static uint Ioctl(Operation op) => (0x8000U<<16) | (3U<<14) | ((0x800U+(uint)op-1)<<2) | (op==Operation.ReadFrames?2U:0U);
    static bool ValidFormat(Message m) => m.SampleFormat==1 && m.SampleRate==48000 && m.FrameBytes==m.Channels*4 &&
        ((m.Channels==2 && m.SpeakerMask==3) || (m.Channels==6 && (m.SpeakerMask==0x60f || m.SpeakerMask==0x3f)));
    static bool EmptyFormat(Message m) => (m.SampleRate|m.SampleFormat|m.Channels|m.SpeakerMask|m.FrameBytes)==0;
    static bool EmptySnapshot(Message m) => (m.Generation|m.Sequence|m.FirstFrame|m.Qpc|m.QpcFrequency|m.OverflowFrames|
        m.DisconnectedFrames|m.TimingLostFrames|m.Flags|m.FrameCount)==0 && m.State==StreamState.Idle && EmptyFormat(m);
    static ulong Get(ReadOnlySpan<byte> p,int o,int width) => width switch {
        2=>BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(o,2)),
        4=>BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(o,4)),
        8=>BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(o,8)), _=>throw new ArgumentOutOfRangeException(nameof(width)) };
    static void Put(Span<byte> p,int o,ulong v,int width) {
        switch(width) {
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(o,2),(ushort)v); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(o,4),(uint)v); break;
            case 8: BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(o,8),v); break;
            default: throw new ArgumentOutOfRangeException(nameof(width));
        }
    }
    static bool ValidCapabilities(ReadOnlySpan<byte> p) {
        if(p.Length!=48) return false;
        ReadOnlySpan<ushort> channels=[2,6,6]; ReadOnlySpan<uint> masks=[3,0x60f,0x3f];
        for(int i=0;i<3;i++) {
            int o=i*16;
            if(Get(p,o,4)!=48000 || Get(p,o+4,2)!=1 || Get(p,o+6,2)!=channels[i] ||
               Get(p,o+8,4)!=masks[i] || Get(p,o+12,2)!=(uint)channels[i]*4 || Get(p,o+14,2)!=0) return false;
        }
        return true;
    }
    static bool Validate(Message m,ReadOnlySpan<byte> p) {
        if((ushort)m.Op<1 || (ushort)m.Op>5 || (m.Type!=Kind.Request && m.Type!=Kind.Response) ||
            (uint)m.Result>9 || (ushort)m.State>5 || (m.Flags & ~KnownFlags)!=0 || p.Length>MaxPayload || m.PayloadBytes!=p.Length) return false;
        if((m.Flags & (GenerationChanged|Overflow|Disconnected))!=0 && (m.Flags & Discontinuity)==0) return false;
        if(m.Type==Kind.Request) {
            if(m.Result!=Status.Ok || !EmptySnapshot(m) || p.Length!=0 || m.QueueCapacity!=0 || m.FeatureBits!=0 || m.FormatCount!=0) return false;
            if(m.Op==Operation.Capabilities || m.Op==Operation.Attach) return m.Session==0 && m.RequestedFrames==0;
            return m.Session!=0 && m.RequestedFrames==0 && m.Op!=Operation.ReadFrames;
        }
        if(m.Result!=Status.Ok) return EmptySnapshot(m) && p.Length==0 && m.RequestedFrames==0 && m.QueueCapacity==0 &&
            m.FeatureBits==0 && m.FormatCount==0 && (m.Op!=Operation.Capabilities || m.Session==0);
        if(m.Op==Operation.Capabilities) return m.Session==0 && EmptySnapshot(m) && m.RequestedFrames==MaxFrames &&
            m.QueueCapacity==CapacityFrames && m.FeatureBits==Features && m.FormatCount==3 && ValidCapabilities(p);
        if(m.Session==0 || m.RequestedFrames!=0 || m.QueueCapacity!=0 || m.FeatureBits!=0 || m.FormatCount!=0) return false;
        if(m.Op==Operation.Detach) return EmptySnapshot(m) && p.Length==0;
        if(m.Generation==0) return m.Op!=Operation.ReadFrames && EmptySnapshot(m) && p.Length==0;
        if(!ValidFormat(m) || m.QpcFrequency==0) return false;
        if(m.State==StreamState.Idle || ((m.Flags & ProtectedContent)!=0)!=(m.State==StreamState.Protected)) return false;
        if(m.Op!=Operation.ReadFrames) return m.FrameCount==0 && p.Length==0;
        return m.State==StreamState.Running && (m.Flags & ProtectedContent)==0 && m.Sequence!=0 &&
            m.FrameCount>=1 && m.FrameCount<=MaxFrames && p.Length==m.FrameCount*m.FrameBytes && m.FirstFrame<=ulong.MaxValue-m.FrameCount;
    }
    public static bool ValidMessage(Message m,ReadOnlySpan<byte> p) {
        if(m.Type==Kind.Request && m.Op==Operation.ReadFrames) {
            if(m.Session==0 || m.Generation==0 || m.RequestedFrames<1 || m.RequestedFrames>MaxFrames) return false;
            m.Generation=0; m.RequestedFrames=0; m.Op=Operation.State;
        }
        return Validate(m,p);
    }
    public static bool ValidReply(Message request,Message reply,ReadOnlySpan<byte> payload) {
        if(!ValidMessage(request,[]) || !ValidMessage(reply,payload) || request.Type!=Kind.Request ||
            reply.Type!=Kind.Response || request.Op!=reply.Op) return false;
        if(request.Op!=Operation.Attach && reply.Session!=request.Session) return false;
        if(request.Op==Operation.Attach && reply.Result!=Status.Ok && reply.Session!=0) return false;
        return request.Op!=Operation.ReadFrames || reply.Result!=Status.Ok ||
            (reply.Generation==request.Generation && reply.FrameCount<=request.RequestedFrames);
    }
    // Payload and output must not overlap, matching the native API.
    public static bool TryEncode(Message m,ReadOnlySpan<byte> payload,Span<byte> output,out int written) {
        written=0;
        if(!ValidMessage(m,payload) || output.Length<HeaderBytes+payload.Length) return false;
        output[..HeaderBytes].Clear();
        Put(output,0,Magic,4); Put(output,4,Version,2); Put(output,6,HeaderBytes,2); Put(output,8,(uint)(HeaderBytes+payload.Length),4);
        Put(output,12,(ulong)m.Op,2);
        Put(output,14,(ulong)m.Type,2);
        Put(output,16,(ulong)m.Flags,4);
        Put(output,20,(ulong)m.Result,4);
        Put(output,24,(ulong)m.Session,8);
        Put(output,32,(ulong)m.Generation,8);
        Put(output,40,(ulong)m.Sequence,8);
        Put(output,48,(ulong)m.FirstFrame,8);
        Put(output,56,(ulong)m.Qpc,8);
        Put(output,64,(ulong)m.QpcFrequency,8);
        Put(output,72,(ulong)m.OverflowFrames,8);
        Put(output,80,(ulong)m.DisconnectedFrames,8);
        Put(output,88,(ulong)m.TimingLostFrames,8);
        Put(output,96,(ulong)m.SampleRate,4);
        Put(output,100,(ulong)m.SampleFormat,2);
        Put(output,102,(ulong)m.Channels,2);
        Put(output,104,(ulong)m.SpeakerMask,4);
        Put(output,108,(ulong)m.FrameBytes,2);
        Put(output,110,(ulong)m.State,2);
        Put(output,112,(ulong)m.FrameCount,4);
        Put(output,116,(ulong)m.RequestedFrames,4);
        Put(output,120,(ulong)m.PayloadBytes,4);
        Put(output,124,(ulong)m.QueueCapacity,4);
        Put(output,128,(ulong)m.FeatureBits,4);
        Put(output,132,(ulong)m.FormatCount,4);
        payload.CopyTo(output[HeaderBytes..]); written=HeaderBytes+payload.Length; return true;
    }
    public static bool TryDecode(ReadOnlySpan<byte> input,out Message result,out ReadOnlySpan<byte> payload) {
        result=default; payload=default;
        if(input.Length<HeaderBytes || input.Length>MaxMessage || Get(input,0,4)!=Magic || Get(input,4,2)!=Version ||
            Get(input,6,2)!=HeaderBytes || Get(input,8,4)!=(uint)input.Length) return false;
        foreach(byte b in input[136..160]) if(b!=0) return false;
        Message m=default;
        m.Op=(Operation)Get(input,12,2);
        m.Type=(Kind)Get(input,14,2);
        m.Flags=(uint)Get(input,16,4);
        m.Result=(Status)Get(input,20,4);
        m.Session=(ulong)Get(input,24,8);
        m.Generation=(ulong)Get(input,32,8);
        m.Sequence=(ulong)Get(input,40,8);
        m.FirstFrame=(ulong)Get(input,48,8);
        m.Qpc=(ulong)Get(input,56,8);
        m.QpcFrequency=(ulong)Get(input,64,8);
        m.OverflowFrames=(ulong)Get(input,72,8);
        m.DisconnectedFrames=(ulong)Get(input,80,8);
        m.TimingLostFrames=(ulong)Get(input,88,8);
        m.SampleRate=(uint)Get(input,96,4);
        m.SampleFormat=(ushort)Get(input,100,2);
        m.Channels=(ushort)Get(input,102,2);
        m.SpeakerMask=(uint)Get(input,104,4);
        m.FrameBytes=(ushort)Get(input,108,2);
        m.State=(StreamState)Get(input,110,2);
        m.FrameCount=(uint)Get(input,112,4);
        m.RequestedFrames=(uint)Get(input,116,4);
        m.PayloadBytes=(uint)Get(input,120,4);
        m.QueueCapacity=(uint)Get(input,124,4);
        m.FeatureBits=(uint)Get(input,128,4);
        m.FormatCount=(uint)Get(input,132,4);
        if(!ValidMessage(m,input[HeaderBytes..])) return false;
        result=m; payload=input[HeaderBytes..]; return true;
    }
}
public sealed class Continuity {
    bool seen; Message last;
    public bool Accept(Message m,ReadOnlySpan<byte> payload) {
        if(!TransportV1.ValidMessage(m,payload) || m.Type!=Kind.Response || m.Op!=Operation.ReadFrames || m.Result!=Status.Ok) return false;
        if(seen) {
            if(m.Session!=last.Session || m.Generation<last.Generation || m.Sequence<=last.Sequence ||
               m.QpcFrequency!=last.QpcFrequency || m.Qpc<last.Qpc || m.OverflowFrames<last.OverflowFrames ||
               m.DisconnectedFrames<last.DisconnectedFrames || m.TimingLostFrames<last.TimingLostFrames) return false;
            if(m.Generation!=last.Generation) {
                if((m.Flags & 3)!=3) return false;
            } else {
                if(m.SpeakerMask!=last.SpeakerMask || m.Channels!=last.Channels || m.FirstFrame<last.FirstFrame+last.FrameCount) return false;
                if((m.FirstFrame!=last.FirstFrame+last.FrameCount || m.Sequence!=last.Sequence+1) && (m.Flags & 1)==0) return false;
            }
        } else if((m.Flags & 1)==0) return false;
        last=m; seen=true; return true;
    }
}
