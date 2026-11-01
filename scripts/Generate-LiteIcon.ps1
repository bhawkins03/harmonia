$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$png = Join-Path $root 'Assets/harmonia-logo.png'
$ico = Join-Path $root 'Assets/harmonia.ico'
[byte[]]$data = [IO.File]::ReadAllBytes($png)
# ICO container with one PNG-encoded 64x64 image; preserves transparent pixels.
$header = [byte[]]@(0,0,1,0,1,0,64,64,0,0,1,0,32,0)
$size = [BitConverter]::GetBytes([int]$data.Length)
$offset = [BitConverter]::GetBytes([int]22)
$stream = [IO.File]::Create($ico)
try {
    $stream.Write($header,0,$header.Length)
    $stream.Write($size,0,4)
    $stream.Write($offset,0,4)
    $stream.Write($data,0,$data.Length)
} finally { $stream.Dispose() }
