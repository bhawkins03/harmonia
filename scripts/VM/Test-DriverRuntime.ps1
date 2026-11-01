[CmdletBinding()]
param([Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,[switch]$ObserveTransport,[switch]$ExpectProtectedStream)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/Workflow.ps1"
$vm=Assert-BenchSession $Session
$null=Get-BenchCheckpoint $vm
$hasDesktop=Invoke-Command -Session $Session -ScriptBlock { @(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0).Count -gt 0 }
if(!$hasDesktop) {
    $null=Read-Host 'Sign into Harmonia Test Bench in VMConnect and leave its desktop session open; press Enter when ready (no password in this terminal)'
    $hasDesktop=Invoke-Command -Session $Session -ScriptBlock { @(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0).Count -gt 0 }
    if(!$hasDesktop){throw 'No interactive guest desktop detected. Content tests require guest sign-in; no silent-content result will be counted as a pass.'}
}
$source=Join-Path $PSScriptRoot '../../VirtualAudio/.work/tests/runtime'
$destination='C:\HarmoniaTest\Runtime\'+[guid]::NewGuid().ToString('N')
Confirm-BenchAction 'Run audio/transport probes, a temporary SYSTEM task and a restricted LocalService test service (no default audio changes)'
$manifest=foreach ($name in 'RuntimeTransportTests.exe','RuntimeAudioTests.exe','ContentRightsPolicyTests.exe') {
    $path=Join-Path $source $name
    [pscustomobject]@{Name=$name;SHA256=(Get-FileHash -LiteralPath $path).Hash}
}
Invoke-Command -Session $Session -ArgumentList $destination -ScriptBlock {
    param($path)
    $ErrorActionPreference='Stop'
    if (Get-Service HarmoniaAudioTransport -ErrorAction SilentlyContinue) { throw 'Existing HarmoniaAudioTransport service; will not replace it.' }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    New-Item -ItemType Directory -Path C:\HarmoniaTest\Logs -Force | Out-Null
}
foreach ($entry in $manifest) { Copy-Item -LiteralPath (Join-Path $source $entry.Name) -Destination $destination -ToSession $Session }
Invoke-Command -Session $Session -ArgumentList $destination,$manifest,([bool]$ObserveTransport),([bool]$ExpectProtectedStream) -ScriptBlock {
    param($path,$manifest,$observe,$expectProtected)
    $ErrorActionPreference='Stop'
    foreach ($entry in $manifest) { if ((Get-FileHash -LiteralPath (Join-Path $path $entry.Name)).Hash -ne $entry.SHA256) { throw 'Probe transfer hash mismatch.' } }
    function Native($exe,$arguments) { & $exe @arguments | Out-Host; if ($LASTEXITCODE) { throw "$exe failed: $LASTEXITCODE" } }
    Native "$path\ContentRightsPolicyTests.exe" @()
    function RunLogged($exe,$mode,$log) {
        $params=@{FilePath=$exe;Wait=$true;PassThru=$true;WindowStyle='Hidden';RedirectStandardOutput=$log;RedirectStandardError=($log+'.err')}
        if ($mode) { $params.ArgumentList=$mode }
        (Start-Process @params).ExitCode
    }
    $denialFailed=(RunLogged "$path\RuntimeTransportTests.exe" 'deny' C:\HarmoniaTest\Logs\RuntimeTransportAdmin.txt) -ne 0
    $audioFailed=(RunLogged "$path\RuntimeAudioTests.exe" '' C:\HarmoniaTest\Logs\RuntimeAudio.txt) -ne 0
    $taskName='HarmoniaDriverProbe-'+[guid]::NewGuid().ToString('N')
    $probeMode=if($expectProtected){'observe-protected'}elseif($observe){'observe'}else{'system'}
    $ready='C:\HarmoniaTest\Logs\TransportObserverReady.txt'
    if($observe){Remove-Item -LiteralPath $ready -ErrorAction SilentlyContinue}
    $action=New-ScheduledTaskAction -Execute $env:ComSpec -Argument ('/d /c ""'+$path+'\RuntimeTransportTests.exe" '+$probeMode+' > C:\HarmoniaTest\Logs\RuntimeTransportSystem.txt 2>&1"')
    $principal=New-ScheduledTaskPrincipal -UserId SYSTEM -LogonType ServiceAccount -RunLevel Highest
    try {
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal | Out-Null
        Start-ScheduledTask -TaskName $taskName
        if($observe) {
            $readyDeadline=(Get-Date).AddSeconds(15)
            while(!(Test-Path -LiteralPath $ready)) {
                if((Get-Date) -gt $readyDeadline){throw 'PCM observer did not attach; inspect RuntimeTransportSystem.txt.'}
                Start-Sleep -Milliseconds 100
            }
            $audioFailed=(RunLogged "$path\RuntimeAudioTests.exe" '' C:\HarmoniaTest\Logs\RuntimeAudioObserved.txt) -ne 0 -or $audioFailed
        }
        $deadline=(Get-Date).AddSeconds(60)
        do {
            Start-Sleep 1
            $info=Get-ScheduledTaskInfo -TaskName $taskName
            $state=(Get-ScheduledTask -TaskName $taskName).State
            if ((Get-Date) -gt $deadline) { throw 'SYSTEM probe timeout; inspect VM and task.' }
        } while ($state -eq 'Running' -or $info.LastRunTime.Year -lt 2026)
        if ($info.LastTaskResult -ne 0) {
            $detail=Get-Content C:\HarmoniaTest\Logs\RuntimeTransportSystem.txt -Raw
            throw "SYSTEM transport probe failed ($($info.LastTaskResult)): $detail"
        }
    } finally {
        if ((Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue).State -ne 'Running') { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue }
    }
    $serviceLog='C:\HarmoniaTest\Logs\RuntimeTransportService.txt'
    New-Item -ItemType File -Path $serviceLog -Force | Out-Null
    $sid='S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259'

    $created=$false
    try {
        $serviceMode=if($expectProtected){'service-observe-protected'}elseif($observe){'service-observe'}else{'service'}
        Native sc.exe @('create','HarmoniaAudioTransport','binPath=',('"'+$path+'\RuntimeTransportTests.exe" '+$serviceMode),'type=','own','start=','demand','obj=','NT AUTHORITY\LocalService')
        $created=$true
        Native sc.exe @('sidtype','HarmoniaAudioTransport','restricted')
        Native icacls.exe @($serviceLog,'/grant',('*'+$sid+':(W)'))
        $serviceReady='C:\HarmoniaTest\Logs\TransportServiceReady.txt'
        if($observe) {
            New-Item -ItemType File -Path $serviceReady -Force | Out-Null
            Native icacls.exe @($serviceReady,'/grant',('*'+$sid+':(W)'))
        }
        Native sc.exe @('start','HarmoniaAudioTransport')
        if($observe) {
            $readyDeadline=(Get-Date).AddSeconds(15)
            while((Get-Item -LiteralPath $serviceReady).Length -eq 0) {
                if((Get-Date) -gt $readyDeadline){throw 'Restricted service did not attach for PCM observation; inspect its log.'}
                Start-Sleep -Milliseconds 100
            }
            $audioFailed=(RunLogged "$path\RuntimeAudioTests.exe" '' C:\HarmoniaTest\Logs\RuntimeAudioServiceObserved.txt) -ne 0 -or $audioFailed
        }
        $deadline=(Get-Date).AddSeconds(60)
        do {
            Start-Sleep 1
            $svc=Get-CimInstance Win32_Service -Filter "Name='HarmoniaAudioTransport'"
            if ((Get-Date) -gt $deadline) { throw 'Restricted service probe timeout; left for inspection.' }
        } while ($svc.State -ne 'Stopped')
        if ($svc.ExitCode -ne 0) { throw "Restricted service probe failed: $($svc.ExitCode) / $($svc.ServiceSpecificExitCode): $(Get-Content $serviceLog -Raw)" }
    } finally {
        if ($created -and (Get-Service HarmoniaAudioTransport).Status -eq 'Stopped') { Native sc.exe @('delete','HarmoniaAudioTransport') }
    }
    if ($denialFailed -or $audioFailed) { Write-Warning 'At least one audio or access-denial probe failed; see logs below.' }
    foreach ($file in 'RuntimeTransportAdmin.txt','RuntimeAudio.txt','RuntimeTransportSystem.txt','RuntimeTransportService.txt') {
        Write-Output "--- $file ---"
        Get-Content -LiteralPath (Join-Path C:\HarmoniaTest\Logs $file)
        Get-Content -LiteralPath (Join-Path C:\HarmoniaTest\Logs ($file+'.err')) -ErrorAction SilentlyContinue
    }
    if ($denialFailed -or $audioFailed) { throw 'Runtime audio/access probe failures; logs retained.' }
}
