[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot "../../VirtualAudio/driver/Package/x64/$Configuration/HarmoniaPackage"
$certificate=Join-Path $source 'HarmoniaDevelopment.cer'
& "$PSScriptRoot/Test-PackageSignature.ps1" -PackagePath $source -CertificatePath $certificate | Out-Host
$temp=Join-Path ([IO.Path]::GetTempPath()) ('HarmoniaSignatureTests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$names='HarmoniaVirtualAudio.inf','HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.cat'
try {
    foreach ($name in $names) {
        foreach ($file in $names) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination $temp -Force }
        $path=Join-Path $temp $name
        if ($name -like '*.inf') { Add-Content -LiteralPath $path '; tampered test copy' }
        else {
            $bytes=[IO.File]::ReadAllBytes($path)
            $offset=if ($name -like '*.sys') {1024} else {$bytes.Length-1}
            $bytes[$offset]=$bytes[$offset] -bxor 1
            [IO.File]::WriteAllBytes($path,$bytes)
        }
        $rejected=$false
        try { & "$PSScriptRoot/Test-PackageSignature.ps1" -PackagePath $temp -CertificatePath $certificate | Out-Null }
        catch { $rejected=$true; Write-Host "PASS: tampered $name rejected: $($_.Exception.Message)" }
        if (!$rejected) { throw "Tampered $name was accepted." }
    }
} finally {
    foreach ($name in $names) { Remove-Item -LiteralPath (Join-Path $temp $name) -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $temp
}
'PASS: valid development package accepted; independent SYS/INF/CAT tampering rejected. No VM accessed.'
