[CmdletBinding()]
param([Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,[switch]$ExpectProtected,[string]$RoutingPlanPath)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/Workflow.ps1"
$vm=Assert-BenchSession $Session
$null=Get-BenchCheckpoint $vm
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output=Join-Path $root ('Suite/.work/VM-'+[guid]::NewGuid().ToString('N'))
foreach($component in @(@{Project='Suite/Service/Harmonia.Suite.Service.csproj';Folder='Service'},@{Project='Suite/Tests/Harmonia.Suite.Tests.csproj';Folder='Tests'},@{Project='Suite/UiTests/Harmonia.Suite.UiTests.csproj';Folder='UI'},@{Project='VirtualAudio/tests/TransportProtocol.Tests/TransportProtocol.Tests.csproj';Folder='Protocol'})) {
    $single=if($component.Folder -eq 'Service'){'false'}else{'true'}
    & dotnet publish (Join-Path $root $component.Project) -c Release -r win-x64 --self-contained true "-p:PublishSingleFile=$single" -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $output $component.Folder)
    if($LASTEXITCODE){throw 'Suite probe publication failed.'}
}
$desktop=Invoke-Command -Session $Session -ScriptBlock { @(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object SessionId -gt 0).Count -gt 0 }
if(!$desktop){throw 'Sign into the guest Windows desktop before Suite service testing.'}
Confirm-BenchAction 'Deploy and test the Suite service as restricted LocalService, run an interactive transport probe and generated audio (no kernel changes)'
$guest='C:\Program Files\HarmoniaSuiteVM\'+[guid]::NewGuid().ToString('N')
$files=@(Get-ChildItem (Join-Path $output 'Service') -File | Where-Object Extension -ne '.pdb' | ForEach-Object { @{Source=$_.FullName;Name=$_.Name} })
$files+=@(
    @{Source=(Join-Path $output 'Tests/Harmonia.Suite.Tests.exe');Name='Harmonia.Suite.Tests.exe'},
    @{Source=(Join-Path $output 'UI/Harmonia.Suite.UiTests.exe');Name='Harmonia.Suite.UiTests.exe'},
    @{Source=(Join-Path $output 'Protocol/TransportProtocol.Tests.exe');Name='TransportProtocol.Tests.exe'},
    @{Source=(Join-Path $root 'VirtualAudio/.work/tests/transport-Release/native.exe');Name='ProtocolNative.exe'},
    @{Source=(Join-Path $root 'VirtualAudio/.work/tests/transport-Release/queue.exe');Name='QueueTests.exe'},
    @{Source=(Join-Path $root 'VirtualAudio/.work/tests/transport-Release/rights.exe');Name='RightsTests.exe'},
    @{Source=(Join-Path $root 'VirtualAudio/tests/vectors/transport-v1.txt');Name='transport-v1.txt'},
    @{Source=(Join-Path $root 'VirtualAudio/.work/tests/runtime/RuntimeAudioTests.exe');Name='RuntimeAudioTests.exe'}
)
Invoke-Command -Session $Session -ArgumentList $guest -ScriptBlock {
    param($path)
    if(Get-Service HarmoniaAudioTransport -ErrorAction SilentlyContinue){throw 'An existing transport service must be reviewed, not replaced.'}
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach($entry in @(@{Sid='S-1-5-18';Rights='FullControl'},@{Sid='S-1-5-32-544';Rights='FullControl'},@{Sid='S-1-5-32-545';Rights='ReadAndExecute'},@{Sid='S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259';Rights='ReadAndExecute'})) {
        $rule=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($entry.Sid),[Security.AccessControl.FileSystemRights]$entry.Rights,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $path -AclObject $acl
}
foreach($file in $files){Copy-Item -LiteralPath $file.Source -Destination (Join-Path $guest $file.Name) -ToSession $Session}
if($RoutingPlanPath){Copy-Item -LiteralPath $RoutingPlanPath -Destination (Join-Path $guest 'routing-plan.json') -ToSession $Session}
$manifest=@($files | ForEach-Object {@{Name=$_.Name;SHA256=(Get-FileHash $_.Source).Hash}})
try {
    Invoke-Command -Session $Session -ArgumentList $guest,$manifest,([bool]$ExpectProtected),([bool]$RoutingPlanPath) -ScriptBlock {
        param($path,$manifest,$protected,$route)
        $ErrorActionPreference='Stop'
        foreach($entry in $manifest){if((Get-FileHash (Join-Path $path $entry.Name)).Hash -ne $entry.SHA256){throw 'Suite transfer hash mismatch.'}}
        function Native($tool,$arguments){& $tool @arguments | Out-Host;if($LASTEXITCODE){throw "$tool failed: $LASTEXITCODE"}}
        $protocol=Join-Path 'C:\HarmoniaTest\Logs' ('SuiteProtocol-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $protocol | Out-Null
        Native "$path\ProtocolNative.exe" @("$path\transport-v1.txt",$protocol)
        Native "$path\QueueTests.exe" @()
        Native "$path\RightsTests.exe" @()
        Native "$path\TransportProtocol.Tests.exe" @("$path\transport-v1.txt",$protocol)
        foreach($suffix in 'encoded','fuzz') {
            if(Compare-Object ([IO.File]::ReadAllLines("$protocol\native-$suffix.txt")) ([IO.File]::ReadAllLines("$protocol\managed-$suffix.txt")) -SyncWindow 0){throw 'Native/managed transport parity failed.'}
        }
        Native sc.exe @('create','HarmoniaAudioTransport','binPath=',('"'+$path+'\HarmoniaAudioTransport.exe"'),'type=','own','start=','demand','obj=','NT AUTHORITY\LocalService')
        $changed=Invoke-CimMethod -InputObject (Get-CimInstance Win32_Service -Filter "Name='HarmoniaAudioTransport'") -MethodName Change -Arguments @{PathName=('"'+$path+'\HarmoniaAudioTransport.exe"')}
        if($changed.ReturnValue -ne 0){throw 'Service executable path quoting failed.'}
        Native sc.exe @('sidtype','HarmoniaAudioTransport','restricted')
        Native sc.exe @('start','HarmoniaAudioTransport')
        $service=Get-Service HarmoniaAudioTransport;$service.WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
        $explorer=@(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object SessionId -gt 0)[0]
        $owner=Invoke-CimMethod -InputObject $explorer -MethodName GetOwner
        $user=$owner.Domain+'\'+$owner.User
        $task='HarmoniaSuiteProbe-'+[guid]::NewGuid().ToString('N')
        $log='C:\HarmoniaTest\Logs\SuiteServiceProbe.txt'
        New-Item -ItemType File -Path $log -Force | Out-Null
        Native icacls.exe @($log,'/grant',($user+':(W)'))
        $mode=if($protected){'protected'}elseif($route){'route "'+$path+'\routing-plan.json"'}else{'probe'}
        $action=New-ScheduledTaskAction -Execute $env:ComSpec -Argument ('/d /c ""'+$path+'\Harmonia.Suite.Tests.exe" > C:\HarmoniaTest\Logs\SuiteUnitTests.txt 2>&1 && "'+$path+'\Harmonia.Suite.UiTests.exe" > C:\HarmoniaTest\Logs\SuiteUiProbe.txt 2>&1 && "'+$path+'\Harmonia.Suite.Tests.exe" devices > C:\HarmoniaTest\Logs\SuiteDevices.txt && "'+$path+'\Harmonia.Suite.Tests.exe" service-devices > C:\HarmoniaTest\Logs\SuiteServiceDevices.txt && "'+$path+'\Harmonia.Suite.Tests.exe" '+$mode+' > '+$log+' 2>&1"')
        $principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        try {
            Register-ScheduledTask -TaskName $task -Action $action -Principal $principal | Out-Null
            function Run-Probe {
                Clear-Content -LiteralPath $log
                Start-ScheduledTask -TaskName $task
                $readyDeadline=(Get-Date).AddSeconds(40)
                while(!(Select-String -LiteralPath $log -Pattern 'Control accepted:' -Quiet -ErrorAction SilentlyContinue)) {
                    if((Get-Date) -gt $readyDeadline -or (Get-ScheduledTask -TaskName $task).State -ne 'Running') {
                        Get-Content C:\HarmoniaTest\Logs\SuiteUnitTests.txt,C:\HarmoniaTest\Logs\SuiteUiProbe.txt,$log -ErrorAction SilentlyContinue
                        throw 'Suite probe did not become ready.'
                    }
                    Start-Sleep -Milliseconds 200
                }
                for($i=0;$i -lt 6;$i++){& "$path\RuntimeAudioTests.exe";if($LASTEXITCODE){throw 'Generated audio test failed.'}}
                $deadline=(Get-Date).AddSeconds(30)
                while((Get-ScheduledTask -TaskName $task).State -eq 'Running'){if((Get-Date) -gt $deadline){throw 'Suite probe timeout'};Start-Sleep 1}
                Get-Content $log
                if((Get-ScheduledTaskInfo -TaskName $task).LastTaskResult -ne 0){throw 'Live Suite service probe failed.'}
            }
            Run-Probe
            Copy-Item -LiteralPath $log -Destination ($log+'.before-restart')
            Stop-Service HarmoniaAudioTransport
            (Get-Service HarmoniaAudioTransport).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
            Start-Service HarmoniaAudioTransport
            (Get-Service HarmoniaAudioTransport).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
            $repeat=New-ScheduledTaskAction -Execute $env:ComSpec -Argument ('/d /c ""'+$path+'\Harmonia.Suite.Tests.exe" '+$mode+' > '+$log+' 2>&1"')
            Set-ScheduledTask -TaskName $task -Action $repeat | Out-Null
            Run-Probe
            'PASS: restricted service restart, kernel-handle release and fresh driver-to-service delivery.'
            Native sc.exe @('qsidtype','HarmoniaAudioTransport')
        } finally {if((Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue).State -ne 'Running'){Unregister-ScheduledTask -TaskName $task -Confirm:$false}}
    }
} finally {
    Invoke-Command -Session $Session -ScriptBlock {
        $service=Get-Service HarmoniaAudioTransport -ErrorAction SilentlyContinue
        if($service){if($service.Status -ne 'Stopped'){Stop-Service HarmoniaAudioTransport;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}; & sc.exe delete HarmoniaAudioTransport | Out-Host;if($LASTEXITCODE){throw 'Test service cleanup failed.'}}
    }
    & "$PSScriptRoot/Collect-Logs.ps1" -Session $Session
}
