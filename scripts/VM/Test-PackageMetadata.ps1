[CmdletBinding()]
param([string]$InfPath)
# Validate the actual OEM default blob against the driver format policy.
$ErrorActionPreference='Stop'
$path=if($InfPath){$InfPath}else{Join-Path $PSScriptRoot '../../VirtualAudio/driver/TabletAudioSample/HarmoniaVirtualAudio.inx'}
$inf=Get-Content -LiteralPath $path -Raw
$match=[regex]::Match($inf,'(?m)^HKR,EP\\0,%OEMFormat%,0x00000001,((?:[^\r\n]*\\\r?\n)*[^\r\n]*)')
if (!$match.Success) { throw 'OEM format missing.' }
$hex=$match.Groups[1].Value -replace '\\\r?\n','' -replace '\s',''
[byte[]]$blob=@($hex.Split(',') | ForEach-Object { [Convert]::ToByte($_,16) })
if ($blob.Length -ne 48 -or [BitConverter]::ToUInt32($blob,0) -ne 65 -or [BitConverter]::ToUInt32($blob,4) -ne 40) { throw 'Invalid serialized VT_BLOB format.' }
if ([BitConverter]::ToUInt16($blob,8) -ne 65534 -or [BitConverter]::ToUInt16($blob,10) -ne 6 -or [BitConverter]::ToUInt32($blob,12) -ne 48000 -or [BitConverter]::ToUInt32($blob,16) -ne 1152000 -or [BitConverter]::ToUInt16($blob,20) -ne 24 -or [BitConverter]::ToUInt16($blob,22) -ne 32 -or [BitConverter]::ToUInt16($blob,24) -ne 22 -or [BitConverter]::ToUInt16($blob,26) -ne 32 -or [BitConverter]::ToUInt32($blob,28) -ne 0x60f) { throw 'OEM format does not match Float32 48kHz 5.1-side descriptor.' }
$subtype=[guid]::new([byte[]]$blob[32..47])
if ($subtype -ne [guid]'00000003-0000-0010-8000-00aa00389b71') { throw 'OEM subtype is not IEEE Float.' }
'PASS: serialized INF OEM default matches supported native 48kHz Float32 5.1-side format.'
$descriptor=Get-Content "$PSScriptRoot/../../VirtualAudio/driver/EndpointsCommon/speakerwavtable.h" -Raw
$concrete=[regex]::Match($descriptor,'#define HARMONIA_FORMAT[\s\S]*?#undef HARMONIA_FORMAT').Value
if (!$concrete -or $concrete -match 'SUBTYPE_WILDCARD' -or ([regex]::Matches($concrete,'SUBTYPE_IEEE_FLOAT')).Count -ne 2) { throw 'Concrete KS and WAVEFORMATEXTENSIBLE formats must both explicitly identify IEEE Float; wildcard belongs only in discovery ranges.' }
'PASS: concrete KS/WAVE descriptors preserve exact IEEE Float identity.'
