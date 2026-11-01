[CmdletBinding()]
param([switch]$Uninstall)
$ErrorActionPreference='Stop'
if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this development installer as guest administrator.'}
$identity=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' -ErrorAction Stop
if($identity.VirtualMachineName -cne 'Harmonia Test Bench'){throw 'This development installer accepts only Harmonia Test Bench.'}
function Native($exe,$arguments){& $exe @arguments | Out-Host;if($LASTEXITCODE){throw "$exe failed: $LASTEXITCODE"}}
if($Uninstall) {
    $service=Get-CimInstance Win32_Service -Filter "Name='HarmoniaAudioTransport'"
    if(!$service -or !$service.PathName.StartsWith('"C:\Program Files\HarmoniaSuiteVM\Development\',[StringComparison]::OrdinalIgnoreCase)){throw 'No matching VM development service; nothing removed.'}
    if((Read-Host 'Stop and remove only the Suite VM development service? Type APPROVE') -cne 'APPROVE'){throw 'Cancelled'}
    if((Get-Service HarmoniaAudioTransport).Status -ne 'Stopped'){Stop-Service HarmoniaAudioTransport;(Get-Service HarmoniaAudioTransport).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
    Native sc.exe @('delete','HarmoniaAudioTransport')
    'Development service removed. Driver, files and user settings retained.';return
}
if(Get-Service HarmoniaAudioTransport -ErrorAction SilentlyContinue){throw 'Existing service found. Uninstall/review it before installing a development build.'}
if(!(Get-CimInstance Win32_SystemDriver -Filter "Name='HarmoniaVirtualAudio'" | Where-Object State -eq 'Running')){throw 'The validated VM driver must already be installed and running. This script does not install a kernel driver.'}
if((Read-Host 'Install the unsigned VM development app and restricted service? Type APPROVE') -cne 'APPROVE'){throw 'Cancelled'}
$manifest=Get-Content (Join-Path $PSScriptRoot 'development-hashes.json') -Raw | ConvertFrom-Json
if($manifest.Channel -cne 'suite-vm-development' -or $manifest.PublicUpdatesEnabled){throw 'Wrong development package channel.'}
$target='C:\Program Files\HarmoniaSuiteVM\Development\'+[guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $target -Force | Out-Null
$acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
$acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
foreach($entry in @(@{Sid='S-1-5-18';Rights='FullControl'},@{Sid='S-1-5-32-544';Rights='FullControl'},@{Sid='S-1-5-32-545';Rights='ReadAndExecute'},@{Sid='S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259';Rights='ReadAndExecute'})) {
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($entry.Sid),[Security.AccessControl.FileSystemRights]$entry.Rights,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
}
Set-Acl -LiteralPath $target -AclObject $acl
foreach($file in $manifest.Files) {
    if($file.Path -notmatch '^(App|Service)/' -and $file.Path -cne 'Install-SuiteDevelopment.ps1'){throw 'Unexpected development file.'}
    if($file.Path.Contains('..') -or $file.Path.Contains(':') -or $file.Path.Contains('\')){throw 'Unsafe development path.'}
    $source=Join-Path $PSScriptRoot $file.Path;$destination=Join-Path $target $file.Path
    if((Get-FileHash -LiteralPath $source).Hash -ne $file.SHA256){throw 'Development source hash mismatch.'}
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if((Get-FileHash -LiteralPath $destination).Hash -ne $file.SHA256){throw 'Development destination hash mismatch.'}
}
Native sc.exe @('create','HarmoniaAudioTransport','binPath=',('"'+$target+'\Service\HarmoniaAudioTransport.exe"'),'type=','own','start=','auto','obj=','NT AUTHORITY\LocalService')
try {
    # Windows PowerShell 5.1 can strip embedded quotes in native argv. Store
    # and verify the literal SCM path instead of leaving an unquoted path.
    $image='"'+$target+'\Service\HarmoniaAudioTransport.exe"'
    $changed=Invoke-CimMethod -InputObject (Get-CimInstance Win32_Service -Filter "Name='HarmoniaAudioTransport'") -MethodName Change -Arguments @{PathName=$image}
    if($changed.ReturnValue -ne 0){throw 'Service image path update failed.'}
    if((Get-CimInstance Win32_Service -Filter "Name='HarmoniaAudioTransport'").PathName -cne $image){throw 'Service image path quoting failed.'}
    Native sc.exe @('sidtype','HarmoniaAudioTransport','restricted')
    Native sc.exe @('failure','HarmoniaAudioTransport','reset=','86400','actions=','restart/5000/restart/15000/restart/30000')
    Native sc.exe @('sdset','HarmoniaAudioTransport','D:P(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)')
    Start-Service HarmoniaAudioTransport
    (Get-Service HarmoniaAudioTransport).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
} catch {if((Get-Service HarmoniaAudioTransport).Status -eq 'Stopped'){& sc.exe delete HarmoniaAudioTransport | Out-Host};throw}
"Suite VM development app installed at $target\App\Harmonia.exe"
'Launch it normally from the guest desktop, without administrator mode. Public updates remain disabled.'
