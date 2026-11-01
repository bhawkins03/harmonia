#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sddl.h>
#include "../protocol/TransportV1.h"
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
#include <fstream>
#include <cstring>
#include <cmath>
using namespace harmonia::transport;
static unsigned checks=0;
static bool serviceObserve=false,serviceProtected=false;
static const char* readinessPath="C:\\HarmoniaTest\\Logs\\TransportObserverReady.txt";
static void Check(bool ok,const char* text) { ++checks; if(!ok) throw std::runtime_error(std::string(text)+" (Win32="+std::to_string(GetLastError())+")"); }
struct Handle {
    HANDLE value=INVALID_HANDLE_VALUE;
    ~Handle(){if(value!=INVALID_HANDLE_VALUE)CloseHandle(value);}
    void Open(){value=CreateFileW(L"\\\\.\\HarmoniaAudioTransport",GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);Check(value!=INVALID_HANDLE_VALUE,"open transport");}
};
static Message Request(Operation op,u64 session=0) { Message m{}; m.Op=op;m.Type=Kind::Request;m.Session=session;return m; }
static Message Call(HANDLE h,const Message& request,std::vector<u8>* audio=nullptr) {
    u8 input[HeaderBytes]{};usize count=0;
    Check(Encode(request,nullptr,0,input,sizeof(input),count),"encode request");
    std::vector<u8> output(MaxMessage);DWORD returned=0;
    Check(DeviceIoControl(h,Ioctl(request.Op),input,static_cast<DWORD>(count),output.data(),static_cast<DWORD>(output.size()),&returned,nullptr)!=FALSE,"DeviceIoControl");
    Message response{};const u8* payload=nullptr;
    Check(Decode(output.data(),returned,response,payload),"decode live response");
    Check(ValidReply(request,response,payload,response.PayloadBytes),"bind live response to request");
    if(audio)audio->assign(payload,payload+response.PayloadBytes);
    return response;
}
static int RunTests(int argc,char** argv) try {
    if(argc!=2) throw std::runtime_error("mode required: deny or system");
    if(std::string(argv[1])=="deny") {
        for(DWORD access:std::vector<DWORD>{GENERIC_READ,GENERIC_WRITE,GENERIC_READ|GENERIC_WRITE}) {
            HANDLE h=CreateFileW(L"\\\\.\\HarmoniaAudioTransport",access,0,nullptr,OPEN_EXISTING,0,nullptr);
            const DWORD error=GetLastError();if(h!=INVALID_HANDLE_VALUE)CloseHandle(h);
            Check(h==INVALID_HANDLE_VALUE && error==ERROR_ACCESS_DENIED,"unauthorized access must be denied, not missing device");
        }
} else if(std::string(argv[1])=="system" || std::string(argv[1])=="restricted" || std::string(argv[1])=="observe" || std::string(argv[1])=="observe-protected" || std::string(argv[1])=="restricted-observe" || std::string(argv[1])=="restricted-observe-protected") {
        HANDLE token=nullptr;Check(OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)!=FALSE,"query token");
        DWORD needed=0;GetTokenInformation(token,TokenUser,nullptr,0,&needed);std::vector<u8> user(needed);
        Check(GetTokenInformation(token,TokenUser,user.data(),needed,&needed)!=FALSE,"token user");
        auto sid=reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid;
        if(std::string(argv[1]).find("restricted")==0) {
            Check(IsWellKnownSid(sid,WinLocalServiceSid)!=FALSE && IsTokenRestricted(token)!=FALSE,"restricted LocalService identity");
            needed=0;GetTokenInformation(token,TokenRestrictedSids,nullptr,0,&needed);std::vector<u8> restricted(needed);
            Check(GetTokenInformation(token,TokenRestrictedSids,restricted.data(),needed,&needed)!=FALSE,"restricted SID list");
            PSID expected=nullptr;Check(ConvertStringSidToSidW(L"S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259",&expected)!=FALSE,"service SID");
            auto groups=reinterpret_cast<TOKEN_GROUPS*>(restricted.data());bool found=false;
            for(DWORD i=0;i<groups->GroupCount;++i)if(EqualSid(groups->Groups[i].Sid,expected))found=true;
            LocalFree(expected);Check(found,"exact approved restricted service SID present");
        } else Check(IsWellKnownSid(sid,WinLocalSystemSid)!=FALSE,"LocalSystem identity");
        CloseHandle(token);
        if(std::string(argv[1]).find("observe")!=std::string::npos) {
            const bool expectProtected=std::string(argv[1]).find("protected")!=std::string::npos;
            Handle h;h.Open();auto attached=Call(h.value,Request(Operation::Attach));
            Check(attached.Result==Status::Ok,"observe attach");
            {std::ofstream ready(readinessPath);Check(ready.good(),"observer readiness file");ready<<"attached\n";}
            u64 frames=0,toneGeneration=0;unsigned protectedReads=0;
            float peak=0;
            std::vector<float> tone;
            std::vector<u64> positions;
            const auto deadline=GetTickCount64()+6000;
            while(GetTickCount64()<deadline) {
                auto state=Call(h.value,Request(Operation::State,attached.Session));
                if(state.Generation) {
                    auto request=Request(Operation::ReadFrames,attached.Session);request.Generation=state.Generation;request.RequestedFrames=480;
                    std::vector<u8> audio;auto reply=Call(h.value,request,&audio);
                    if(reply.Result==Status::Protected)++protectedReads;
                    if(reply.Result==Status::Ok) {
                        Check(reply.FrameCount>0 && reply.Channels==6 && reply.SampleRate==48000,"actual render export format");
                        for(usize i=0;i<audio.size();i+=4){float sample=0;std::memcpy(&sample,audio.data()+i,4);Check(std::isfinite(sample) && std::abs(sample)<=0.021f,"exported test-tone sample bounds");if(std::abs(sample)>peak)peak=std::abs(sample);}
                        if(tone.size()<4096*6) {
                            if(toneGeneration!=reply.Generation){tone.clear();positions.clear();toneGeneration=reply.Generation;}
                            for(usize f=0;f<reply.FrameCount && tone.size()<4096*6;++f) {
                                float samples[6]{};std::memcpy(samples,audio.data()+f*24,24);
                                bool audible=false;for(float sample:samples)if(std::abs(sample)>0.000001f)audible=true;
                                if(!tone.empty() || audible){tone.insert(tone.end(),samples,samples+6);positions.push_back(reply.FirstFrame+f);}
                            }
                        }
                        frames+=reply.FrameCount;
                    }
                }
                Sleep(5);
            }
            std::cout<<"OBSERVE: exportedFrames="<<frames<<" protectedReads="<<protectedReads<<" sampledFrames="<<positions.size()<<" peak="<<peak<<"\n";
            if(expectProtected) {
                Check(frames==0 && protectedReads>0,"simulated protection must deny all PCM while playback advances");
                std::cout<<"PASS: simulated CopyProtect denied PCM\n";return 0;
            }
            Check(frames>0,"no PCM exported during event-driven unprotected test tone; inspect rights/publication gate");
            Check(tone.size()==4096*6,"4096 PCM frames with a non-silent start required for channel validation");
            for(unsigned channel=0;channel<6;++channel) {
                double own=0,other=0;
                for(unsigned frequency=0;frequency<6;++frequency) {
                    double real=0,imaginary=0;
                    for(unsigned f=0;f<4096;++f) {
                        const double phase=6.283185307179586*(220.0+110.0*frequency)*static_cast<double>(positions[f])/48000.0;
                        real+=tone[f*6+channel]*std::cos(phase);imaginary+=tone[f*6+channel]*std::sin(phase);
                    }
                    const double magnitude=std::sqrt(real*real+imaginary*imaginary)/4096;
                    if(frequency==channel)own=magnitude;else if(magnitude>other)other=magnitude;
                }
                Check(own>0.00001 && own>other*4,"per-channel tone identity/isolation");
            }
            std::cout<<"PASS: live PCM publication and six-channel tone isolation\n";return 0;
        }
        for(unsigned iteration=0;iteration<100;++iteration) {
            Handle h;h.Open();
            Check(Call(h.value,Request(Operation::Capabilities)).Result==Status::Ok,"capabilities");
            auto attached=Call(h.value,Request(Operation::Attach));
            Check(attached.Result==Status::Ok && attached.Session!=0,"attach");
            Check(Call(h.value,Request(Operation::Attach)).Result==Status::Busy,"second attach busy");
            auto state=Call(h.value,Request(Operation::State,attached.Session));
            Check(state.Result==Status::Ok,"state");
            auto read=Request(Operation::ReadFrames,attached.Session);read.Generation=state.Generation?state.Generation:1;read.RequestedFrames=1;
            auto reply=Call(h.value,read);
            if(state.State!=StreamState::Running)Check(reply.Result!=Status::Ok && reply.PayloadBytes==0,"inactive/protected stream must not export PCM");
            Check(Call(h.value,Request(Operation::State,attached.Session+1)).Result==Status::NotAttached,"stale token denied");
            u8 malformed[HeaderBytes]{};u8 output[MaxMessage]{};DWORD returned=0;
            Check(!DeviceIoControl(h.value,Ioctl(Operation::Capabilities),malformed,sizeof(malformed),output,sizeof(output),&returned,nullptr) && returned==0,"malformed request rejected with no output");
            returned=0;
            Check(!DeviceIoControl(h.value,0x8000ffff,malformed,sizeof(malformed),output,sizeof(output),&returned,nullptr) && returned==0,"unknown METHOD_NEITHER rejected with no output");
            if(iteration%2) Check(Call(h.value,Request(Operation::Detach,attached.Session)).Result==Status::Ok,"detach");
            // Other iterations close while attached; next iteration must attach.
        }
        for(DWORD access:std::vector<DWORD>{GENERIC_READ,GENERIC_WRITE}) {
            HANDLE h=CreateFileW(L"\\\\.\\HarmoniaAudioTransport",access,0,nullptr,OPEN_EXISTING,0,nullptr);
            const DWORD error=GetLastError();if(h!=INVALID_HANDLE_VALUE)CloseHandle(h);
            Check(h==INVALID_HANDLE_VALUE && error==ERROR_ACCESS_DENIED,"single-access open denied even for SYSTEM");
        }
        HANDLE suffix=CreateFileW(L"\\\\.\\HarmoniaAudioTransport\\unexpected",GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);
        const DWORD error=GetLastError();if(suffix!=INVALID_HANDLE_VALUE)CloseHandle(suffix);
        Check(suffix==INVALID_HANDLE_VALUE && error==ERROR_ACCESS_DENIED,"relative namespace denied");
    } else throw std::runtime_error("unknown mode");
    std::cout<<"PASS: "<<argv[1]<<" live transport checks="<<checks<<"\n";return 0;
} catch(const std::exception& e) {std::cerr<<"FAIL: "<<e.what()<<" after "<<checks<<" checks\n";return 1;}


static SERVICE_STATUS_HANDLE serviceHandle=nullptr;
static void WINAPI ServiceControl(DWORD) {}
static void WINAPI ServiceMain(DWORD,LPWSTR*) {
    serviceHandle=RegisterServiceCtrlHandlerW(L"HarmoniaAudioTransport",ServiceControl);
    if(!serviceHandle)return;
    SERVICE_STATUS status{};status.dwServiceType=SERVICE_WIN32_OWN_PROCESS;status.dwCurrentState=SERVICE_RUNNING;
    SetServiceStatus(serviceHandle,&status);
    std::ofstream log("C:\\HarmoniaTest\\Logs\\RuntimeTransportService.txt",std::ios::trunc);
    auto oldOut=std::cout.rdbuf(log.rdbuf());auto oldErr=std::cerr.rdbuf(log.rdbuf());
    char program[]="RuntimeTransportTests";char mode[]="restricted";char* args[]={program,mode};
    int result=log.good()?RunTests(2,args):1;
    if(!result && serviceObserve) {
        readinessPath="C:\\HarmoniaTest\\Logs\\TransportServiceReady.txt";
        char observe[]="restricted-observe";char protectedObserve[]="restricted-observe-protected";
        char* streamArgs[]={program,serviceProtected?protectedObserve:observe};result=RunTests(2,streamArgs);
    }
    std::cout.flush();std::cerr.flush();std::cout.rdbuf(oldOut);std::cerr.rdbuf(oldErr);log.close();
    status.dwCurrentState=SERVICE_STOPPED;status.dwWin32ExitCode=result?ERROR_SERVICE_SPECIFIC_ERROR:NO_ERROR;
    status.dwServiceSpecificExitCode=static_cast<DWORD>(result);SetServiceStatus(serviceHandle,&status);
}
int main(int argc,char** argv) {
    if(argc==2 && (std::string(argv[1])=="service" || std::string(argv[1])=="service-observe" || std::string(argv[1])=="service-observe-protected")) {
        serviceObserve=std::string(argv[1])!="service";serviceProtected=std::string(argv[1])=="service-observe-protected";
        SERVICE_TABLE_ENTRYW table[]={{const_cast<LPWSTR>(L"HarmoniaAudioTransport"),ServiceMain},{nullptr,nullptr}};
        if(!StartServiceCtrlDispatcherW(table)){std::cerr<<"Service dispatcher failed: "<<GetLastError()<<"\n";return 1;}
        return 0;
    }
    return RunTests(argc,argv);
}
