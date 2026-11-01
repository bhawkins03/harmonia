[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
# Called only by the live workflow; private key stays in host CurrentUser\My.
$pin = Get-Content "$PSScriptRoot/../../VirtualAudio/upstream.json" -Raw | ConvertFrom-Json
$kit = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin/$($pin.kitVersion)"
$signTool = Join-Path $kit 'x64/signtool.exe'
$inf2cat = Join-Path $kit 'x86/Inf2Cat.exe'
foreach ($tool in $signTool,$inf2cat) { if (!(Test-Path -LiteralPath $tool)) { throw "Required WDK tool missing: $tool" } }
$null = Get-HarmoniaInfIdentity (Join-Path $PackagePath 'HarmoniaVirtualAudio.inf')
$certificates = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq 'CN=Harmonia Development Only' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) -and
    $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3'
})
if ($certificates.Count -gt 1) { throw 'Multiple development certificates; resolve host CurrentUser certificate ambiguity manually.' }
$certificate = $certificates | Select-Object -First 1
if (!$certificate) {
    Confirm-BenchAction 'Create development-only code-signing certificate in HOST CurrentUser My (no host trust changes)'
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Harmonia Development Only' -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(1)
}
$publicCertificate = Join-Path $PackagePath 'HarmoniaDevelopment.cer'
Export-Certificate -Cert $certificate -FilePath $publicCertificate -Force | Out-Null
$sys = Join-Path $PackagePath 'HarmoniaVirtualAudio.sys'
$cat = Join-Path $PackagePath 'HarmoniaVirtualAudio.cat'
Invoke-BenchNative $signTool @('sign','/s','My','/sha1',$certificate.Thumbprint,'/fd','SHA256',$sys)
# Inf2Cat must hash the already embedded-signed SYS.
Invoke-BenchNative $inf2cat @("/driver:$PackagePath",'/os:10_GE_X64','/uselocaltime')
Invoke-BenchNative $signTool @('sign','/s','My','/sha1',$certificate.Thumbprint,'/fd','SHA256',$cat)
& "$PSScriptRoot/Test-PackageSignature.ps1" -PackagePath $PackagePath -CertificatePath $publicCertificate | Out-Host
