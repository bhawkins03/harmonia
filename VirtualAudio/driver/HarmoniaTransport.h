#pragma once
#include <ntddk.h>
// Nonpageable code. Lifecycle entry points require PASSIVE; streaming max DISPATCH.
_IRQL_requires_(PASSIVE_LEVEL)
NTSTATUS HarmoniaTransportInit(PDRIVER_OBJECT driver);
_IRQL_requires_(PASSIVE_LEVEL)
NTSTATUS HarmoniaTransportStart(PDEVICE_OBJECT adapter);
_IRQL_requires_(PASSIVE_LEVEL)
void HarmoniaTransportStop(PDEVICE_OBJECT adapter,bool remove);
void HarmoniaTransportPower(PDEVICE_OBJECT adapter,bool on);
_IRQL_requires_(PASSIVE_LEVEL)
void HarmoniaTransportUnload();
void HarmoniaTransportBegin(void* stream,USHORT channels,ULONG mask);
void HarmoniaTransportRun(void* stream,bool run,bool paused=false);
void HarmoniaTransportEnd(void* stream);
ULONGLONG HarmoniaTransportRevoke(void* stream);
void HarmoniaTransportGrant(void* stream,ULONGLONG ticket,bool eligible);
void HarmoniaTransportConsume(void* stream,const UCHAR* buffer,ULONG bytes,ULONG offset,
    ULONGLONG frames,bool committed,ULONGLONG qpc);
