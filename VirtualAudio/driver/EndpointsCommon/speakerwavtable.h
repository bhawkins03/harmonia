/* Copyright (c) Microsoft Corporation. All rights reserved.
 * Harmonia render descriptors derived from the MIT-licensed SysVAD sample.
 */
#pragma once
#include "SysVadShared.h"
#include "../../native/EndpointProfile.h"
#define SPEAKER_DEVICE_MAX_CHANNELS 6

#define HARMONIA_FORMAT(channels, mask) \
{ { sizeof(KSDATAFORMAT_WAVEFORMATEXTENSIBLE), 0, 0, 0, \
    STATICGUIDOF(KSDATAFORMAT_TYPE_AUDIO), STATICGUIDOF(KSDATAFORMAT_SUBTYPE_IEEE_FLOAT), \
    STATICGUIDOF(KSDATAFORMAT_SPECIFIER_WAVEFORMATEX) }, \
  { { WAVE_FORMAT_EXTENSIBLE, channels, 48000, 48000 * channels * 4, channels * 4, 32, \
      sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX) }, \
    32, mask, STATICGUIDOF(KSDATAFORMAT_SUBTYPE_IEEE_FLOAT) } }

static KSDATAFORMAT_WAVEFORMATEXTENSIBLE SpeakerHostPinSupportedDeviceFormats[] = {
    HARMONIA_FORMAT(6, harmonia::SurroundSideMask),
    HARMONIA_FORMAT(6, harmonia::SurroundBackMask),
    HARMONIA_FORMAT(2, harmonia::StereoMask),
};
#undef HARMONIA_FORMAT
static MODE_AND_DEFAULT_FORMAT SpeakerHostPinSupportedDeviceModes[] = {
    { STATIC_AUDIO_SIGNALPROCESSINGMODE_RAW, &SpeakerHostPinSupportedDeviceFormats[0].DataFormat },
    { STATIC_AUDIO_SIGNALPROCESSINGMODE_DEFAULT, &SpeakerHostPinSupportedDeviceFormats[0].DataFormat },
};
// Pin order follows the existing non-offload render helpers in minwavert.h.
static PIN_DEVICE_FORMATS_AND_MODES SpeakerPinDeviceFormatsAndModes[] = {
    { SystemRenderPin, SpeakerHostPinSupportedDeviceFormats, SIZEOF_ARRAY(SpeakerHostPinSupportedDeviceFormats),
      SpeakerHostPinSupportedDeviceModes, SIZEOF_ARRAY(SpeakerHostPinSupportedDeviceModes) },
    { BridgePin, NULL, 0, NULL, 0 },
};
// Endpoint discovery requires a PCM-family wildcard range. Concrete formats
// and intersection/stream validation remain strictly IEEE Float32.
#define HARMONIA_RANGE(channels) \
{ { sizeof(KSDATARANGE_AUDIO), KSDATARANGE_ATTRIBUTES, 0, 0, \
    STATICGUIDOF(KSDATAFORMAT_TYPE_AUDIO), STATICGUIDOF(KSDATAFORMAT_SUBTYPE_WILDCARD), \
    STATICGUIDOF(KSDATAFORMAT_SPECIFIER_WAVEFORMATEX) }, channels, 32, 32, 48000, 48000 }
static KSDATARANGE_AUDIO SpeakerPinDataRangesStream[] = { HARMONIA_RANGE(2), HARMONIA_RANGE(6) };
#undef HARMONIA_RANGE
static PKSDATARANGE SpeakerPinDataRangePointersStream[] = {
    PKSDATARANGE(&SpeakerPinDataRangesStream[0]), PKSDATARANGE(&PinDataRangeAttributeList),
    PKSDATARANGE(&SpeakerPinDataRangesStream[1]), PKSDATARANGE(&PinDataRangeAttributeList),
};
static KSDATARANGE SpeakerBridgeRange = {
    sizeof(KSDATARANGE), 0, 0, 0, STATICGUIDOF(KSDATAFORMAT_TYPE_AUDIO),
    STATICGUIDOF(KSDATAFORMAT_SUBTYPE_ANALOG), STATICGUIDOF(KSDATAFORMAT_SPECIFIER_NONE)
};
static PKSDATARANGE SpeakerBridgePointers[] = { &SpeakerBridgeRange };
static PCPIN_DESCRIPTOR SpeakerWaveMiniportPins[] = {
    { 1, 1, 0, NULL, { 0, NULL, 0, NULL, SIZEOF_ARRAY(SpeakerPinDataRangePointersStream), SpeakerPinDataRangePointersStream,
        KSPIN_DATAFLOW_IN, KSPIN_COMMUNICATION_SINK, &KSCATEGORY_AUDIO, NULL, 0 } },
    { 0, 0, 0, NULL, { 0, NULL, 0, NULL, SIZEOF_ARRAY(SpeakerBridgePointers), SpeakerBridgePointers,
        KSPIN_DATAFLOW_OUT, KSPIN_COMMUNICATION_NONE, &KSCATEGORY_AUDIO, NULL, 0 } },
};
static PCCONNECTION_DESCRIPTOR SpeakerWaveMiniportConnections[] = {
    { PCFILTER_NODE, KSPIN_WAVE_RENDER3_SINK_SYSTEM, PCFILTER_NODE, KSPIN_WAVE_RENDER3_SOURCE },
};
static PCPROPERTY_ITEM PropertiesSpeakerWaveFilter[] = {
    { &KSPROPSETID_Pin, KSPROPERTY_PIN_PROPOSEDATAFORMAT, KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_BASICSUPPORT, PropertyHandler_WaveFilter },
    { &KSPROPSETID_Pin, KSPROPERTY_PIN_PROPOSEDATAFORMAT2, KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_BASICSUPPORT, PropertyHandler_WaveFilter },
};
DEFINE_PCAUTOMATION_TABLE_PROP(AutomationSpeakerWaveFilter, PropertiesSpeakerWaveFilter);
static PCFILTER_DESCRIPTOR SpeakerWaveMiniportFilterDescriptor = {
    0, &AutomationSpeakerWaveFilter, sizeof(PCPIN_DESCRIPTOR), SIZEOF_ARRAY(SpeakerWaveMiniportPins), SpeakerWaveMiniportPins,
    0, 0, NULL, SIZEOF_ARRAY(SpeakerWaveMiniportConnections), SpeakerWaveMiniportConnections, 0, NULL
};
