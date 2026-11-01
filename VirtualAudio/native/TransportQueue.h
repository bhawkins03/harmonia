// Nonpaged, allocation-free transport state machine. Caller serializes EVERY call.
// No OS pointers are retained. Exact same code is exercised by native tests.
#pragma once
#include "../protocol/TransportV1.h"
namespace harmonia::transport {
class Queue {
    u8 pcm[CapacityFrames][24]{};
    u64 frame[CapacityFrames]{}, tick[CapacityFrames]{};
    u32 head=0, count=0, pending=Discontinuity;
    u64 nextFrame=0, sessions=0, lastEnd=0, lastReadTick=0;
    bool active=false, running=false, paused=false, allowed=false, removed=false, exhausted=false, delivered=false;
    Message snapshot{};
    constexpr void Flush() noexcept { head=0;count=0;pending|=Discontinuity; }
    constexpr void Add(u64& counter,u64 n) noexcept {
        if(n>=~u64(0)-counter) {counter=~u64(0);exhausted=true;Flush();} else counter+=n;
    }
    constexpr void Advance(u64 n) noexcept { Add(nextFrame,n); }
    constexpr void Bump() noexcept {
        Flush();nextFrame=0;pending|=GenerationChanged;
        if(snapshot.Generation==~u64(0)) exhausted=true;else ++snapshot.Generation;
    }
    constexpr StreamState State() const noexcept {
        return removed?StreamState::Removed:!active?StreamState::Stopped:!running?(paused?StreamState::Paused:StreamState::Stopped):
            !allowed?StreamState::Protected:StreamState::Running;
    }
    constexpr Message Response(Operation op,u64 now) const noexcept {
        Message m=snapshot;m.Op=op;m.Type=Kind::Response;m.State=State();
        if(!m.Generation) {m={};m.Op=op;m.Type=Kind::Response;m.Session=snapshot.Session;return m;}
        m.FirstFrame=count?frame[head]:nextFrame;m.Qpc=now;m.Flags=pending;
        if(m.State==StreamState::Protected) m.Flags|=ProtectedContent;
        return m;
    }
    constexpr Status Error(const Message& r) const noexcept {
        if(r.Op==Operation::Capabilities) return Status::Ok;
        if(r.Op==Operation::Attach) return snapshot.Session?Status::Busy:exhausted?Status::CounterExhausted:Status::Ok;
        if(!snapshot.Session || r.Session!=snapshot.Session) return Status::NotAttached;
        if(r.Op!=Operation::ReadFrames) return Status::Ok;
        if(exhausted) return Status::CounterExhausted;
        if(removed) return Status::Removed;
        if(!active || !running) return Status::Stopped;
        if(!allowed) return Status::Protected;
        if(r.Generation!=snapshot.Generation) return Status::GenerationMismatch;
        if(!count) return Status::NoData;
        return Status::Ok;
    }
public:
    constexpr u64 Generation() const noexcept {return snapshot.Generation;}
    constexpr u32 Count() const noexcept {return count;}
    constexpr u64 Session() const noexcept {return snapshot.Session;}
    constexpr bool Begin(u16 channels,u32 mask,u64 frequency) noexcept {
        if(!harmonia::IsSupportedFormat(48000,channels,mask,32,true) || !frequency || frequency>~u64(0)/CapacityFrames) return false;
        if(snapshot.QpcFrequency && snapshot.QpcFrequency!=frequency) {exhausted=true;Flush();return false;}
        snapshot.SampleRate=48000;snapshot.SampleFormat=1;snapshot.Channels=channels;snapshot.SpeakerMask=mask;
        snapshot.FrameBytes=static_cast<u16>(channels*4);snapshot.QpcFrequency=frequency;
        active=true;running=false;paused=false;allowed=false;removed=false;Bump();return !exhausted;
    }
    constexpr void Run(bool value) noexcept {if(running!=value || paused){running=value;paused=false;Bump();}}
    constexpr void Pause() noexcept {if(running || !paused){running=false;paused=true;Bump();}}
    constexpr u64 Revoke() noexcept {allowed=false;if(active) Bump();else Flush();return snapshot.Generation;}
    constexpr void Grant(u64 ticket,bool eligible) noexcept {
        if(active && ticket==snapshot.Generation && eligible && !removed && !exhausted) {allowed=true;Bump();}
    }
    constexpr void End(bool removal) noexcept {allowed=false;running=false;paused=false;active=false;removed=removal;if(snapshot.Generation) Bump();else Flush();}
    constexpr void Disconnect() noexcept {
        if(snapshot.Session) {Add(snapshot.DisconnectedFrames,count);Flush();pending|=Disconnected;snapshot.Session=0;}
    }
    // Called only with whole frames consumed by the virtual DMA clock, before
    // publishing new position/notifications. No copy on an uncertain source.
    constexpr void Consume(const u8* cyclic,u32 cyclicBytes,u32 offset,u64 frames,bool committed,u64 firstTick) noexcept {
        if(!active || !running || removed || exhausted || !allowed || !frames) return;
        if(!snapshot.Session) {Add(snapshot.DisconnectedFrames,frames);Advance(frames);pending|=Discontinuity|Disconnected;return;}
        const u32 stride=snapshot.FrameBytes;
        if(!committed || !cyclic || !cyclicBytes || cyclicBytes%stride || offset>=cyclicBytes || offset%stride ||
            frames>MaxFrames || frames>cyclicBytes/stride) {
            Add(snapshot.TimingLostFrames,frames);Advance(frames);pending|=Discontinuity;return;
        }
        if(frames>~u64(0)-nextFrame || firstTick>~u64(0)-(frames*snapshot.QpcFrequency)/48000) {exhausted=true;Flush();return;}
        for(u32 i=0;i<static_cast<u32>(frames);++i) {
            if(count==CapacityFrames) {head=(head+1)%CapacityFrames;--count;Add(snapshot.OverflowFrames,1);pending|=Discontinuity|Overflow;}
            const u32 slot=(head+count)%CapacityFrames;
            for(u32 j=0;j<stride;++j) pcm[slot][j]=cyclic[(static_cast<u64>(offset)+i*stride+j)%cyclicBytes];
            frame[slot]=nextFrame+i;tick[slot]=firstTick+(static_cast<u64>(i)*snapshot.QpcFrequency)/48000;++count;
        }
        Advance(frames);
    }
    // Transaction: capacity/encoding errors change neither cursors nor sessions.
    // Scratch is caller-owned nonpaged MaxPayload bytes, distinct from output.
    constexpr bool Process(const Message& r,u8* scratch,u8* output,usize capacity,usize& written,u64 now) noexcept {
        written=0;
        if(!ValidMessage(r,nullptr,0) || r.Type!=Kind::Request || !scratch || !output) return false;
        const usize required=r.Op==Operation::Capabilities?208:r.Op==Operation::ReadFrames?HeaderBytes+r.RequestedFrames*24:HeaderBytes;
        if(capacity<required) return false;
        Message m{};m.Op=r.Op;m.Type=Kind::Response;m.Session=r.Session;m.Result=Error(r);
        if(m.Result!=Status::Ok) return Encode(m,nullptr,0,output,capacity,written);
        if(r.Op==Operation::Capabilities) {
            const u16 channels[]={2,6,6};const u32 masks[]={3,0x60f,0x3f};
            for(u32 i=0;i<48;++i) scratch[i]=0;
            for(u32 i=0;i<3;++i) {Put(scratch,i*16,48000,4);Put(scratch,i*16+4,1,2);Put(scratch,i*16+6,channels[i],2);
                Put(scratch,i*16+8,masks[i],4);Put(scratch,i*16+12,channels[i]*4U,2);}
            m.RequestedFrames=MaxFrames;m.QueueCapacity=CapacityFrames;m.FeatureBits=Features;m.FormatCount=3;m.PayloadBytes=48;
            return Encode(m,scratch,48,output,capacity,written);
        }
        if(r.Op==Operation::Attach) {
            if(sessions==~u64(0) || (active && snapshot.Generation==~u64(0))) {
                exhausted=true;m.Result=Status::CounterExhausted;return Encode(m,nullptr,0,output,capacity,written);
            }
            m=Response(r.Op,now);m.Session=sessions+1;m.Sequence=0;m.FirstFrame=0;
            if(active) {++m.Generation;m.Flags|=Discontinuity|GenerationChanged;}
            if(!Encode(m,nullptr,0,output,capacity,written)) return false;
            if(active) Bump();else Flush();snapshot.Session=++sessions;snapshot.Sequence=0;delivered=false;lastReadTick=0;return true;
        }
        if(r.Op==Operation::Detach) {
            if(!Encode(m,nullptr,0,output,capacity,written)) return false;Disconnect();return true;
        }
        m=Response(r.Op,now);
        if(r.Op==Operation::State) return Encode(m,nullptr,0,output,capacity,written);
        if(snapshot.Sequence==~u64(0)) {exhausted=true;m={};m.Op=r.Op;m.Type=Kind::Response;m.Session=r.Session;
            m.Result=Status::CounterExhausted;return Encode(m,nullptr,0,output,capacity,written);}
        u32 n=0;while(n<count && n<r.RequestedFrames && frame[(head+n)%CapacityFrames]==frame[head]+n) ++n;
        m.Sequence=snapshot.Sequence+1;m.FrameCount=n;m.PayloadBytes=n*m.FrameBytes;m.Qpc=tick[head];m.Flags|=EstimatedTimestamp;
        if(delivered && m.Qpc<lastReadTick) m.Qpc=lastReadTick;
        if(!delivered || m.FirstFrame!=lastEnd) m.Flags|=Discontinuity;
        for(u32 i=0;i<n;++i) for(u32 j=0;j<m.FrameBytes;++j) scratch[i*m.FrameBytes+j]=pcm[(head+i)%CapacityFrames][j];
        if(!Encode(m,scratch,m.PayloadBytes,output,capacity,written)) return false;
        head=(head+n)%CapacityFrames;count-=n;snapshot.Sequence=m.Sequence;lastEnd=m.FirstFrame+n;lastReadTick=m.Qpc;
        pending=0;delivered=true;return true;
    }
};
}
