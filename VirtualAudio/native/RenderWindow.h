#pragma once
#include "../protocol/TransportV1.h"
namespace harmonia::transport {
// Absolute packet-commit window; never infer validity from a modulo cursor.
class RenderWindow {
    u64 first=0,last=0;
    bool known=false;
public:
    constexpr void Reset() noexcept {known=false;first=last=0;}
    constexpr bool Commit(u64 start,u32 bytes,u16 stride) noexcept {
        if(!stride || !bytes || start%stride || bytes%stride || start>~u64(0)-bytes) {Reset();return false;}
        const u64 end=start+bytes;
        if(!known || start>last || end<last) first=start;
        last=end;known=true;return true;
    }
    constexpr bool Contains(u64 position,u64 bytes,u32 capacity,u16 stride,bool packetMode) const noexcept {
        return packetMode && known && stride && capacity && capacity%stride==0 && position%stride==0 &&
            bytes%stride==0 && bytes<=capacity && bytes/stride<=MaxFrames && position>=first &&
            position<=~u64(0)-bytes && position+bytes<=last &&
            (last<=capacity || position>=last-capacity);
    }
};
}
