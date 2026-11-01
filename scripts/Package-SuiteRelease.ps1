[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublisherSha256,
    [Parameter(Mandatory)][string]$DriverPublisherSha256,
    [Parameter(Mandatory)][string]$SigningThumbprint,
    [Parameter(Mandatory)][string]$SignedDriverPath,
    [Parameter(Mandatory)][version]$DriverVersion,
    [version]$Version='0.3.0', [version]$ServiceVersion='0.3.0',
    [version]$AppServiceMinimum='0.3.0', [version]$AppServiceMaximum='0.3.0',
    [Parameter(Mandatory)][version]$ServiceDriverMinimum,
    [Parameter(Mandatory)][version]$ServiceDriverMaximum,
    [switch]$ApplicationOnly,
    [Parameter(Mandatory)][string]$SignToolPath,
    [string]$TimestampUrl='https://timestamp.digicert.com'
)
$ErrorActionPreference='Stop'
if($Version.Build -lt 0 -or $Version.Revision -ge 0 -or $ServiceVersion.Build -lt 0 -or $ServiceVersion.Revision -ge 0){throw 'Application and service release versions require major.minor.patch (three components).'}
if($PublisherSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $DriverPublisherSha256 -notmatch '^[0-9a-fA-F]{64}$'){throw 'Specify pinned SHA-256 publisher certificate fingerprints.'}
if(!([uri]$TimestampUrl).Scheme.Equals('https')){throw 'Timestamp service must use HTTPS.'}
if((Get-AuthenticodeSignature -LiteralPath $SignToolPath).Status -ne 'Valid'){throw 'A trusted SDK SignTool is required.'}
$cert=Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint"
$actual=[Security.Cryptography.SHA256]::Create().ComputeHash($cert.RawData)
if(([BitConverter]::ToString($actual)-replace '-','') -ne $PublisherSha256){throw 'Signing certificate differs from pinned publisher.'}
if($cert.Subject -eq 'CN=Harmonia Development Only'){throw 'VM development certificates cannot sign ordinary Suite releases.'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage=Join-Path $root ('dist/SuiteRelease-'+$Version+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
function Native([string]$Tool,[string[]]$Arguments){ & $Tool @Arguments; if($LASTEXITCODE){throw "$Tool failed: $LASTEXITCODE"} }
Push-Location $root
try {
    $pins=@("-p:SuitePublisherSha256=$PublisherSha256","-p:SuiteDriverPublisherSha256=$DriverPublisherSha256")
    foreach($component in @(@{Name='App';Project='Harmonia.csproj'},@{Name='Service';Project='Suite/Service/Harmonia.Suite.Service.csproj'},@{Name='Installer';Project='Suite/Installer/Harmonia.Suite.Installer.csproj'})) {
        if($ApplicationOnly -and $component.Name -eq 'Service'){continue}
        $componentVersion=if($component.Name -eq 'Service'){$ServiceVersion}else{$Version}
        $single=if($component.Name -eq 'Service'){'false'}else{'true'}
        Native dotnet (@('publish',$component.Project,'-c','Release','-r','win-x64','--self-contained','true',"-p:PublishSingleFile=$single",'-p:IncludeNativeLibrariesForSelfExtract=true',"-p:Version=$componentVersion",'-o',(Join-Path $stage $component.Name))+$pins)
        foreach($exe in Get-ChildItem (Join-Path $stage $component.Name) -Filter '*.exe') {
            Native $SignToolPath @('sign','/s','My','/sha1',$SigningThumbprint,'/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256',$exe.FullName)
            if((Get-AuthenticodeSignature $exe.FullName).Status -ne 'Valid'){throw 'Signed executable is not trusted.'}
        }
    }
    if(!$ApplicationOnly) {
        New-Item -ItemType Directory (Join-Path $stage 'Driver') | Out-Null
        foreach($name in 'HarmoniaVirtualAudio.inf','HarmoniaVirtualAudio.sys','harmoniavirtualaudio.cat') {Copy-Item -LiteralPath (Join-Path $SignedDriverPath $name) -Destination (Join-Path $stage 'Driver')}
        $driverVer=Select-String -LiteralPath (Join-Path $stage 'Driver/HarmoniaVirtualAudio.inf') -Pattern '^DriverVer\s*=\s*[^,]+,\s*(\S+)\s*$'
        if(!$driverVer -or [version]$driverVer.Matches[0].Groups[1].Value -ne $DriverVersion){throw 'Manifest driver version differs from the signed INF.'}
        # The exact catalog must pass Windows kernel signing policy and hash membership.
        Native $SignToolPath @('verify','/kp','/v','/c',(Join-Path $stage 'Driver/harmoniavirtualaudio.cat'),(Join-Path $stage 'Driver/HarmoniaVirtualAudio.sys'))
        Native $SignToolPath @('verify','/kp','/v','/c',(Join-Path $stage 'Driver/harmoniavirtualaudio.cat'),(Join-Path $stage 'Driver/HarmoniaVirtualAudio.inf'))
    }
    $files=@(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
        @{Path=$_.FullName.Substring($stage.Length+1).Replace('\','/');Sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash;Size=$_.Length}
    })
    $manifest=@{Schema=1;Channel='suite';Version="$Version";ServiceVersion="$ServiceVersion";DriverVersion="$DriverVersion";
        AppServiceMinimum="$AppServiceMinimum";AppServiceMaximum="$AppServiceMaximum";ServiceDriverMinimum="$ServiceDriverMinimum";ServiceDriverMaximum="$ServiceDriverMaximum";
        UpdateService=(!$ApplicationOnly);UpdateDriver=(!$ApplicationOnly);TransportVersion=1;Files=$files}
    $manifestPath=Join-Path $stage 'suite-manifest.json'
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Add-Type -AssemblyName System.Security
    $cms=[Security.Cryptography.Pkcs.SignedCms]::new([Security.Cryptography.Pkcs.ContentInfo]::new([IO.File]::ReadAllBytes($manifestPath)),$true)
    $signer=[Security.Cryptography.Pkcs.CmsSigner]::new($cert);$signer.DigestAlgorithm=[Security.Cryptography.Oid]::new('2.16.840.1.101.3.4.2.1')
    $cms.ComputeSignature($signer);[IO.File]::WriteAllBytes((Join-Path $stage 'suite-manifest.p7s'),$cms.Encode())
    # Use the same verifier as clients before creating an archive.
    Native (Join-Path $stage 'Installer/Harmonia.Suite.Installer.exe') @('--verify',$stage)
    $zip=Join-Path $root ('dist/Harmonia-Suite-'+$Version+'-win-x64.zip')
    $entries=Get-ChildItem $stage -Recurse -File
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
    try { foreach($file in $entries){$relative=$file.FullName.Substring($stage.Length+1).Replace('\','/');[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative) | Out-Null} }
    finally {$archive.Dispose()}
    "Verified Suite release archive: $zip (publish as asset Harmonia-Suite-win-x64.zip under v$Version-suite only)."
} finally {Pop-Location}
