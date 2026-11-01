/* Copyright (c) Microsoft Corporation. All rights reserved.
 * Harmonia endpoint registration derived from the MIT-licensed SysVAD sample.
 */
#pragma once
#include "speakertopo.h"
#include "speakertoptable.h"
#include "speakerwavtable.h"
NTSTATUS CreateMiniportWaveRTSYSVAD(PUNKNOWN *, REFCLSID, PUNKNOWN, POOL_FLAGS, PUNKNOWN, PVOID, PENDPOINT_MINIPAIR);
NTSTATUS CreateMiniportTopologySYSVAD(PUNKNOWN *, REFCLSID, PUNKNOWN, POOL_FLAGS, PUNKNOWN, PVOID, PENDPOINT_MINIPAIR);
static PHYSICALCONNECTIONTABLE SpeakerTopologyPhysicalConnections[] = {
    { KSPIN_TOPO_WAVEOUT_SOURCE, KSPIN_WAVE_RENDER3_SOURCE, CONNECTIONTYPE_WAVE_OUTPUT }
};
static ENDPOINT_MINIPAIR SpeakerMiniports = {
    eSpeakerDevice,
    L"HarmoniaTopology", NULL, CreateMiniportTopologySYSVAD, &SpeakerTopoMiniportFilterDescriptor, 0, NULL,
    L"HarmoniaWave", NULL, CreateMiniportWaveRTSYSVAD, &SpeakerWaveMiniportFilterDescriptor, 0, NULL,
    SPEAKER_DEVICE_MAX_CHANNELS, SpeakerPinDeviceFormatsAndModes, SIZEOF_ARRAY(SpeakerPinDeviceFormatsAndModes),
    SpeakerTopologyPhysicalConnections, SIZEOF_ARRAY(SpeakerTopologyPhysicalConnections),
    0, NULL, 0, NULL
};
static PENDPOINT_MINIPAIR g_RenderEndpoints[] = { &SpeakerMiniports };
#define g_cRenderEndpoints SIZEOF_ARRAY(g_RenderEndpoints)
#define g_MaxMiniports (g_cRenderEndpoints * 2)

