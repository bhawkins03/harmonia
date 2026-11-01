#include "../native/TransportQueue.h"
#include <memory>
#include <vector>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>
#include <mutex>
#include <atomic>
using namespace harmonia::transport;
static int checks;
static void Check(bool value) {if(!value) throw std::runtime_error("queue check "+std::to_string(checks));++checks;}
struct Harness {
    std::unique_ptr<Queue> queue=std::make_unique<Queue>();
    u8 scratch[MaxPayload]{},out[MaxMessage]{};
    u64 session=0;
    Message Call(Operation op,u32 frames=0,u64 gen=0,usize capacity=MaxMessage) {
        Message r{};r.Op=op;r.Type=Kind::Request;r.Session=op==Operation::Attach||op==Operation::Capabilities?0:session;
        r.Generation=gen;r.RequestedFrames=frames;usize n=0;
        Check(queue->Process(r,scratch,out,capacity,n,100000));Message m{};const u8* p=nullptr;
        Check(Decode(out,n,m,p));Check(ValidReply(r,m,p,n-HeaderBytes));
        if(op==Operation::Attach && m.Result==Status::Ok) session=m.Session;return m;
    }
    void Start(u16 channels,u32 mask) {
        Check(queue->Begin(channels,mask,10000000));queue->Run(true);auto ticket=queue->Revoke();queue->Grant(ticket,true);
    }
};
int main() try {
    for(auto mask:{3U,0x60fU,0x3fU}) {
        auto h=std::make_unique<Harness>();u16 channels=mask==3?2:6;u32 stride=channels*4;
        Check(h->Call(Operation::Capabilities).FormatCount==3);Check(h->Call(Operation::Attach).Generation==0);
        Check(h->Call(Operation::Attach).Result==Status::Busy);h->Start(channels,mask);
        std::vector<u8> source(480*stride);
        for(u32 i=0;i<source.size();++i) source[i]=static_cast<u8>(i*17+3);
        // Wrap with all bytes distinct across channels; PCM is never interpreted/mixed.
        h->queue->Consume(source.data(),static_cast<u32>(source.size()),479*stride,4,true,1000);
        auto m=h->Call(Operation::ReadFrames,480,h->queue->Generation());Check(m.FrameCount==4 && m.SpeakerMask==mask);
        for(u32 i=0;i<4*stride;++i) Check(h->out[HeaderBytes+i]==source[(479*stride+i)%source.size()]);
        Check((m.Flags&Discontinuity)!=0);Check(h->Call(Operation::ReadFrames,1,h->queue->Generation()).Result==Status::NoData);
        // Overflow is exact (8192 frames), oldest complete frames disappear.
        for(u32 i=0;i<18;++i) h->queue->Consume(source.data(),static_cast<u32>(source.size()),0,480,true,10000+i*100000);
        Check(h->queue->Count()==8192);m=h->Call(Operation::State);Check(m.OverflowFrames==448);
        m=h->Call(Operation::ReadFrames,480,h->queue->Generation());Check(m.FirstFrame==452 && (m.Flags&Overflow));
        // Encoder/output rejection and cancellation-before-dispatch must not consume.
        Message r{};r.Op=Operation::ReadFrames;r.Type=Kind::Request;r.Session=h->session;r.Generation=h->queue->Generation();r.RequestedFrames=480;
        usize n=99;auto count=h->queue->Count();Check(!h->queue->Process(r,h->scratch,h->out,160,n,500));Check(n==0 && h->queue->Count()==count);
        auto prior=h->Call(Operation::State);r.Session++;
        Check(h->queue->Process(r,h->scratch,h->out,MaxMessage,n,500));Check(h->queue->Count()==count);Check(h->Call(Operation::State).Sequence==prior.Sequence);
        // Uncommitted, huge delayed and invalid cyclic spans become timing loss.
        h->queue->Consume(nullptr,0,0,5,false,1000000);
        h->queue->Consume(source.data(),static_cast<u32>(source.size()),0,100000,true,1000000);
        Check(h->Call(Operation::State).TimingLostFrames==100005);
        h->Call(Operation::Detach);h->queue->Consume(nullptr,0,0,7,false,1000000);
        auto attach=h->Call(Operation::Attach);Check(attach.DisconnectedFrames==h->queue->Count()+count+7);
        Check(h->queue->Count()==0);Check(h->Call(Operation::ReadFrames,1,attach.Generation).Result==Status::NoData);
        auto old=attach.Generation;h->queue->Run(false);h->queue->Run(true);
        Check(h->Call(Operation::ReadFrames,1,old).Result==Status::GenerationMismatch);
        // Rights must be positively regranted; stale rights tickets cannot revive export.
        auto ticket=h->queue->Revoke();h->queue->Consume(source.data(),static_cast<u32>(source.size()),0,1,true,2000000);
        Check(h->queue->Count()==0);Check(h->Call(Operation::ReadFrames,1,h->queue->Generation()).Result==Status::Protected);
        h->queue->Run(false);h->queue->Grant(ticket,true);h->queue->Run(true);
        Check(h->Call(Operation::ReadFrames,1,h->queue->Generation()).Result==Status::Protected);
        ticket=h->queue->Revoke();h->queue->Grant(ticket,true);
        h->queue->Consume(source.data(),static_cast<u32>(source.size()),0,2,true,3000000);
        m=h->Call(Operation::ReadFrames,2,h->queue->Generation());Check(m.FrameCount==2 && (m.Flags&GenerationChanged));
        h->queue->End(true);Check(h->Call(Operation::ReadFrames,1,h->queue->Generation()).Result==Status::Removed);
        Check(h->queue->Count()==0);h->queue->Grant(ticket,true);Check(h->queue->Count()==0);
    }
    {
        auto h=std::make_unique<Harness>();h->Call(Operation::Attach);h->Start(2,3);
        h->queue->Pause();Check(h->Call(Operation::State).State==StreamState::Paused);
        Check(h->Call(Operation::ReadFrames,1,h->queue->Generation()).Result==Status::Stopped);
        h->queue->Run(true);h->Call(Operation::Detach);
        h->queue->Consume(nullptr,0,0,~u64(0),false,1000);
        Check(h->Call(Operation::Attach).Result==Status::CounterExhausted);
    }
    {auto q=std::make_unique<Queue>();Check(!q->Begin(8,0x63f,1000));Check(!q->Begin(6,3,1000));Check(!q->Begin(2,3,0));}
    {
        // Model the driver's single-lock linearization. This tests the exact
        // queue core, not Windows spinlocks/IRP cancellation or Driver Verifier.
        auto h=std::make_unique<Harness>();h->Call(Operation::Attach);h->Start(6,0x60f);
        std::mutex lock;std::atomic<bool> done=false;u32 received=0;Continuity continuity;
        std::thread producer([&] {
            std::vector<u8> source(160*24,0x6b);
            for(u32 i=0;i<1000;++i) {
                std::lock_guard<std::mutex> held(lock);
                if(i%37==0) {h->queue->End(false);h->queue->Begin(6,0x60f,10000000);h->queue->Run(true);auto ticket=h->queue->Revoke();h->queue->Grant(ticket,true);}
                h->queue->Consume(source.data(),static_cast<u32>(source.size()),0,160,true,10000+i*100000ULL);
            }
            done=true;
        });
        for(;;) {
            bool finish=false;
            {
                std::lock_guard<std::mutex> held(lock);
                auto m=h->Call(Operation::ReadFrames,480,h->queue->Generation());
                if(m.Result==Status::Ok) {Check(continuity.Accept(m,h->out+HeaderBytes,m.PayloadBytes));received+=m.FrameCount;}
                else Check(m.Result==Status::NoData);
                Check(h->queue->Count()<=8192);finish=done && h->queue->Count()==0;
            }
            if(finish) break;std::this_thread::yield();
        }
        producer.join();Check(received>0);
    }
    std::cout<<"Native transport queue passed: "<<checks<<" checks (stereo/side/back, wrap, overflow, transaction, disconnect, rights, removal)\n";
    return 0;
} catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
