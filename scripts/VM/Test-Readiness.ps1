[CmdletBinding()]
param([System.Management.Automation.Runspaces.PSSession]$Session)
. "$PSScriptRoot/Common.ps1"
function Check {
    param([string]$Name,[scriptblock]$Action)
    try { [pscustomobject]@{ Check=$Name; Status='PASS'; Detail=(& $Action | Out-String).Trim() } }
    catch { [pscustomobject]@{ Check=$Name; Status='BLOCKED'; Detail=$_.Exception.Message } }
}
Check 'Host administrator' {
    if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run an elevated host PowerShell for Hyper-V management.' }
    'Elevated'
}
Check 'PowerShell Direct / Hyper-V commands' { Get-Command Get-VM,Get-VMSnapshot,Checkpoint-VM,New-PSSession,Copy-Item | Select-Object Name,Source }
Check 'VM' {
    $vm = Get-VM -Name $script:BenchName
    if ($vm.Generation -ne 2 -or $vm.State -ne 'Running') { throw 'Expected running Generation 2 VM.' }
    $vm | Select-Object Name,Id,State,Generation
}
Check 'Pre-driver checkpoint' { $vm = Get-VM -Name $script:BenchName; Get-BenchCheckpoint $vm | Select-Object Name,Id,CreationTime }
foreach ($configuration in 'Debug','Release') {
    Check "Host $configuration package" {
        $path = Join-Path $PSScriptRoot "../../VirtualAudio/driver/Package/x64/$configuration/HarmoniaPackage"
        foreach ($name in 'HarmoniaVirtualAudio.inf','HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.cat') {
            $file = Get-Item -LiteralPath (Join-Path $path $name)
            if (!$file.Length) { throw "Empty artifact: $name" }
            $file | Select-Object Name,Length
        }
        'Driver signature: ' + (Get-AuthenticodeSignature (Join-Path $path 'HarmoniaVirtualAudio.sys')).Status
    }
}
if (!$Session) {
    [pscustomobject]@{ Check='Guest checks'; Status='BLOCKED'; Detail='No session supplied. Connect interactively, then rerun with -Session $session.' }
    return
}
$null = Assert-BenchSession $Session
Invoke-Command -Session $Session -ScriptBlock {
    $ErrorActionPreference = 'Stop'
    function Result($name,$ok,$detail) { [pscustomobject]@{ Check=$name; Status=$(if($ok){'PASS'}else{'BLOCKED'}); Detail=$detail } }
    $os = Get-CimInstance Win32_OperatingSystem
    Result 'Guest OS' ($os.Caption -match 'Windows 11' -and $os.OSArchitecture -eq '64-bit') "$($os.Caption), build $($os.BuildNumber), $($os.OSArchitecture)"
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Result 'Guest administrator' $admin ([Security.Principal.WindowsIdentity]::GetCurrent().Name)
    foreach ($name in 'Audiosrv','AudioEndpointBuilder') {
        $svc = Get-Service $name
        Result "Guest $name" ($svc.Status -eq 'Running') ([string]$svc.Status)
    }
    Result 'Guest test directory' (Test-Path -LiteralPath 'C:\HarmoniaTest' -PathType Container) 'C:\HarmoniaTest (read-only existence check)'
    $drivers = @(Get-WindowsDriver -Online -All | Where-Object OriginalFileName -match 'Harmonia')
    $devices = @(Get-PnpDevice | Where-Object { $_.InstanceId -match '^ROOT\\HarmoniaVirtualAudio' -or $_.FriendlyName -match 'Harmonia' })
    $services = @(Get-CimInstance Win32_SystemDriver | Where-Object Name -match 'Harmonia')
    Result 'Guest Harmonia installation baseline' ($drivers.Count -eq 0 -and $devices.Count -eq 0 -and $services.Count -eq 0) (@{ DriverStore=$drivers; Devices=$devices; Services=$services } | ConvertTo-Json -Depth 4)
    Get-PnpDevice | Where-Object { $_.Class -in @('Media','AudioEndpoint') } | Select-Object Status,Class,FriendlyName,InstanceId
}
