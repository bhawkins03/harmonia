// Compile with /kernel /W4 /WX to keep the shared codec free of user-mode CRT dependencies.
#include "../protocol/TransportV1.h"
bool HarmoniaProtocolKernelSmoke(const unsigned char* bytes, harmonia::transport::usize length) noexcept {
    harmonia::transport::Message message{};
    const unsigned char* payload=nullptr;
    return harmonia::transport::Decode(bytes,length,message,payload);
}
