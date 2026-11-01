[CmdletBinding()]
param([Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,[Parameter(Mandatory)][string]$ArchivePath)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/Workflow.ps1"
$vm=Assert-BenchSession $Session
$null=Get-BenchCheckpoint $vm
Confirm-BenchAction 'Test installation and controlled removal of the VM-only development app/service package, preserving the kernel driver'
$stage='C:\HarmoniaTest\DeploymentProbe-'+[guid]::NewGuid().ToString('N')
Invoke-Command -Session $Session -ArgumentList $stage -ScriptBlock {param($path) New-Item -ItemType Directory -Path $path -Force | Out-Null}
Copy-Item -LiteralPath $ArchivePath -Destination ($stage+'\package.zip') -ToSession $Session
$hash=(Get-FileHash -LiteralPath $ArchivePath).Hash
try {
    Invoke-Command -Session $Session -ArgumentList $stage,$hash -ScriptBlock {
        param($path,$hash)
        $ErrorActionPreference='Stop'
        $ProgressPreference='SilentlyContinue'
        Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
        if((Get-FileHash -LiteralPath ($path+'\package.zip')).Hash -ne $hash){throw 'Development archive transfer mismatch.'}
        $driverBefore=Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceName -eq 'Harmonia Virtual Audio'
        Expand-Archive -LiteralPath ($path+'\package.zip') -DestinationPath ($path+'\expanded')
        $installer=$path+'\expanded\Install-SuiteDevelopment.ps1'
        try {
            & $installer
            $service=Get-CimInstance Win32_Service -Filter "Name='HarmoniaAudioTransport'"
            $sid=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\HarmoniaAudioTransport'
            if($service.State -ne 'Running' -or $service.StartName -ine 'NT AUTHORITY\LocalService' -or $sid.ServiceSidType -ne 3 -or !$service.PathName.StartsWith('"C:\Program Files\HarmoniaSuiteVM\Development\')){throw 'Development service health, identity, restricted SID or quoted image path failed.'}
            'PASS: development service installed, running as restricted LocalService with a quoted protected executable path.'
        } finally {if(Get-Service HarmoniaAudioTransport -ErrorAction SilentlyContinue){& $installer -Uninstall}}
        if(Get-Service HarmoniaAudioTransport -ErrorAction SilentlyContinue){throw 'Development service was not removed.'}
        $driverAfter=Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceName -eq 'Harmonia Virtual Audio'
        if($driverBefore.InfName -cne $driverAfter.InfName -or $driverBefore.DriverVersion -cne $driverAfter.DriverVersion){throw 'Kernel driver changed during user-mode deployment test.'}
        'PASS: controlled development uninstall preserved the existing driver and user configuration.'
    }
} finally { & "$PSScriptRoot/Collect-Logs.ps1" -Session $Session }
