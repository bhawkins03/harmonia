// Harmonia transport v1. Logical objects are never copied onto the wire.
#pragma once
#include "../native/EndpointProfile.h"
namespace harmonia::transport {
using u8 = unsigned char;
using u16 = unsigned short;
using u32 = unsigned int;
using u64 = unsigned long long;
using usize = decltype(sizeof(0));
static_assert(sizeof(u8) == 1 && sizeof(u16) == 2 && sizeof(u32) == 4 && sizeof(u64) == 8);
constexpr u32 Magic = 0x31544148; // ASCII HAT1
constexpr u16 Version = 1, HeaderBytes = 160;
constexpr u32 MaxFrames = 480, CapacityFrames = 8192, MaxPayload = 480 * 24;
constexpr u32 MaxMessage = HeaderBytes + MaxPayload;
enum class Operation : u16 { Capabilities = 1, Attach = 2, ReadFrames = 3, State = 4, Detach = 5 };
enum class Kind : u16 { Request = 1, Response = 2 };
enum class Status : u32 { Ok, NoData, NotAttached, Busy, GenerationMismatch, UnsupportedFormat, Protected, Stopped, Removed, CounterExhausted };
enum class StreamState : u16 { Idle, Running, Paused, Stopped, Protected, Removed };
enum Flag : u32 { Discontinuity = 1, GenerationChanged = 2, Overflow = 4, Disconnected = 8, ProtectedContent = 16, EstimatedTimestamp = 32 };
constexpr u32 KnownFlags = 63, Features = 7; // direct reads, separate loss counters, QPC timestamps
// Windows CTL_CODE, private device type/functions; definitions only, no handlers.
constexpr u32 DeviceType = 0x8000, ReadAccess = 1, WriteAccess = 2;
constexpr u32 Ioctl(Operation op) noexcept {
    return (DeviceType << 16) | ((ReadAccess | WriteAccess) << 14) |
        ((0x800 + static_cast<u32>(op) - 1) << 2) | (op == Operation::ReadFrames ? 2U : 0U);
}
struct Message {
    Operation Op{};
    Kind Type{};
    u32 Flags{};
    Status Result{};
    u64 Session{}, Generation{}, Sequence{}, FirstFrame{}, Qpc{}, QpcFrequency{};
    u64 OverflowFrames{}, DisconnectedFrames{}, TimingLostFrames{};
    u32 SampleRate{};
    u16 SampleFormat{}, Channels{};
    u32 SpeakerMask{};
    u16 FrameBytes{};
    StreamState State{};
    u32 FrameCount{}, RequestedFrames{}, PayloadBytes{}, QueueCapacity{}, FeatureBits{}, FormatCount{};
};
inline void Put(u8* p, usize offset, u64 value, usize width) noexcept {
    for (usize i = 0; i < width; ++i) p[offset + i] = static_cast<u8>(value >> (8 * i));
}
inline u64 Get(const u8* p, usize offset, usize width) noexcept {
    u64 value = 0;
    for (usize i = 0; i < width; ++i) value |= static_cast<u64>(p[offset + i]) << (8 * i);
    return value;
}
inline bool Zero(const u8* p, usize start, usize end) noexcept {
    for (usize i = start; i < end; ++i) if (p[i]) return false;
    return true;
}
inline bool ValidFormat(const Message& m) noexcept {
    return m.SampleFormat == 1 && harmonia::IsSupportedFormat(m.SampleRate, m.Channels, m.SpeakerMask, 32, true) && m.FrameBytes == m.Channels * 4;
}
inline bool EmptyFormat(const Message& m) noexcept {
    return !(m.SampleRate || m.SampleFormat || m.Channels || m.SpeakerMask || m.FrameBytes);
}
inline bool EmptySnapshot(const Message& m) noexcept {
    return !(m.Generation || m.Sequence || m.FirstFrame || m.Qpc || m.QpcFrequency || m.OverflowFrames ||
        m.DisconnectedFrames || m.TimingLostFrames || m.Flags || m.FrameCount) && m.State == StreamState::Idle && EmptyFormat(m);
}
inline bool ValidCapabilities(const u8* p, usize n) noexcept {
    if (!p || n != 48) return false;
    constexpr u16 channels[] = { 2, 6, 6 };
    constexpr u32 masks[] = { 3, 0x60f, 0x3f };
    for (usize i = 0; i < 3; ++i) {
        const usize o = i * 16;
        if (Get(p,o,4) != 48000 || Get(p,o+4,2) != 1 || Get(p,o+6,2) != channels[i] ||
            Get(p,o+8,4) != masks[i] || Get(p,o+12,2) != channels[i] * 4U || Get(p,o+14,2)) return false;
    }
    return true;
}
inline bool Validate(const Message& m, const u8* payload, usize n) noexcept {
    const auto op = static_cast<u16>(m.Op);
    if (op < 1 || op > 5 || (m.Type != Kind::Request && m.Type != Kind::Response) ||
        static_cast<u32>(m.Result) > 9 || static_cast<u16>(m.State) > 5 ||
        (m.Flags & ~KnownFlags) || n > MaxPayload || m.PayloadBytes != n || (n && !payload)) return false;
    if ((m.Flags & (GenerationChanged | Overflow | Disconnected)) && !(m.Flags & Discontinuity)) return false;
    if (m.Type == Kind::Request) {
        if (m.Result != Status::Ok || !EmptySnapshot(m) || n || m.QueueCapacity || m.FeatureBits || m.FormatCount) return false;
        if (m.Op == Operation::Capabilities || m.Op == Operation::Attach)
            return !m.Session && !m.RequestedFrames;
        // READ carries its expected generation in Generation, handled below by Decode/Encode request validation.
        return m.Session && !m.RequestedFrames && m.Op != Operation::ReadFrames;
    }
    if (m.Result != Status::Ok) {
        // Canonical errors carry no snapshot or audio. Re-query STATE for details.
        return EmptySnapshot(m) && !n && !m.RequestedFrames && !m.QueueCapacity && !m.FeatureBits && !m.FormatCount &&
            (m.Op != Operation::Capabilities || !m.Session);
    }
    if (m.Op == Operation::Capabilities)
        return !m.Session && EmptySnapshot(m) && m.RequestedFrames == MaxFrames &&
            m.QueueCapacity == CapacityFrames && m.FeatureBits == Features && m.FormatCount == 3 && ValidCapabilities(payload,n);
    if (!m.Session || m.RequestedFrames || m.QueueCapacity || m.FeatureBits || m.FormatCount) return false;
    if (m.Op == Operation::Detach) return EmptySnapshot(m) && !n;
    if (!m.Generation) return m.Op != Operation::ReadFrames && EmptySnapshot(m) && !n;
    if (!ValidFormat(m) || !m.QpcFrequency) return false;
    if (m.State == StreamState::Idle || ((m.Flags & ProtectedContent) != 0) != (m.State == StreamState::Protected)) return false;
    if (m.Op != Operation::ReadFrames) return !m.FrameCount && !n;
    return m.State == StreamState::Running && !(m.Flags & ProtectedContent) && m.Sequence &&
        m.FrameCount >= 1 && m.FrameCount <= MaxFrames && n == m.FrameCount * m.FrameBytes &&
        m.FirstFrame <= (~u64(0)) - m.FrameCount;
}
inline bool ValidMessage(const Message& m, const u8* p, usize n) noexcept {
    if (m.Type == Kind::Request && m.Op == Operation::ReadFrames) {
        if (!m.Session || !m.Generation || m.RequestedFrames < 1 || m.RequestedFrames > MaxFrames) return false;
        Message copy = m;
        copy.Generation = 0; copy.RequestedFrames = 0; copy.Op = Operation::State;
        return Validate(copy,p,n);
    }
    return Validate(m,p,n);
}
inline bool ValidReply(const Message& request, const Message& reply, const u8* payload, usize n) noexcept {
    if (!ValidMessage(request,nullptr,0) || !ValidMessage(reply,payload,n) ||
        request.Type != Kind::Request || reply.Type != Kind::Response || request.Op != reply.Op) return false;
    if (request.Op != Operation::Attach && reply.Session != request.Session) return false;
    if (request.Op == Operation::Attach && reply.Result != Status::Ok && reply.Session) return false;
    return request.Op != Operation::ReadFrames || reply.Result != Status::Ok ||
        (reply.Generation == request.Generation && reply.FrameCount <= request.RequestedFrames);
}
// Functions below are generated field-by-field from the documented offsets.
inline bool Encode(const Message& m, const u8* payload, usize n, u8* output, usize capacity, usize& written) noexcept {
    written = 0;
    if (!output || !ValidMessage(m,payload,n) || capacity < HeaderBytes + n) return false;
    // Input payload and output must not overlap. Caller retains both buffers.
    for (usize i = 0; i < HeaderBytes; ++i) output[i] = 0;
    Put(output,0,Magic,4); Put(output,4,Version,2); Put(output,6,HeaderBytes,2); Put(output,8,HeaderBytes+n,4);
    Put(output,12,static_cast<u64>(m.Op),2);
    Put(output,14,static_cast<u64>(m.Type),2);
    Put(output,16,static_cast<u64>(m.Flags),4);
    Put(output,20,static_cast<u64>(m.Result),4);
    Put(output,24,static_cast<u64>(m.Session),8);
    Put(output,32,static_cast<u64>(m.Generation),8);
    Put(output,40,static_cast<u64>(m.Sequence),8);
    Put(output,48,static_cast<u64>(m.FirstFrame),8);
    Put(output,56,static_cast<u64>(m.Qpc),8);
    Put(output,64,static_cast<u64>(m.QpcFrequency),8);
    Put(output,72,static_cast<u64>(m.OverflowFrames),8);
    Put(output,80,static_cast<u64>(m.DisconnectedFrames),8);
    Put(output,88,static_cast<u64>(m.TimingLostFrames),8);
    Put(output,96,static_cast<u64>(m.SampleRate),4);
    Put(output,100,static_cast<u64>(m.SampleFormat),2);
    Put(output,102,static_cast<u64>(m.Channels),2);
    Put(output,104,static_cast<u64>(m.SpeakerMask),4);
    Put(output,108,static_cast<u64>(m.FrameBytes),2);
    Put(output,110,static_cast<u64>(m.State),2);
    Put(output,112,static_cast<u64>(m.FrameCount),4);
    Put(output,116,static_cast<u64>(m.RequestedFrames),4);
    Put(output,120,static_cast<u64>(m.PayloadBytes),4);
    Put(output,124,static_cast<u64>(m.QueueCapacity),4);
    Put(output,128,static_cast<u64>(m.FeatureBits),4);
    Put(output,132,static_cast<u64>(m.FormatCount),4);
    for (usize i = 0; i < n; ++i) output[HeaderBytes+i] = payload[i];
    written = HeaderBytes+n;
    return true;
}
inline bool Decode(const u8* input, usize length, Message& result, const u8*& payload) noexcept {
    result = {}; payload = nullptr;
    if (!input || length < HeaderBytes || length > MaxMessage || Get(input,0,4) != Magic ||
        Get(input,4,2) != Version || Get(input,6,2) != HeaderBytes || Get(input,8,4) != length || !Zero(input,136,160)) return false;
    Message m{};
    m.Op = static_cast<Operation>(Get(input,12,2));
    m.Type = static_cast<Kind>(Get(input,14,2));
    m.Flags = static_cast<u32>(Get(input,16,4));
    m.Result = static_cast<Status>(Get(input,20,4));
    m.Session = static_cast<u64>(Get(input,24,8));
    m.Generation = static_cast<u64>(Get(input,32,8));
    m.Sequence = static_cast<u64>(Get(input,40,8));
    m.FirstFrame = static_cast<u64>(Get(input,48,8));
    m.Qpc = static_cast<u64>(Get(input,56,8));
    m.QpcFrequency = static_cast<u64>(Get(input,64,8));
    m.OverflowFrames = static_cast<u64>(Get(input,72,8));
    m.DisconnectedFrames = static_cast<u64>(Get(input,80,8));
    m.TimingLostFrames = static_cast<u64>(Get(input,88,8));
    m.SampleRate = static_cast<u32>(Get(input,96,4));
    m.SampleFormat = static_cast<u16>(Get(input,100,2));
    m.Channels = static_cast<u16>(Get(input,102,2));
    m.SpeakerMask = static_cast<u32>(Get(input,104,4));
    m.FrameBytes = static_cast<u16>(Get(input,108,2));
    m.State = static_cast<StreamState>(Get(input,110,2));
    m.FrameCount = static_cast<u32>(Get(input,112,4));
    m.RequestedFrames = static_cast<u32>(Get(input,116,4));
    m.PayloadBytes = static_cast<u32>(Get(input,120,4));
    m.QueueCapacity = static_cast<u32>(Get(input,124,4));
    m.FeatureBits = static_cast<u32>(Get(input,128,4));
    m.FormatCount = static_cast<u32>(Get(input,132,4));
    if (!ValidMessage(m,input+HeaderBytes,length-HeaderBytes)) return false;
    result = m; payload = input+HeaderBytes;
    return true;
}
// Stateful consumer guard; only successful READ_FRAMES responses may enter it.
// Start a new tracker after a fresh ATTACH. Counters never decrease in a session.
class Continuity {
    bool seen = false;
    Message last{};
public:
    bool Accept(const Message& m, const u8* payload, usize n) noexcept {
        if (!ValidMessage(m,payload,n) || m.Type != Kind::Response || m.Op != Operation::ReadFrames || m.Result != Status::Ok) return false;
        if (seen) {
            if (m.Session != last.Session || m.Generation < last.Generation || m.Sequence <= last.Sequence ||
                m.QpcFrequency != last.QpcFrequency || m.Qpc < last.Qpc ||
                m.OverflowFrames < last.OverflowFrames || m.DisconnectedFrames < last.DisconnectedFrames ||
                m.TimingLostFrames < last.TimingLostFrames) return false;
            if (m.Generation != last.Generation) {
                if ((m.Flags & (Discontinuity|GenerationChanged)) != (Discontinuity|GenerationChanged)) return false;
            } else {
                if (m.SpeakerMask != last.SpeakerMask || m.Channels != last.Channels || m.FirstFrame < last.FirstFrame+last.FrameCount) return false;
                if ((m.FirstFrame != last.FirstFrame+last.FrameCount || m.Sequence != last.Sequence+1) && !(m.Flags & Discontinuity)) return false;
            }
        } else if (!(m.Flags & Discontinuity)) return false;
        last=m; seen=true; return true;
    }
};
}
