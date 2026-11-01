// Harmonia format policy shared by driver negotiation and native checks.
#pragma once

namespace harmonia {
using uint32_t = unsigned int;
using uint16_t = unsigned short;
static_assert(sizeof(uint32_t) == 4 && sizeof(uint16_t) == 2, "Windows format field sizes");
constexpr uint32_t SampleRate = 48000;
constexpr uint16_t SampleBits = 32; // IEEE-754 float, not integer PCM32.
constexpr uint32_t StereoMask = 0x3;
constexpr uint32_t SurroundSideMask = 0x60f;
constexpr uint32_t SurroundBackMask = 0x3f;
constexpr bool IsSupportedLayout(uint16_t channels, uint32_t mask) noexcept {
    return (channels == 2 && mask == StereoMask) ||
           (channels == 6 && (mask == SurroundSideMask || mask == SurroundBackMask));
}
// Caller also validates extensible header length, IEEE_FLOAT subtype, valid
// bits, block alignment and average bytes/sec; this is only the profile check.
constexpr bool IsSupportedFormat(uint32_t rate, uint16_t channels,
                                uint32_t mask, uint16_t bits,
                                bool ieeeFloat) noexcept {
    return rate == SampleRate && bits == SampleBits && ieeeFloat &&
           IsSupportedLayout(channels, mask);
}
// Scalar validation shared with the kernel; GUIDs and buffer sizes are checked
// before calling this function. No CRT headers are required in kernel builds.
constexpr bool IsSupportedEncoding(uint32_t rate, uint16_t channels, uint32_t mask,
    uint16_t bits, bool ieeeFloat, uint16_t validBits, uint16_t blockAlign,
    uint32_t averageBytes) noexcept {
    return IsSupportedFormat(rate, channels, mask, bits, ieeeFloat) &&
        validBits == SampleBits && blockAlign == channels * 4 &&
        averageBytes == SampleRate * blockAlign;
}
static_assert(IsSupportedFormat(48000, 6, SurroundSideMask, 32, true), "5.1 side");
static_assert(IsSupportedFormat(48000, 6, SurroundBackMask, 32, true), "5.1 back");
static_assert(IsSupportedFormat(48000, 2, StereoMask, 32, true), "stereo");
static_assert(!IsSupportedFormat(48000, 6, StereoMask, 32, true), "mask mismatch");
static_assert(!IsSupportedFormat(48000, 6, SurroundSideMask, 32, false), "PCM32 rejected");
static_assert(!IsSupportedFormat(48000, 8, 0x63f, 32, true), "7.1 deferred");
}
