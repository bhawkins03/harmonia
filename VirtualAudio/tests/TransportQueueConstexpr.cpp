// Exercises the actual production state machine even where Application Control
// prevents execution of freshly built native test programs. No policy changes.
#include "../native/TransportQueue.h"
using namespace harmonia::transport;
constexpr bool QueueProof(u16 channels,u32 mask) {
    Queue q{};u8 scratch[MaxPayload]{},out[MaxMessage]{},source[480*24]{};usize n=0;
    for(u32 i=0;i<480*24;++i) source[i]=static_cast<u8>(i*17+3);
    Message request{};request.Type=Kind::Request;request.Op=Operation::Attach;
    if(!q.Process(request,scratch,out,sizeof(out),n,1) || !q.Session()) return false;
    if(!q.Begin(channels,mask,48000)) return false;
    q.Run(true);auto ticket=q.Revoke();q.Grant(ticket,true);
    const u32 stride=channels*4;
    q.Consume(source,480*stride,479*stride,4,true,1000);
    request.Op=Operation::ReadFrames;request.Session=q.Session();request.Generation=q.Generation();request.RequestedFrames=480;
    if(!q.Process(request,scratch,out,sizeof(out),n,2000) || Get(out,112,4)!=4) return false;
    for(u32 i=0;i<4*stride;++i) if(out[HeaderBytes+i]!=source[(479*stride+i)%(480*stride)]) return false;
    for(u32 i=0;i<18;++i) q.Consume(source,480*stride,0,480,true,5000+i*480);
    if(q.Count()!=8192) return false;
    auto count=q.Count();if(q.Process(request,scratch,out,160,n,50000) || q.Count()!=count) return false;
    if(!q.Process(request,scratch,out,sizeof(out),n,50000) || Get(out,72,8)!=448 || Get(out,48,8)!=452) return false;
    q.Consume(nullptr,0,0,17,false,50000);
    request.Op=Operation::State;request.Generation=0;request.RequestedFrames=0;
    if(!q.Process(request,scratch,out,sizeof(out),n,50000) || Get(out,88,8)!=17) return false;
    ticket=q.Revoke();request.Op=Operation::ReadFrames;request.Generation=q.Generation();request.RequestedFrames=1;
    if(!q.Process(request,scratch,out,sizeof(out),n,50000) || Get(out,20,4)!=static_cast<u32>(Status::Protected)) return false;
    q.Run(false);q.Grant(ticket,true);q.Run(true);request.Generation=q.Generation();
    if(!q.Process(request,scratch,out,sizeof(out),n,50000) || Get(out,20,4)!=static_cast<u32>(Status::Protected)) return false;
    q.End(true);request.Generation=q.Generation();
    return q.Process(request,scratch,out,sizeof(out),n,50000) && Get(out,20,4)==static_cast<u32>(Status::Removed) && q.Count()==0;
}
static_assert(QueueProof(2,3),"stereo bounded queue and fail-closed lifecycle");
static_assert(QueueProof(6,0x60f),"side 5.1 exact ordering");
static_assert(QueueProof(6,0x3f),"back 5.1 exact ordering");
