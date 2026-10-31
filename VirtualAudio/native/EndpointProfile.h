// Harmonia format policy; not yet integrated into a SysVAD driver project.
#pragma once
#include <stdint.h>
namespace harmonia {
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
static_assert(IsSupportedFormat(48000, 6, SurroundSideMask, 32, true), "5.1 side");
static_assert(IsSupportedFormat(48000, 6, SurroundBackMask, 32, true), "5.1 back");
static_assert(IsSupportedFormat(48000, 2, StereoMask, 32, true), "stereo");
static_assert(!IsSupportedFormat(48000, 6, StereoMask, 32, true), "mask mismatch");
static_assert(!IsSupportedFormat(48000, 6, SurroundSideMask, 32, false), "PCM32 rejected");
static_assert(!IsSupportedFormat(48000, 8, 0x63f, 32, true), "7.1 deferred");
}
