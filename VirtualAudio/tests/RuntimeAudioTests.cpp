#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <endpointvolume.h>
#include <functiondiscoverykeys_devpkey.h>
#include <ksmedia.h>
#include <wrl/client.h>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
#include <cstring>
using Microsoft::WRL::ComPtr;
static void Hr(HRESULT hr,const char* name){if(FAILED(hr))throw std::runtime_error(std::string(name)+" HRESULT="+std::to_string(static_cast<unsigned long>(hr)));}
int main() try {
    Hr(CoInitializeEx(nullptr,COINIT_MULTITHREADED),"COM init");
    ComPtr<IMMDeviceEnumerator> enumerator;Hr(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)),"enumerator");
    ComPtr<IMMDeviceCollection> devices;Hr(enumerator->EnumAudioEndpoints(eRender,DEVICE_STATE_ACTIVE,&devices),"render endpoints");
    UINT count=0;Hr(devices->GetCount(&count),"endpoint count");unsigned tested=0;
    for(UINT i=0;i<count;++i){
        ComPtr<IMMDevice> device;Hr(devices->Item(i,&device),"endpoint");
        ComPtr<IPropertyStore> properties;Hr(device->OpenPropertyStore(STGM_READ,&properties),"properties");
        PROPVARIANT name;PropVariantInit(&name);Hr(properties->GetValue(PKEY_Device_FriendlyName,&name),"name");
        bool ours=name.vt==VT_LPWSTR && std::wstring(name.pwszVal).find(L"Harmonia")!=std::wstring::npos;
        if(ours)std::wcout<<L"Endpoint: "<<name.pwszVal<<L"\n";
        PropVariantClear(&name);if(!ours)continue;
        ComPtr<IAudioEndpointVolume> endpointVolume;Hr(device->Activate(__uuidof(IAudioEndpointVolume),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(endpointVolume.GetAddressOf())),"endpoint volume");
        BOOL endpointMuted=FALSE;float endpointLevel=0;
        Hr(endpointVolume->GetMute(&endpointMuted),"endpoint mute");Hr(endpointVolume->GetMasterVolumeLevelScalar(&endpointLevel),"endpoint level");
        std::cout<<"Endpoint volume="<<endpointLevel<<" mute="<<endpointMuted<<"\n";
        ComPtr<IAudioClient> client;Hr(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(client.GetAddressOf())),"audio client");
        for(WORD channels:{WORD(2),WORD(6)}) {
            WAVEFORMATEXTENSIBLE f{};f.Format.wFormatTag=WAVE_FORMAT_EXTENSIBLE;f.Format.nChannels=channels;f.Format.nSamplesPerSec=48000;f.Format.wBitsPerSample=32;f.Format.nBlockAlign=channels*4;f.Format.nAvgBytesPerSec=48000*f.Format.nBlockAlign;f.Format.cbSize=22;f.Samples.wValidBitsPerSample=32;f.dwChannelMask=channels==2?3:0x60f;f.SubFormat=KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            WAVEFORMATEX* closest=nullptr;HRESULT shared=client->IsFormatSupported(AUDCLNT_SHAREMODE_SHARED,&f.Format,&closest);CoTaskMemFree(closest);
            HRESULT exclusive=client->IsFormatSupported(AUDCLNT_SHAREMODE_EXCLUSIVE,&f.Format,nullptr);
            std::cout<<"Float32 "<<channels<<" channels: shared=0x"<<std::hex<<static_cast<unsigned long>(shared)<<" exclusive=0x"<<static_cast<unsigned long>(exclusive)<<std::dec<<"\n";
        }
        WAVEFORMATEX* mix=nullptr;Hr(client->GetMixFormat(&mix),"mix format");
        std::cout<<"Mix: "<<mix->nSamplesPerSec<<" Hz, "<<mix->nChannels<<" channels, "<<mix->wBitsPerSample<<" bits\n";
        if(mix->nSamplesPerSec!=48000 || (mix->nChannels!=2 && mix->nChannels!=6)){CoTaskMemFree(mix);throw std::runtime_error("unexpected mix format");}
        const bool isFloat=mix->wFormatTag==WAVE_FORMAT_IEEE_FLOAT || (mix->wFormatTag==WAVE_FORMAT_EXTENSIBLE && reinterpret_cast<WAVEFORMATEXTENSIBLE*>(mix)->SubFormat==KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
        if(!isFloat || mix->wBitsPerSample!=32){CoTaskMemFree(mix);throw std::runtime_error("mix is not float32");}
        std::vector<BYTE> captureFormat(sizeof(WAVEFORMATEX)+mix->cbSize);
        std::memcpy(captureFormat.data(),mix,captureFormat.size());
        for(WORD channels:{WORD(2),WORD(6)}) {
            WAVEFORMATEXTENSIBLE format{};format.Format.wFormatTag=WAVE_FORMAT_EXTENSIBLE;format.Format.nChannels=channels;format.Format.nSamplesPerSec=48000;format.Format.wBitsPerSample=32;format.Format.nBlockAlign=channels*4;format.Format.nAvgBytesPerSec=48000*format.Format.nBlockAlign;format.Format.cbSize=22;format.Samples.wValidBitsPerSample=32;format.dwChannelMask=channels==2?3:0x60f;format.SubFormat=KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            WAVEFORMATEX* closest=nullptr;HRESULT support=client->IsFormatSupported(AUDCLNT_SHAREMODE_SHARED,&format.Format,&closest);CoTaskMemFree(closest);
            std::cout<<"Shared format "<<channels<<" channels: HRESULT="<<std::hex<<static_cast<unsigned long>(support)<<std::dec<<"\n";
        }
        HRESULT init=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_EVENTCALLBACK,1000000,0,mix,nullptr);
        const WORD channels=mix->nChannels;CoTaskMemFree(mix);Hr(init,"initialize shared stream");
        ComPtr<ISimpleAudioVolume> sessionVolume;Hr(client->GetService(IID_PPV_ARGS(&sessionVolume)),"session volume");
        BOOL sessionMuted=FALSE;float sessionLevel=0;Hr(sessionVolume->GetMute(&sessionMuted),"session mute");Hr(sessionVolume->GetMasterVolume(&sessionLevel),"session level");
        std::cout<<"Session volume="<<sessionLevel<<" mute="<<sessionMuted<<"\n";
        HANDLE ready=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!ready)throw std::runtime_error("create audio event");
        Hr(client->SetEventHandle(ready),"set audio event");
        UINT32 capacity=0;Hr(client->GetBufferSize(&capacity),"buffer size");
        ComPtr<IAudioRenderClient> render;Hr(client->GetService(IID_PPV_ARGS(&render)),"render service");
        ComPtr<IAudioClient> loopClient;Hr(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(loopClient.GetAddressOf())),"loopback client");
        Hr(loopClient->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK,1000000,0,reinterpret_cast<WAVEFORMATEX*>(captureFormat.data()),nullptr),"loopback initialize");
        ComPtr<IAudioCaptureClient> capture;Hr(loopClient->GetService(IID_PPV_ARGS(&capture)),"loopback capture");
        Hr(loopClient->Start(),"loopback start");float loopPeak=0;
        ComPtr<IAudioClock> clock;Hr(client->GetService(IID_PPV_ARGS(&clock)),"audio clock");
        UINT64 frequency=0,before=0,after=0;
        Hr(clock->GetFrequency(&frequency),"clock frequency");
        Hr(clock->GetPosition(&before,nullptr),"initial clock position");
        Hr(client->Start(),"start");unsigned long long frame=0;
        for(unsigned tick=0;tick<200;++tick){
            if(WaitForSingleObject(ready,2000)!=WAIT_OBJECT_0)throw std::runtime_error("render notification timeout");
            UINT32 padding=0;Hr(client->GetCurrentPadding(&padding),"padding");
            UINT32 frames=capacity-padding;if(frames){BYTE* data=nullptr;Hr(render->GetBuffer(frames,&data),"get buffer");
                float* output=reinterpret_cast<float*>(data);
                for(UINT32 f=0;f<frames;++f,++frame)for(WORD ch=0;ch<channels;++ch)output[f*channels+ch]=static_cast<float>(0.02*std::sin(6.283185307179586*(220.0+110.0*ch)*static_cast<double>(frame)/48000.0));
                Hr(render->ReleaseBuffer(frames,0),"release buffer");}
            UINT32 available=0;Hr(capture->GetNextPacketSize(&available),"loopback packet");
            while(available) {
                BYTE* data=nullptr;UINT32 countFrames=0;DWORD flags=0;
                Hr(capture->GetBuffer(&data,&countFrames,&flags,nullptr,nullptr),"loopback buffer");
                if(!(flags&AUDCLNT_BUFFERFLAGS_SILENT))for(UINT32 s=0;s<countFrames*channels;++s){float value=0;std::memcpy(&value,data+s*4,4);if(std::abs(value)>loopPeak)loopPeak=std::abs(value);}
                Hr(capture->ReleaseBuffer(countFrames),"loopback release");Hr(capture->GetNextPacketSize(&available),"loopback next");
            }
        }
        Hr(clock->GetPosition(&after,nullptr),"final clock position");
        Hr(client->Stop(),"stop");
        Hr(loopClient->Stop(),"loopback stop");std::cout<<"Software loopback peak="<<loopPeak<<"\n";
        CloseHandle(ready);
        if(!frequency || after<before || after-before<frequency || frame<48000)throw std::runtime_error("render stream or device clock did not advance for at least one second");
        std::cout<<"PASS: shared-mode rendered "<<frame<<" frames; device clock advanced "<<static_cast<double>(after-before)/static_cast<double>(frequency)<<" seconds without changing default endpoint\n";++tested;
    }
    if(!tested)throw std::runtime_error("no active Harmonia render endpoint");
    return 0;
} catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
