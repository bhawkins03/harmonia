[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$CertificatePath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
if (!('HarmoniaPackageVerifier' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class HarmoniaPackageVerifier {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileInfo {
  public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path;
  public IntPtr File,Subject;
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustData {
  public uint Size; public IntPtr Policy,Sip; public uint UI,Revocation,Choice;
  public IntPtr File; public uint StateAction; public IntPtr State,URL;
  public uint Flags,Context; public IntPtr Settings;
 }
 [DllImport("wintrust.dll",ExactSpelling=true)] static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
 public static uint Verify(string path) {
  var f=new FileInfo {Size=(uint)Marshal.SizeOf(typeof(FileInfo)),Path=path};
  IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfo)));
  Marshal.StructureToPtr(f,p,false);
  var data=new TrustData {Size=(uint)Marshal.SizeOf(typeof(TrustData)),UI=2,Choice=1,File=p,StateAction=1,Flags=0x1000};
  var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
  try { return unchecked((uint)WinVerifyTrust(new IntPtr(-1),ref action,ref data)); }
  finally {data.StateAction=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);Marshal.DestroyStructure(p,typeof(FileInfo));Marshal.FreeHGlobal(p);}
 }
 [DllImport("wintrust.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 public static extern IntPtr CryptCATOpen(string file,uint flags,IntPtr provider,uint version,uint encoding);
 [DllImport("wintrust.dll", CharSet=CharSet.Unicode)] public static extern IntPtr CryptCATGetMemberInfo(IntPtr catalog,string tag);
 [DllImport("wintrust.dll")] public static extern bool CryptCATClose(IntPtr catalog);
 [DllImport("wintrust.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 public static extern bool CryptCATAdminAcquireContext2(out IntPtr context,IntPtr subsystem,string algorithm,IntPtr policy,uint flags);
 [DllImport("wintrust.dll", SetLastError=true)]
 public static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr context,IntPtr file,ref uint size,byte[] hash,uint flags);
 [DllImport("wintrust.dll")] public static extern bool CryptCATAdminReleaseContext(IntPtr context,uint flags);
}
"@
}
$certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'Development certificate is not currently valid.' }
$cat=Join-Path $PackagePath 'HarmoniaVirtualAudio.cat'
$sys=Join-Path $PackagePath 'HarmoniaVirtualAudio.sys'
$cms=[Security.Cryptography.Pkcs.SignedCms]::new()
$cms.Decode([IO.File]::ReadAllBytes($cat));$cms.CheckSignature($true)
if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Catalog signer mismatch.' }
foreach ($file in $sys,$cat) {
    $signature=Get-AuthenticodeSignature -LiteralPath $file
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Signer mismatch: $file" }
    $status=[HarmoniaPackageVerifier]::Verify([IO.Path]::GetFullPath($file))
    # WinVerifyTrust validates content. Allow only success or the exact untrusted
    # self-signed development root HRESULT; never accept generic UnknownError.
    if ($status -ne 0 -and $status -ne [uint32]2148204809) { throw ('Invalid signature: {0} HRESULT 0x{1:X8}' -f $file,$status) }
}
$handle=[HarmoniaPackageVerifier]::CryptCATOpen($cat,0,[IntPtr]::Zero,0x200,0)
if ($handle -eq [IntPtr]::Zero -or $handle -eq [IntPtr](-1)) { throw 'Cannot open catalog.' }
try {
    $context=[IntPtr]::Zero
    if (![HarmoniaPackageVerifier]::CryptCATAdminAcquireContext2([ref]$context,[IntPtr]::Zero,'SHA256',[IntPtr]::Zero,0)) { throw 'Cannot acquire catalog context.' }
    try {
        foreach ($file in $sys,(Join-Path $PackagePath 'HarmoniaVirtualAudio.inf')) {
            $stream=[IO.File]::OpenRead($file)
            try {
                [uint32]$size=0
                $null=[HarmoniaPackageVerifier]::CryptCATAdminCalcHashFromFileHandle2($context,$stream.SafeFileHandle.DangerousGetHandle(),[ref]$size,$null,0)
                if (!$size) { throw 'Cannot size catalog hash.' }
                $hash=New-Object byte[] $size
                if (![HarmoniaPackageVerifier]::CryptCATAdminCalcHashFromFileHandle2($context,$stream.SafeFileHandle.DangerousGetHandle(),[ref]$size,$hash,0)) { throw 'Catalog hash failed.' }
                $tag=[BitConverter]::ToString($hash).Replace('-','')
                if ([HarmoniaPackageVerifier]::CryptCATGetMemberInfo($handle,$tag) -eq [IntPtr]::Zero) { throw "File absent from signed catalog: $file" }
            } finally { $stream.Dispose() }
        }
    } finally { [HarmoniaPackageVerifier]::CryptCATAdminReleaseContext($context,0) | Out-Null }
} finally { [HarmoniaPackageVerifier]::CryptCATClose($handle) | Out-Null }
[pscustomobject]@{Status='PASS';Signer=$certificate.Thumbprint;Detail='SYS/CAT signatures and INF/SYS catalog membership verified; development root may be untrusted on host.'}
