#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winioctl.h>
#include <objbase.h>
#include <setupapi.h>
#include <ks.h>
#include <mmreg.h>
#include <ksmedia.h>
#include <iostream>
#include <vector>
#include <string>
#include <cstring>
int main(){
 HDEVINFO set=SetupDiGetClassDevsW(&KSCATEGORY_AUDIO,nullptr,nullptr,DIGCF_PRESENT|DIGCF_DEVICEINTERFACE);
 if(set==INVALID_HANDLE_VALUE)return 1;
 for(DWORD i=0;;++i){SP_DEVICE_INTERFACE_DATA item{};item.cbSize=sizeof(item);if(!SetupDiEnumDeviceInterfaces(set,nullptr,&KSCATEGORY_AUDIO,i,&item))break;
  DWORD size=0;SetupDiGetDeviceInterfaceDetailW(set,&item,nullptr,0,&size,nullptr);std::vector<BYTE> bytes(size);
  auto detail=reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(bytes.data());detail->cbSize=sizeof(*detail);
  if(!SetupDiGetDeviceInterfaceDetailW(set,&item,detail,size,nullptr,nullptr))continue;
  std::wstring path=detail->DevicePath;if(path.find(L"harmonia")==std::wstring::npos && path.find(L"Harmonia")==std::wstring::npos)continue;
  std::wcout<<L"Interface: "<<path<<L"\n";
  HANDLE h=CreateFileW(path.c_str(),GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,0,nullptr);
  if(h==INVALID_HANDLE_VALUE){std::cout<<"OPEN ERROR "<<GetLastError()<<"\n";continue;}
  for(ULONG pin=0;pin<2;++pin)for(ULONG id:{ULONG(KSPROPERTY_PIN_CATEGORY),ULONG(KSPROPERTY_PIN_DATARANGES),ULONG(KSPROPERTY_PIN_PHYSICALCONNECTION),ULONG(KSPROPERTY_PIN_DATAINTERSECTION)}){
   KSP_PIN request{};request.Property.Set=KSPROPSETID_Pin;request.Property.Id=id;request.Property.Flags=KSPROPERTY_TYPE_GET;request.PinId=pin;
   BYTE output[8192]{};DWORD got=0;BOOL ok=DeviceIoControl(h,IOCTL_KS_PROPERTY,&request,sizeof(request),output,sizeof(output),&got,nullptr);
   std::cout<<"pin="<<pin<<" property="<<id<<" ok="<<ok<<" error="<<(ok?0:GetLastError())<<" bytes="<<got<<"\n";
   if(ok && id==KSPROPERTY_PIN_PHYSICALCONNECTION){auto connection=reinterpret_cast<KSPIN_PHYSICALCONNECTION*>(output);std::wcout<<L"  connects pin "<<connection->Pin<<L" on "<<connection->SymbolicLinkName<<L"\n";}
   if(ok && id==KSPROPERTY_PIN_CATEGORY && got>=sizeof(GUID)){WCHAR text[40];StringFromGUID2(*reinterpret_cast<GUID*>(output),text,40);std::wcout<<L"  category "<<text<<L"\n";}
   if(ok && id==KSPROPERTY_PIN_DATARANGES && got>=sizeof(KSMULTIPLE_ITEM)){auto list=reinterpret_cast<KSMULTIPLE_ITEM*>(output);std::cout<<"  ranges="<<list->Count<<"\n";}
  }
  if(path.find(L"wave")!=std::wstring::npos){
    struct Intersection {KSP_PIN pin;KSMULTIPLE_ITEM list;KSDATARANGE_AUDIO range;};
    for(const GUID subtype:std::vector<GUID>{KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,KSDATAFORMAT_SUBTYPE_PCM,KSDATAFORMAT_SUBTYPE_WILDCARD}){
      Intersection request{};request.pin.Property.Set=KSPROPSETID_Pin;request.pin.Property.Id=KSPROPERTY_PIN_DATAINTERSECTION;request.pin.Property.Flags=KSPROPERTY_TYPE_GET;
      request.list.Count=1;request.list.Size=sizeof(request.list)+sizeof(request.range);
      request.range.DataRange.FormatSize=sizeof(request.range);request.range.DataRange.MajorFormat=KSDATAFORMAT_TYPE_AUDIO;request.range.DataRange.SubFormat=subtype;request.range.DataRange.Specifier=KSDATAFORMAT_SPECIFIER_WAVEFORMATEX;
      request.range.MaximumChannels=6;request.range.MinimumBitsPerSample=32;request.range.MaximumBitsPerSample=32;request.range.MinimumSampleFrequency=48000;request.range.MaximumSampleFrequency=48000;
      BYTE output[8192]{};DWORD got=0;BOOL ok=DeviceIoControl(h,IOCTL_KS_PROPERTY,&request,sizeof(request),output,sizeof(output),&got,nullptr);
      std::cout<<"intersection subtype="<<subtype.Data1<<" ok="<<ok<<" error="<<(ok?0:GetLastError())<<" bytes="<<got<<"\n";
    }
  }
  CloseHandle(h);
 }
 SetupDiDestroyDeviceInfoList(set);return 0;
}
