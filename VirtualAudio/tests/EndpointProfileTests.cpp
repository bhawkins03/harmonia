#include "../native/EndpointProfile.h"
using namespace harmonia;
static_assert(IsSupportedEncoding(48000, 2, 3, 32, true, 32, 8, 384000), "stereo");
static_assert(IsSupportedEncoding(48000, 6, 0x60f, 32, true, 32, 24, 1152000), "5.1 side");
static_assert(IsSupportedEncoding(48000, 6, 0x3f, 32, true, 32, 24, 1152000), "5.1 back");
static_assert(!IsSupportedEncoding(48000, 6, 3, 32, true, 32, 24, 1152000), "wrong mask");
static_assert(!IsSupportedEncoding(44100, 2, 3, 32, true, 32, 8, 352800), "wrong rate");
static_assert(!IsSupportedEncoding(48000, 2, 3, 32, false, 32, 8, 384000), "integer PCM");
static_assert(!IsSupportedEncoding(48000, 2, 3, 32, true, 24, 8, 384000), "valid bits");
static_assert(!IsSupportedEncoding(48000, 2, 3, 32, true, 32, 4, 384000), "alignment");
static_assert(!IsSupportedEncoding(48000, 2, 3, 32, true, 32, 8, 192000), "average bytes");
static_assert(!IsSupportedEncoding(48000, 8, 0x63f, 32, true, 32, 32, 1536000), "7.1");
static_assert(!IsSupportedEncoding(48000, 0, 0, 32, true, 32, 0, 0), "empty layout");
static_assert(!IsSupportedEncoding(48000, 65535, 0x60f, 32, true, 32, 65532, 0xffffffff), "oversized layout");
