#include "../protocol/TransportV1.h"
#include <fstream>
#include <iostream>
#include <string>
#include <vector>
#include <stdexcept>
#include <algorithm>
using namespace harmonia::transport;
static_assert(Ioctl(Operation::Capabilities)==0x8000e000 && Ioctl(Operation::Attach)==0x8000e004 &&
    Ioctl(Operation::ReadFrames)==0x8000e00a && Ioctl(Operation::State)==0x8000e00c && Ioctl(Operation::Detach)==0x8000e010);
static_assert((Ioctl(Operation::ReadFrames)&3)==2 && ((Ioctl(Operation::ReadFrames)>>14)&3)==3);
static_assert((Ioctl(Operation::Attach)&3)==0 && ((Ioctl(Operation::Capabilities)>>14)&3)==3);
static int checks=0;
static void Check(bool ok) { ++checks; if(!ok) throw std::runtime_error("protocol check " + std::to_string(checks)); }
static std::vector<u8> Hex(const std::string& s) {
    std::vector<u8> bytes;
    Check(s.size()%2==0);
    for(usize i=0;i<s.size();i+=2) bytes.push_back(static_cast<u8>(std::stoul(s.substr(i,2),nullptr,16)));
    return bytes;
}
static std::string Hex(const std::vector<u8>& v) {
    const char* digits="0123456789abcdef"; std::string s;
    for(auto b:v) { s+=digits[b>>4]; s+=digits[b&15]; } return s;
}
static Message Expected(const std::string& name,std::vector<u8>& p) {
    Message m{}; m.Type=Kind::Request;
    m.Op=name.find("capabilities")==0?Operation::Capabilities:
         name.find("attach")==0?Operation::Attach:
         name.find("state")==0?Operation::State:
         name.find("detach")==0?Operation::Detach:Operation::ReadFrames;
    if(name=="capabilities_request" || name=="attach_request") return m;
    m.Session=0x0102030405060708ULL;
    if(name=="read_request") { m.Generation=7; m.RequestedFrames=480; return m; }
    if(name=="state_request" || name=="detach_request") return m;
    m.Type=Kind::Response;
    if(name=="capabilities_response") {
        m.Session=0; m.RequestedFrames=480; m.QueueCapacity=8192; m.FeatureBits=7; m.FormatCount=3;
        p=Hex("80bb000001000200030000000800000080bb0000010006000f0600001800000080bb0000010006003f00000018000000");
    } else if(name=="no_data") m.Result=Status::NoData;
    else if(name=="protected_error") m.Result=Status::Protected;
    else if(name!="attach_idle" && name!="detach_response") {
        m.Generation=7; m.Sequence=9; m.FirstFrame=1000; m.Qpc=123456789; m.QpcFrequency=10000000;
        m.OverflowFrames=11; m.DisconnectedFrames=22; m.TimingLostFrames=33;
        m.SampleRate=48000; m.SampleFormat=1; m.Channels=6; m.SpeakerMask=0x60f; m.FrameBytes=24;
        m.State=StreamState::Running; m.Flags=3;
        if(name=="read_stereo") {
            m.Channels=2; m.SpeakerMask=3; m.FrameBytes=8; m.FrameCount=2;
            p=Hex("0000803f000080bf0000003f00000000");
        } else if(name=="read_side" || name=="read_back") {
            m.FrameCount=1; m.Flags=name=="read_side"?5U:9U;
            if(name=="read_back") m.SpeakerMask=0x3f;
            p=Hex("0000803f0000004000004040000080400000a0400000c040");
        }
    }
    m.PayloadBytes=static_cast<u32>(p.size()); return m;
}
static bool DecodeBytes(const std::vector<u8>& bytes) {
    Message m{}; const u8* p=nullptr; return Decode(bytes.data(),bytes.size(),m,p);
}
static void Reject(const std::vector<u8>& bytes) {
    Message m{}; m.Session=99; const u8* p=bytes.data();
    Check(!Decode(bytes.data(),bytes.size(),m,p)); Check(m.Session==0 && p==nullptr);
}
int main(int argc,char** argv) try {
    if(argc!=3) throw std::runtime_error("fixture path and output directory required");
    std::ifstream input(argv[1]); Check(input.good());
    std::ofstream encoded(std::string(argv[2])+"/native-encoded.txt");
    std::ofstream fuzz(std::string(argv[2])+"/native-fuzz.txt"); Check(encoded.good()&&fuzz.good());
    std::vector<std::vector<u8>> fixtures; std::vector<u8> stereo,request,cap;
    std::string line;
    while(std::getline(input,line)) {
        if(!line.empty() && line.back()=='\r') line.pop_back();
        if(line.empty() || line[0]=='#') continue;
        auto split=line.find('|'); auto name=line.substr(0,split); auto bytes=Hex(line.substr(split+1));
        std::vector<u8> payload; auto expected=Expected(name,payload);
        std::vector<u8> actual(MaxMessage,0xcc); usize written=99;
        Check(Encode(expected,payload.data(),payload.size(),actual.data(),actual.size(),written));
        actual.resize(written); Check(actual==bytes);
        Message decoded{}; const u8* data=nullptr;
        Check(Decode(bytes.data(),bytes.size(),decoded,data));
        Check(decoded.Session==expected.Session && decoded.Generation==expected.Generation && decoded.SpeakerMask==expected.SpeakerMask);
        Check(std::equal(payload.begin(),payload.end(),data));
        Check(Encode(decoded,data,decoded.PayloadBytes,actual.data(),actual.size(),written)); Check(actual==bytes);
        encoded<<name<<'|'<<Hex(actual)<<'\n';
        for(usize n=0;n<bytes.size();++n) Reject(std::vector<u8>(bytes.begin(),bytes.begin()+n));
        auto extra=bytes;extra.push_back(0);Reject(extra);
        for(usize i=136;i<160;++i) { auto bad=bytes;bad[i]=1;Reject(bad); }
        std::vector<u8> shortOutput(bytes.size()-1,0xcc);
        Check(!Encode(expected,payload.data(),payload.size(),shortOutput.data(),shortOutput.size(),written));
        Check(written==0 && std::all_of(shortOutput.begin(),shortOutput.end(),[](u8 v){return v==0xcc;}));
        if(name=="read_stereo") stereo=bytes;
        if(name=="read_request") request=bytes;
        if(name=="capabilities_response") cap=bytes;
        fixtures.push_back(bytes);
    }
    Check(fixtures.size()==15);
    struct Mutation { usize offset,width; u64 value; };
    const Mutation invalid[]={{0,4,0},{4,2,2},{6,2,159},{8,4,0xffffffff},{12,2,6},{14,2,0},
        {16,4,64},{16,4,4},{20,4,10},{24,8,0},{32,8,0},{40,8,0},{48,8,~u64(0)},
        {64,8,0},{96,4,44100},{100,2,2},{102,2,8},{104,4,0x60f},{108,2,4},{110,2,4},
        {112,4,481},{112,4,0xffffffff},{116,4,1},{120,4,0xffffffff},{124,4,8192}};
    for(auto v:invalid) {auto bad=stereo;Put(bad.data(),v.offset,v.value,v.width);Reject(bad);}
    for(auto frames:{0U,481U,0xffffffffU}) {auto bad=request;Put(bad.data(),116,frames,4);Reject(bad);}
    {auto bad=cap;Put(bad.data(),168,0x63f,4);Reject(bad);}
    for(u16 channels:{u16(2),u16(6)}) {
        std::vector<u8> p;auto m=Expected("read_stereo",p);
        m.Channels=channels;m.SpeakerMask=channels==2?3:0x60f;m.FrameBytes=static_cast<u16>(channels*4);
        m.FrameCount=480;m.PayloadBytes=480*m.FrameBytes;p.assign(m.PayloadBytes,0);
        std::vector<u8> b(MaxMessage);usize n=0;
        Check(Encode(m,p.data(),p.size(),b.data(),b.size(),n));b.resize(n);Check(DecodeBytes(b));
    }
    std::vector<u8> p;auto m=Expected("read_stereo",p);Continuity c;
    std::vector<u8> ignored; auto rq=Expected("read_request",ignored);
    Check(ValidReply(rq,m,p.data(),p.size()));
    auto wrong=m;wrong.Session++;Check(!ValidReply(rq,wrong,p.data(),p.size()));
    wrong=m;wrong.Generation++;Check(!ValidReply(rq,wrong,p.data(),p.size()));
    rq.RequestedFrames=1;Check(!ValidReply(rq,m,p.data(),p.size()));rq.RequestedFrames=480;
    wrong=m;wrong.Op=Operation::State;wrong.FrameCount=0;wrong.PayloadBytes=0;Check(!ValidReply(rq,wrong,nullptr,0));
    {Message empty{};const u8* bytes=nullptr;Check(!Decode(nullptr,160,empty,bytes));usize n=5;
     Check(!Encode(m,p.data(),p.size(),nullptr,MaxMessage,n));Check(n==0);}
    {std::vector<u8> huge(MaxMessage+1);Reject(huge);}
    auto unmarked=m;unmarked.Flags=0;Continuity initial;Check(!initial.Accept(unmarked,p.data(),p.size()));
    Check(c.Accept(m,p.data(),p.size()));Check(!c.Accept(m,p.data(),p.size()));
    m.Sequence++;m.FirstFrame+=m.FrameCount;m.Qpc++;m.Flags=0;Check(c.Accept(m,p.data(),p.size()));
    auto next=m;next.Sequence++;next.FirstFrame+=10;Check(!c.Accept(next,p.data(),p.size()));
    next.Flags=Discontinuity|Overflow;next.OverflowFrames++;Check(c.Accept(next,p.data(),p.size()));
    next.Generation++;next.Sequence++;next.FirstFrame=0;next.Flags=0;Check(!c.Accept(next,p.data(),p.size()));
    next.Flags=Discontinuity|GenerationChanged;Check(c.Accept(next,p.data(),p.size()));
    auto bad=next;bad.Sequence++;bad.FirstFrame+=bad.FrameCount;bad.TimingLostFrames--;Check(!c.Accept(bad,p.data(),p.size()));
    bad=next;bad.Sequence++;bad.FirstFrame+=bad.FrameCount;bad.Generation--;Check(!c.Accept(bad,p.data(),p.size()));
    bad=next;bad.Sequence++;bad.FirstFrame+=bad.FrameCount;bad.SpeakerMask=0x3f;bad.Channels=6;bad.FrameBytes=24;bad.PayloadBytes=48;
    // Format changes without a generation bump are rejected even with valid bytes.
    std::vector<u8> changedFormat(48);Check(!c.Accept(bad,changedFormat.data(),changedFormat.size()));
    u32 seed=0x12345678;
    for(int i=0;i<10000;++i) {
        seed=seed*1664525U+1013904223U;auto b=fixtures[seed%fixtures.size()];
        seed=seed*1664525U+1013904223U;auto index=seed%b.size();
        seed=seed*1664525U+1013904223U;b[index]^=static_cast<u8>((seed>>24)|1);
        bool ok=DecodeBytes(b);fuzz<<i<<'|'<<(ok?1:0)<<'\n';
        if(ok) {Message d{};const u8* payload=nullptr;Check(Decode(b.data(),b.size(),d,payload));std::vector<u8> out(b.size());usize n;
            Check(Encode(d,payload,d.PayloadBytes,out.data(),out.size(),n));Check(out==b);}
    }
    std::cout<<"C++ transport checks passed: "<<checks<<"; 15 vectors; 10000 deterministic mutations\n";
    return 0;
} catch(const std::exception& e) {std::cerr<<e.what()<<'\n';return 1;}
