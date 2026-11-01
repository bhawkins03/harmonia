#include <ntifs.h>
#include <sysvad.h>
#include <wdmsec.h>

#include "HarmoniaTransport.h"
#include "../native/TransportQueue.h"
#include "EndpointsCommon/NewDelete.h"
#pragma code_seg()
using namespace harmonia::transport;
namespace {
constexpr ULONG Tag='tHmH';
// Private setup/security class GUID; standard KS category GUIDs are unchanged.
const GUID ControlClass={0x820d4501,0x6ec3,0x4ab0,{0x8c,0x78,0xc9,0x1f,0x0a,0xd9,0x17,0x14}};
UNICODE_STRING deviceName=RTL_CONSTANT_STRING(L"\\Device\\HarmoniaAudioTransport");
UNICODE_STRING linkName=RTL_CONSTANT_STRING(L"\\DosDevices\\HarmoniaAudioTransport");
UNICODE_STRING sddl=RTL_CONSTANT_STRING(L"D:P(A;;GA;;;SY)(A;;GRGW;;;S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259)");
struct File {
    EX_RUNDOWN_REF rundown{};
    volatile LONG busy=0;
    bool cleaned=false;
    PEPROCESS process=nullptr;
    u8 scratch[MaxPayload]{};
};
struct Context {
    KSPIN_LOCK lock{};
    KMUTEX lifecycle{};
    Queue queue;
    PDEVICE_OBJECT control=nullptr,adapter=nullptr;
    File* attached=nullptr;
    void* stream=nullptr;
    bool online=false,powered=false;
    USHORT channels=0;ULONG mask=0;
    PDRIVER_DISPATCH original[IRP_MJ_MAXIMUM_FUNCTION+1]{};
};
// One adapter/stream, lifetime from DriverEntry to DriverUnload. PnP removal
// gates/flushes it but never frees queue storage beneath a DPC or open handle.
Context* context=nullptr;
NTSTATUS Complete(PIRP irp,NTSTATUS status,ULONG_PTR bytes=0) {
    irp->IoStatus.Status=status;irp->IoStatus.Information=NT_SUCCESS(status)?bytes:0;
    IoCompleteRequest(irp,IO_NO_INCREMENT);return status;
}
NTSTATUS Create(PIRP irp,PIO_STACK_LOCATION stack) {
    auto file=stack->FileObject;
    if(KeGetCurrentIrql()!=PASSIVE_LEVEL || irp->RequestorMode!=UserMode || !file ||
        file->FileName.Length || (file->Flags&FO_REMOTE_ORIGIN) || stack->Parameters.Create.ShareAccess ||
        !stack->Parameters.Create.SecurityContext ||
        (stack->Parameters.Create.SecurityContext->DesiredAccess&(FILE_READ_DATA|FILE_WRITE_DATA))!=(FILE_READ_DATA|FILE_WRITE_DATA))
        return Complete(irp,STATUS_ACCESS_DENIED);
    auto state=new(POOL_FLAG_NON_PAGED,Tag) File();
    if(!state) return Complete(irp,STATUS_INSUFFICIENT_RESOURCES);
    ExInitializeRundownProtection(&state->rundown);
    state->process=IoGetRequestorProcess(irp);
    if(!state->process) {delete state;return Complete(irp,STATUS_ACCESS_DENIED);}
    ObReferenceObject(state->process);file->FsContext=state;
    return Complete(irp,STATUS_SUCCESS,FILE_OPENED);
}
NTSTATUS Control(PIRP irp,PIO_STACK_LOCATION stack,File* file) {
    if(KeGetCurrentIrql()!=PASSIVE_LEVEL || irp->RequestorMode!=UserMode || IoGetRequestorProcess(irp)!=file->process)
        return Complete(irp,STATUS_ACCESS_DENIED);
    NTSTATUS status=IoValidateDeviceIoControlAccess(irp,FILE_READ_DATA|FILE_WRITE_DATA);
    if(!NT_SUCCESS(status)) return Complete(irp,status);
    if(!ExAcquireRundownProtection(&file->rundown)) return Complete(irp,STATUS_FILE_CLOSED);
    if(InterlockedCompareExchange(&file->busy,1,0)) {ExReleaseRundownProtection(&file->rundown);return Complete(irp,STATUS_DEVICE_BUSY);}
    ULONG_PTR information=0;
    do {
        const auto& io=stack->Parameters.DeviceIoControl;
        // Reject METHOD_NEITHER/unknown codes BEFORE touching SystemBuffer.
        bool known=false;for(u16 op=1;op<=5;++op) if(io.IoControlCode==Ioctl(static_cast<Operation>(op))) known=true;
        if(!known) {status=STATUS_INVALID_DEVICE_REQUEST;break;}
        if(io.OutputBufferLength>MaxMessage) {status=STATUS_INVALID_BUFFER_SIZE;break;}
        if(io.InputBufferLength!=HeaderBytes || !irp->AssociatedIrp.SystemBuffer) {status=STATUS_INVALID_BUFFER_SIZE;break;}
        // Capture METHOD_BUFFERED input before sharing its buffer with output.
        u8 requestBytes[HeaderBytes];RtlCopyMemory(requestBytes,irp->AssociatedIrp.SystemBuffer,HeaderBytes);
        Message request{};const u8* payload=nullptr;
        if(!Decode(requestBytes,HeaderBytes,request,payload) || request.Type!=Kind::Request || io.IoControlCode!=Ioctl(request.Op)) {
            status=STATUS_INVALID_PARAMETER;break;
        }
        const ULONG need=request.Op==Operation::Capabilities?208:request.Op==Operation::ReadFrames?HeaderBytes+request.RequestedFrames*24:HeaderBytes;
        if(io.OutputBufferLength<need) {status=STATUS_BUFFER_TOO_SMALL;break;}
        u8* output=nullptr;
        if(request.Op==Operation::ReadFrames) {
            if(!irp->MdlAddress || MmGetMdlByteCount(irp->MdlAddress)<io.OutputBufferLength) {status=STATUS_INVALID_BUFFER_SIZE;break;}
            output=static_cast<u8*>(MmGetSystemAddressForMdlSafe(irp->MdlAddress,NormalPagePriority|MdlMappingNoExecute));
            if(!output) {status=STATUS_INSUFFICIENT_RESOURCES;break;}
        } else output=static_cast<u8*>(irp->AssociatedIrp.SystemBuffer);
        // All buffers are now owned/pinned nonpaged storage. No allocation,
        // mapping, waits, PortCls callbacks or completion occurs under this lock.
        KIRQL old;KeAcquireSpinLock(&context->lock,&old);
        if(file->cleaned) status=STATUS_FILE_CLOSED;
        else if(irp->Cancel) status=STATUS_CANCELLED;
        else {
            usize bytes=0;
            if(request.Op!=Operation::Capabilities && request.Op!=Operation::Attach && context->attached!=file) {
                Message denied{};denied.Op=request.Op;denied.Type=Kind::Response;denied.Session=request.Session;denied.Result=Status::NotAttached;
                status=Encode(denied,nullptr,0,output,io.OutputBufferLength,bytes)?STATUS_SUCCESS:STATUS_INTERNAL_ERROR;
            } else {
                const u64 oldSession=context->queue.Session();
                status=context->queue.Process(request,file->scratch,output,io.OutputBufferLength,bytes,
                    static_cast<u64>(KeQueryPerformanceCounter(nullptr).QuadPart))?STATUS_SUCCESS:STATUS_INTERNAL_ERROR;
                if(NT_SUCCESS(status)) {
                    if(!oldSession && context->queue.Session()) context->attached=file;
                    if(!context->queue.Session()) context->attached=nullptr;
                }
            }
            information=bytes;
        }
        KeReleaseSpinLock(&context->lock,old);
    } while(false);
    // Cancellation arriving after the locked transaction linearizes loses to
    // successful completion. Never return cancellation after consuming frames.
    InterlockedExchange(&file->busy,0);ExReleaseRundownProtection(&file->rundown);
    return Complete(irp,status,information);
}
_Dispatch_type_(IRP_MJ_CREATE)
_Dispatch_type_(IRP_MJ_CREATE_NAMED_PIPE)
_Dispatch_type_(IRP_MJ_CLOSE)
_Dispatch_type_(IRP_MJ_READ)
_Dispatch_type_(IRP_MJ_WRITE)
_Dispatch_type_(IRP_MJ_QUERY_INFORMATION)
_Dispatch_type_(IRP_MJ_SET_INFORMATION)
_Dispatch_type_(IRP_MJ_QUERY_EA)
_Dispatch_type_(IRP_MJ_SET_EA)
_Dispatch_type_(IRP_MJ_FLUSH_BUFFERS)
_Dispatch_type_(IRP_MJ_QUERY_VOLUME_INFORMATION)
_Dispatch_type_(IRP_MJ_SET_VOLUME_INFORMATION)
_Dispatch_type_(IRP_MJ_DIRECTORY_CONTROL)
_Dispatch_type_(IRP_MJ_FILE_SYSTEM_CONTROL)
_Dispatch_type_(IRP_MJ_DEVICE_CONTROL)
_Dispatch_type_(IRP_MJ_INTERNAL_DEVICE_CONTROL)
_Dispatch_type_(IRP_MJ_SHUTDOWN)
_Dispatch_type_(IRP_MJ_LOCK_CONTROL)
_Dispatch_type_(IRP_MJ_CLEANUP)
_Dispatch_type_(IRP_MJ_CREATE_MAILSLOT)
_Dispatch_type_(IRP_MJ_QUERY_SECURITY)
_Dispatch_type_(IRP_MJ_SET_SECURITY)
_Dispatch_type_(IRP_MJ_POWER)
_Dispatch_type_(IRP_MJ_SYSTEM_CONTROL)
_Dispatch_type_(IRP_MJ_DEVICE_CHANGE)
_Dispatch_type_(IRP_MJ_QUERY_QUOTA)
_Dispatch_type_(IRP_MJ_SET_QUOTA)
_Dispatch_type_(IRP_MJ_PNP)
DRIVER_DISPATCH Dispatch;
_Use_decl_annotations_
NTSTATUS Dispatch(PDEVICE_OBJECT device,PIRP irp) {
    auto stack=IoGetCurrentIrpStackLocation(irp);
    if(device->DeviceType!=DeviceType) {
        auto original=context->original[stack->MajorFunction];
        return original?original(device,irp):Complete(irp,STATUS_INVALID_DEVICE_REQUEST);
    }
    if(stack->MajorFunction==IRP_MJ_CREATE) return Create(irp,stack);
    auto file=stack->FileObject?static_cast<File*>(stack->FileObject->FsContext):nullptr;
    if(!file) return Complete(irp,STATUS_FILE_CLOSED);
    switch(stack->MajorFunction) {
    case IRP_MJ_CLEANUP: {
        KIRQL old;KeAcquireSpinLock(&context->lock,&old);file->cleaned=true;
        if(context->attached==file) {context->queue.Disconnect();context->attached=nullptr;}
        KeReleaseSpinLock(&context->lock,old);return Complete(irp,STATUS_SUCCESS);
    }
    case IRP_MJ_CLOSE:
        ExWaitForRundownProtectionRelease(&file->rundown);
        // Normal I/O-manager cleanup precedes close; defensively revoke here
        // as well so no opaque owner pointer can outlive its file context.
        {
            KIRQL old;KeAcquireSpinLock(&context->lock,&old);file->cleaned=true;
            if(context->attached==file) {context->queue.Disconnect();context->attached=nullptr;}
            KeReleaseSpinLock(&context->lock,old);
        }
        ObDereferenceObject(file->process);stack->FileObject->FsContext=nullptr;delete file;return Complete(irp,STATUS_SUCCESS);
    case IRP_MJ_DEVICE_CONTROL: return Control(irp,stack,file);
    default:return Complete(irp,STATUS_INVALID_DEVICE_REQUEST);
    }
}
}
_Use_decl_annotations_
NTSTATUS HarmoniaTransportInit(PDRIVER_OBJECT driver) {
    NT_ASSERT(KeGetCurrentIrql()==PASSIVE_LEVEL);context=new(POOL_FLAG_NON_PAGED,Tag) Context();
    if(!context) return STATUS_INSUFFICIENT_RESOURCES;
    KeInitializeSpinLock(&context->lock);
    KeInitializeMutex(&context->lifecycle,0);
    // WDM dispatch interposition is required because PortCls owns this driver
    // object; only our separate DeviceType is handled, all audio IRPs forward.
    for(ULONG i=0;i<=IRP_MJ_MAXIMUM_FUNCTION;++i) {
#pragma warning(suppress:28175) // Documented WDM MajorFunction dispatch registration.
        context->original[i]=driver->MajorFunction[i];
#pragma warning(suppress:28175) // Preserve/forward every PortCls dispatch entry.
        driver->MajorFunction[i]=Dispatch;
    }
    return STATUS_SUCCESS;
}
class TransportLifecycleGuard {
    KMUTEX* mutex;
public:
    _IRQL_requires_(PASSIVE_LEVEL) explicit TransportLifecycleGuard(KMUTEX* p):mutex(p) {
        KeWaitForSingleObject(mutex,Executive,KernelMode,FALSE,nullptr);
    }
    _IRQL_requires_(PASSIVE_LEVEL) ~TransportLifecycleGuard() {KeReleaseMutex(mutex,FALSE);}
};
_Use_decl_annotations_
NTSTATUS HarmoniaTransportStart(PDEVICE_OBJECT adapter) {
    NT_ASSERT(KeGetCurrentIrql()==PASSIVE_LEVEL);
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    const bool other=context->adapter && context->adapter!=adapter;
    KeReleaseSpinLock(&context->lock,old);if(other) return STATUS_DEVICE_BUSY;
    if(!context->control) {
        PDEVICE_OBJECT control=nullptr;
        NTSTATUS status=IoCreateDeviceSecure(adapter->DriverObject,0,&deviceName,DeviceType,FILE_DEVICE_SECURE_OPEN,
            TRUE,&sddl,&ControlClass,&control);
        if(!NT_SUCCESS(status)) return status;
        status=IoCreateSymbolicLink(&linkName,&deviceName);
        if(!NT_SUCCESS(status)) {IoDeleteDevice(control);return status;}
        context->control=control;control->Flags&=~DO_DEVICE_INITIALIZING;
    }
    KeAcquireSpinLock(&context->lock,&old);context->adapter=adapter;context->online=true;context->powered=true;
    KeReleaseSpinLock(&context->lock,old);return STATUS_SUCCESS;
}
_Use_decl_annotations_
void HarmoniaTransportStop(PDEVICE_OBJECT adapter,bool remove) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->adapter!=adapter) {KeReleaseSpinLock(&context->lock,old);return;}
    context->online=false;context->powered=false;context->stream=nullptr;context->queue.End(true);
    if(remove) context->adapter=nullptr;
    KeReleaseSpinLock(&context->lock,old);
    if(remove && context->control) {IoDeleteSymbolicLink(&linkName);IoDeleteDevice(context->control);context->control=nullptr;}
}
void HarmoniaTransportPower(PDEVICE_OBJECT adapter,bool on) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->adapter==adapter) {
        if(!on) context->queue.End(false);
        else if(!context->powered && context->stream) {
            LARGE_INTEGER frequency;KeQueryPerformanceCounter(&frequency);
            context->queue.Begin(context->channels,context->mask,static_cast<u64>(frequency.QuadPart));
        }
        context->powered=on;
    }
    KeReleaseSpinLock(&context->lock,old);
}
_Use_decl_annotations_
void HarmoniaTransportUnload() {
    NT_ASSERT(KeGetCurrentIrql()==PASSIVE_LEVEL);if(!context) return;
    if(context->control) {IoDeleteSymbolicLink(&linkName);IoDeleteDevice(context->control);}
    delete context;context=nullptr;
}
void HarmoniaTransportBegin(void* stream,USHORT channels,ULONG mask) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->online && context->powered && !context->stream) {
        LARGE_INTEGER frequency;KeQueryPerformanceCounter(&frequency);
        if(context->queue.Begin(channels,mask,static_cast<u64>(frequency.QuadPart))) {context->stream=stream;context->channels=channels;context->mask=mask;}
    }
    KeReleaseSpinLock(&context->lock,old);
}
void HarmoniaTransportRun(void* stream,bool run,bool paused) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->stream==stream && context->online && context->powered) {
        if(paused) context->queue.Pause();else context->queue.Run(run);
    }
    KeReleaseSpinLock(&context->lock,old);
}
void HarmoniaTransportEnd(void* stream) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->stream==stream) {context->queue.End(false);context->stream=nullptr;}
    KeReleaseSpinLock(&context->lock,old);
}
ULONGLONG HarmoniaTransportRevoke(void* stream) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);u64 ticket=0;
    if(context->stream==stream) ticket=context->queue.Revoke();
    KeReleaseSpinLock(&context->lock,old);return ticket;
}
void HarmoniaTransportGrant(void* stream,ULONGLONG ticket,bool eligible) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->stream==stream && context->online && context->powered) context->queue.Grant(ticket,eligible);
    KeReleaseSpinLock(&context->lock,old);
}
void HarmoniaTransportConsume(void* stream,const UCHAR* buffer,ULONG bytes,ULONG offset,ULONGLONG frames,bool committed,ULONGLONG qpc) {
    KIRQL old;KeAcquireSpinLock(&context->lock,&old);
    if(context->stream==stream && context->online && context->powered) context->queue.Consume(buffer,bytes,offset,frames,committed,qpc);
    KeReleaseSpinLock(&context->lock,old);
}
