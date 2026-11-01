using Harmonia.Transport;
using System.Buffers.Binary;
using static Harmonia.Transport.TransportV1;

static class Tests {
    static int checks;
    static void Check(bool ok) { checks++; if(!ok) throw new Exception($"protocol check {checks}"); }
    static Message Expected(string name,out byte[] p) {
        p=[]; Message m=new() { Type=Kind.Request, Op=name.StartsWith("capabilities")?Operation.Capabilities:
            name.StartsWith("attach")?Operation.Attach:name.StartsWith("state")?Operation.State:
            name.StartsWith("detach")?Operation.Detach:Operation.ReadFrames };
        if(name is "capabilities_request" or "attach_request") return m;
        m.Session=0x0102030405060708;
        if(name=="read_request") {m.Generation=7;m.RequestedFrames=480;return m;}
        if(name is "state_request" or "detach_request") return m;
        m.Type=Kind.Response;
        if(name=="capabilities_response") {
            m.Session=0;m.RequestedFrames=480;m.QueueCapacity=8192;m.FeatureBits=7;m.FormatCount=3;
            p=Convert.FromHexString("80bb000001000200030000000800000080bb0000010006000f0600001800000080bb0000010006003f00000018000000");
        } else if(name=="no_data") m.Result=Status.NoData;
        else if(name=="protected_error") m.Result=Status.Protected;
        else if(name is not ("attach_idle" or "detach_response")) {
            m.Generation=7;m.Sequence=9;m.FirstFrame=1000;m.Qpc=123456789;m.QpcFrequency=10000000;
            m.OverflowFrames=11;m.DisconnectedFrames=22;m.TimingLostFrames=33;
            m.SampleRate=48000;m.SampleFormat=1;m.Channels=6;m.SpeakerMask=0x60f;m.FrameBytes=24;
            m.State=StreamState.Running;m.Flags=3;
            if(name=="read_stereo") {
                m.Channels=2;m.SpeakerMask=3;m.FrameBytes=8;m.FrameCount=2;
                p=Convert.FromHexString("0000803f000080bf0000003f00000000");
            } else if(name is "read_side" or "read_back") {
                m.FrameCount=1;m.Flags=name=="read_side"?5U:9U;
                if(name=="read_back") m.SpeakerMask=0x3f;
                p=Convert.FromHexString("0000803f0000004000004040000080400000a0400000c040");
            }
        }
        m.PayloadBytes=(uint)p.Length;return m;
    }
    static void Reject(byte[] b) {
        Check(!TryDecode(b,out var m,out var p));Check(m.Session==0 && p.IsEmpty);
    }
    static void Mutate(byte[] b,int offset,int width,ulong value) {
        if(width==2) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset,2),(ushort)value);
        else if(width==4) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset,4),(uint)value);
        else BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(offset,8),value);
    }
    public static int Main(string[] args) {
        try {Run(args);return 0;} catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
    static void Run(string[] args) {
        Check(args.Length==2);
        using var encoded=new StreamWriter(Path.Combine(args[1],"managed-encoded.txt"));
        using var fuzz=new StreamWriter(Path.Combine(args[1],"managed-fuzz.txt"));
        var fixtures=new List<byte[]>(); byte[] stereo=[],request=[],cap=[];
        foreach(var line in File.ReadLines(args[0])) {
            if(line.Length==0 || line[0]=='#') continue;
            var pair=line.Split('|');var name=pair[0];var bytes=Convert.FromHexString(pair[1]);
            var expected=Expected(name,out var payload);var actual=new byte[MaxMessage];
            Check(TryEncode(expected,payload,actual,out int written));actual=actual[..written];Check(actual.SequenceEqual(bytes));
            Check(TryDecode(bytes,out var decoded,out var data));
            Check(decoded.Session==expected.Session && decoded.Generation==expected.Generation && decoded.SpeakerMask==expected.SpeakerMask);
            Check(data.SequenceEqual(payload));Check(TryEncode(decoded,data,actual,out written));Check(actual.SequenceEqual(bytes));
            encoded.WriteLine($"{name}|{Convert.ToHexString(actual).ToLowerInvariant()}");
            for(int n=0;n<bytes.Length;n++) Reject(bytes[..n]);
            Reject([..bytes,0]);
            for(int i=136;i<160;i++){var bad=(byte[])bytes.Clone();bad[i]=1;Reject(bad);}
            var shortOutput=Enumerable.Repeat((byte)0xcc,bytes.Length-1).ToArray();
            Check(!TryEncode(expected,payload,shortOutput,out written));Check(written==0 && shortOutput.All(b=>b==0xcc));
            if(name=="read_stereo") stereo=bytes;if(name=="read_request") request=bytes;if(name=="capabilities_response") cap=bytes;
            fixtures.Add(bytes);
        }
        Check(fixtures.Count==15);
        (int o,int w,ulong v)[] invalid=[(0,4,0),(4,2,2),(6,2,159),(8,4,0xffffffff),(12,2,6),(14,2,0),
            (16,4,64),(16,4,4),(20,4,10),(24,8,0),(32,8,0),(40,8,0),(48,8,ulong.MaxValue),
            (64,8,0),(96,4,44100),(100,2,2),(102,2,8),(104,4,0x60f),(108,2,4),(110,2,4),
            (112,4,481),(112,4,0xffffffff),(116,4,1),(120,4,0xffffffff),(124,4,8192)];
        foreach(var (o,w,v) in invalid){var bad=(byte[])stereo.Clone();Mutate(bad,o,w,v);Reject(bad);}
        foreach(uint frames in new uint[]{0,481,0xffffffff}) {var bad=(byte[])request.Clone();Mutate(bad,116,4,frames);Reject(bad);}
        {var bad=(byte[])cap.Clone();Mutate(bad,168,4,0x63f);Reject(bad);}
        foreach(ushort channels in new ushort[]{2,6}) {
            var m=Expected("read_stereo",out _);m.Channels=channels;m.SpeakerMask=channels==2?3U:0x60f;m.FrameBytes=(ushort)(channels*4);
            m.FrameCount=480;m.PayloadBytes=480U*m.FrameBytes;var p=new byte[m.PayloadBytes];var b=new byte[MaxMessage];
            Check(TryEncode(m,p,b,out int n));Check(TryDecode(b.AsSpan(0,n),out _,out _));
        }
        var message=Expected("read_stereo",out var pcm);var c=new Continuity();
        var rq=Expected("read_request",out _);Check(ValidReply(rq,message,pcm));
        var wrong=message;wrong.Session++;Check(!ValidReply(rq,wrong,pcm));
        wrong=message;wrong.Generation++;Check(!ValidReply(rq,wrong,pcm));
        rq.RequestedFrames=1;Check(!ValidReply(rq,message,pcm));rq.RequestedFrames=480;
        wrong=message;wrong.Op=Operation.State;wrong.FrameCount=0;wrong.PayloadBytes=0;Check(!ValidReply(rq,wrong,[]));
        Reject(new byte[MaxMessage+1]);
        var unmarked=message;unmarked.Flags=0;Check(!new Continuity().Accept(unmarked,pcm));
        Check(c.Accept(message,pcm));Check(!c.Accept(message,pcm));
        message.Sequence++;message.FirstFrame+=message.FrameCount;message.Qpc++;message.Flags=0;Check(c.Accept(message,pcm));
        var next=message;next.Sequence++;next.FirstFrame+=10;Check(!c.Accept(next,pcm));
        next.Flags=Discontinuity|Overflow;next.OverflowFrames++;Check(c.Accept(next,pcm));
        next.Generation++;next.Sequence++;next.FirstFrame=0;next.Flags=0;Check(!c.Accept(next,pcm));
        next.Flags=Discontinuity|GenerationChanged;Check(c.Accept(next,pcm));
        var regression=next;regression.Sequence++;regression.FirstFrame+=regression.FrameCount;regression.TimingLostFrames--;Check(!c.Accept(regression,pcm));
        regression=next;regression.Sequence++;regression.FirstFrame+=regression.FrameCount;regression.Generation--;Check(!c.Accept(regression,pcm));
        regression=next;regression.Sequence++;regression.FirstFrame+=regression.FrameCount;
        regression.SpeakerMask=0x3f;regression.Channels=6;regression.FrameBytes=24;regression.PayloadBytes=48;
        Check(!c.Accept(regression,new byte[48]));
        Check(Ioctl(Operation.Capabilities)==0x8000e000 && Ioctl(Operation.Attach)==0x8000e004 &&
            Ioctl(Operation.ReadFrames)==0x8000e00a && Ioctl(Operation.State)==0x8000e00c && Ioctl(Operation.Detach)==0x8000e010);
        uint seed=0x12345678;
        for(int i=0;i<10000;i++) {
            seed=unchecked(seed*1664525U+1013904223U);var b=(byte[])fixtures[(int)(seed%(uint)fixtures.Count)].Clone();
            seed=unchecked(seed*1664525U+1013904223U);int index=(int)(seed%(uint)b.Length);
            seed=unchecked(seed*1664525U+1013904223U);b[index]^=(byte)((seed>>24)|1);
            bool ok=TryDecode(b,out var d,out var payload);fuzz.WriteLine($"{i}|{(ok?1:0)}");
            if(ok){var output=new byte[b.Length];Check(TryEncode(d,payload,output,out _));Check(output.SequenceEqual(b));}
        }
        Console.WriteLine($"C# transport checks passed: {checks}; 15 vectors; 10000 deterministic mutations");
    }
}
