[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$DryRun, [switch]$BuildOnly, [switch]$DeployOnly,
    [switch]$CollectLogsOnly, [switch]$PrepareGuest, [switch]$Cleanup, [switch]$RuntimeTests, [switch]$SimulateProtected,
    [string]$DevconPath,
    [System.Management.Automation.PSCredential]$Credential,
    [ValidateRange(30,900)][int]$TimeoutSeconds = 240
)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/Workflow.ps1"
$session = $null
$results = [Collections.Generic.List[object]]::new()
$mode = 'Full'
$selected = @('BuildOnly','DeployOnly','CollectLogsOnly','PrepareGuest' | Where-Object { Get-Variable $_ -ValueOnly })
if ($selected.Count -gt 1) { throw 'Choose only one mode switch.' }
if ($selected.Count) { $mode = $selected[0] }
if ($SimulateProtected -and ($Configuration -ne 'Debug' -or !$RuntimeTests -or $mode -ne 'Full')) { throw '-SimulateProtected requires Debug full workflow with -RuntimeTests.' }
if ($RuntimeTests -and $mode -ne 'Full') { throw '-RuntimeTests requires full workflow.' }
if ($Cleanup -and $mode -ne 'Full') { throw '-Cleanup requires the full workflow.' }
$stage = 'Host preflight'
$failed = $false
function Connect-Bench {
    if ($script:session) { Remove-PSSession $script:session -ErrorAction SilentlyContinue; $script:session = $null }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $script:session = New-PSSession -VMId $vm.Id -Credential $credential -ErrorAction Stop
            return
        } catch { Start-Sleep -Seconds 3 }
    } while ((Get-Date) -lt $deadline)
    throw 'PowerShell Direct unavailable within timeout; inspect console, credentials and recovery screen.'
}
function Guest([scriptblock]$Action, [object[]]$Arguments = @()) {
    # Retry only the read-only connectivity probe; never replay a mutation.
    try { Invoke-Command -Session $script:session -ScriptBlock { 1 } -ErrorAction Stop | Out-Null }
    catch { Connect-Bench }
    $null = Assert-BenchSession $script:session
    Invoke-Command -Session $script:session -ScriptBlock $Action -ArgumentList $Arguments -ErrorAction Stop
}
function Restart-Bench([switch]$Firmware) {
    # Hyper-V's shutdown integration handles a signed-in guest desktop without
    # forcing applications closed. Stop-Computer refuses this case outright.
    Stop-VM -VM $vm -Confirm:$false -ErrorAction Stop # No Force or TurnOff fallback.
    if ($session) { Remove-PSSession $session -ErrorAction SilentlyContinue; $script:session = $null }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-VM -Id $vm.Id).State -ne 'Off') {
        if ((Get-Date) -gt $deadline) { throw 'Graceful shutdown timed out. Inspect VM console; no forced power-off attempted.' }
        Start-Sleep 2
    }
    if ($Firmware) { Set-VMFirmware -VM $vm -EnableSecureBoot Off }
    Start-VM -VM $vm | Out-Null
    Connect-Bench
}
try {
    Assert-BenchAdministrator
    Get-Command Get-VM,Get-VMSnapshot,Get-VMFirmware,Set-VMFirmware,Start-VM,Stop-VM,New-PSSession -ErrorAction Stop | Out-Null
    $vm = Get-VM -Name $script:BenchName -ErrorAction Stop
    if ($vm.Generation -ne 2) { throw 'Expected Generation 2 VM.' }
    $checkpoint = Get-BenchCheckpoint $vm
    Write-Host "VM: $($vm.Name); state: $($vm.State); checkpoint: $($checkpoint.Id) $($checkpoint.CreationTime)"
    if ($vm.State -notin @('Running','Off')) { throw "VM state $($vm.State) requires manual resolution." }
    $package = Join-Path $PSScriptRoot "../../VirtualAudio/driver/Package/x64/$Configuration/HarmoniaPackage"
    if ($mode -notin @('PrepareGuest','CollectLogsOnly')) {
        foreach ($name in 'HarmoniaVirtualAudio.inf','HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.cat') {
            $file = Get-Item -LiteralPath (Join-Path $package $name)
            if (!$file.Length) { throw "Empty artifact: $name" }
        }
        $hardwareId = Get-HarmoniaInfIdentity (Join-Path $package 'HarmoniaVirtualAudio.inf')
    }
    if ($mode -in @('Full','DeployOnly')) {
        $pin = Get-Content "$PSScriptRoot/../../VirtualAudio/upstream.json" -Raw | ConvertFrom-Json
        $kit = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin/$($pin.kitVersion)"
        foreach ($tool in 'x64/signtool.exe','x86/Inf2Cat.exe') {
            if (!(Test-Path -LiteralPath (Join-Path $kit $tool))) { throw "Missing pinned WDK tool: $tool" }
        }
        if ($mode -eq 'Full' -and !$DryRun) {
            if (!$DevconPath -or !(Test-Path -LiteralPath $DevconPath -PathType Leaf)) { throw 'Supply trusted WDK x64 -DevconPath.' }
            if ((Get-AuthenticodeSignature $DevconPath).Status -ne 'Valid') { throw 'DevCon signature invalid.' }
        }
    }
    if ($DryRun) {
        Get-BenchPlan $mode
        if($RuntimeTests){'Build native runtime probes; APPROVE temporary guest service/task; validate event-driven render, device clock, PCM publication and six-channel tone identity.'}
        if($SimulateProtected){'DEBUG DENIAL TEST: force CopyProtect; require playback clock progress but zero exported PCM and Protected replies.'}
        Write-Host 'DRY RUN: no VM changes, build, signing, credential prompts or log writes.'
        return
    }
    if ($mode -in @('Full','BuildOnly')) {
        $stage = 'Build'
        & "$PSScriptRoot/../../VirtualAudio/scripts/Build-Harmonia.ps1" -Configuration $Configuration -SimulateProtected:$SimulateProtected | Out-Host
        $hardwareId = Get-HarmoniaInfIdentity (Join-Path $package 'HarmoniaVirtualAudio.inf')
        foreach ($name in 'HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.cat') {
            if (!(Get-Item (Join-Path $package $name)).Length) { throw "Empty build output: $name" }
        }
        & "$PSScriptRoot/Test-PackageMetadata.ps1" -InfPath (Join-Path $package 'HarmoniaVirtualAudio.inf') | Out-Host
        $results.Add([pscustomobject]@{Stage=$stage;Status='PASS';Detail=$Configuration})
        if ($BuildOnly) { return }
    }
    if ($RuntimeTests) { & "$PSScriptRoot/Build-RuntimeTests.ps1" | Out-Host }
    $stage = 'Guest preflight'
    if (!$credential) { $credential = Get-Credential -Message 'Harmonia Test Bench local administrator (memory only, this run)' }
    if (!$credential) { throw 'Credentials cancelled.' }
    if ($vm.State -eq 'Off') { Start-VM -VM $vm | Out-Null }
    Connect-Bench
    $state = Guest {
        $os = Get-CimInstance Win32_OperatingSystem
        if ($os.Caption -notmatch 'Windows 11' -or $os.OSArchitecture -ne '64-bit') { throw 'Windows 11 x64 required.' }
        if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Guest administrator required.' }
        $bl = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
        $bcd = & bcdedit.exe /enum '{current}' 2>&1
        if ($LASTEXITCODE) { throw 'Cannot read guest BCD.' }
        $dg = Get-CimInstance -Namespace root/Microsoft/Windows/DeviceGuard -ClassName Win32_DeviceGuard
        [pscustomobject]@{ Decrypted=([string]$bl.VolumeStatus -eq 'FullyDecrypted'); Protection=[string]$bl.ProtectionStatus; SecureBoot=(Confirm-SecureBootUEFI); TestSigning=([bool]($bcd -match 'testsigning\s+Yes')); VBS=$dg.VirtualizationBasedSecurityStatus }
    }
    if ($PrepareGuest) {
        $stage = 'Prepare guest boot security'
        if ($state.SecureBoot -or !$state.TestSigning) {
            # Fail closed rather than guessing whether a recovery key was safely backed up.
            if (!$state.Decrypted) { throw 'Guest BitLocker is encrypted or transitioning. Back up and verify recovery key outside harness, then fully decrypt guest OS volume manually before boot changes. No keys are read or logged.' }
            if ($state.SecureBoot) {
                Confirm-BenchAction 'Disable guest Secure Boot after graceful shutdown (BitLocker fully decrypted)'
                Restart-Bench -Firmware
            }
            if (!$state.TestSigning) {
                Confirm-BenchAction 'Enable guest BCD TESTSIGNING; gracefully restart guest'
                Guest { & bcdedit.exe /set '{current}' testsigning on; if ($LASTEXITCODE) { throw 'BCD change failed.' } }
                Restart-Bench
            }
        }
        Guest {
            if (Confirm-SecureBootUEFI) { throw 'Secure Boot remains enabled.' }
            $b = & bcdedit.exe /enum '{current}'
            if ($LASTEXITCODE -or !($b -match 'testsigning\s+Yes')) { throw 'Test signing was not enabled.' }
            New-Item -ItemType Directory -Path C:\HarmoniaTest -Force | Out-Null
        }
        $results.Add([pscustomobject]@{Stage=$stage;Status='PASS';Detail='Guest prepared; certificate trust occurs during full test.'})
        return
    }
    if ($CollectLogsOnly) { $stage = 'Collect logs'; & "$PSScriptRoot/Collect-Logs.ps1" -Session $session | Out-Host; return }
    $stage = 'Sign package'
    & "$PSScriptRoot/Test-PackageMetadata.ps1" -InfPath (Join-Path $package 'HarmoniaVirtualAudio.inf') | Out-Host
    . "$PSScriptRoot/Sign-TestPackage.ps1" -PackagePath $package
    $stage = 'Deploy'
    Guest { New-Item -ItemType Directory -Path C:\HarmoniaTest -Force | Out-Null }
    $deployment = & "$PSScriptRoot/Deploy-TestBuild.ps1" -Session $session -Configuration $Configuration -PassThru
    Copy-Item -LiteralPath $publicCertificate -Destination $deployment.Path -ToSession $session
    if ($DeployOnly) { $results.Add([pscustomobject]@{Stage=$stage;Status='PASS';Detail=$deployment.Path}); return }
    $stage = 'Driver prerequisites'
    if ($state.SecureBoot -or !$state.TestSigning) { throw 'Run -PrepareGuest first: Secure Boot must be off and guest TESTSIGNING on.' }
    if ($state.VBS -eq 2) { throw 'VBS is running. This harness does not disable it; review compatibility manually.' }
    if (!$DevconPath -or !(Test-Path -LiteralPath $DevconPath -PathType Leaf)) { throw 'Supply -DevconPath pointing to trusted WDK x64 devcon.exe for root-device installation.' }
    if ((Get-AuthenticodeSignature $DevconPath).Status -ne 'Valid') { throw 'DevCon must have a valid signature.' }
    Copy-Item -LiteralPath $DevconPath -Destination ($deployment.Path + '\devcon.exe') -ToSession $session
    $devconHash = (Get-FileHash -LiteralPath $DevconPath -Algorithm SHA256).Hash
    Guest {
        param($path,$hash)
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash -or
            (Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'Valid') { throw 'Guest DevCon transfer/signature verification failed.' }
    } @(($deployment.Path + '\devcon.exe'),$devconHash)
    $stage = 'Certificate trust'
    $thumbprint = $certificate.Thumbprint
    $trusted = Guest { param($t) (Test-Path "Cert:\LocalMachine\Root\$t") -and (Test-Path "Cert:\LocalMachine\TrustedPublisher\$t") } @($thumbprint)
    if (!$trusted) {
        Confirm-BenchAction "Trust development certificate $thumbprint in guest LocalMachine Root and TrustedPublisher"
        Guest {
            param($path,$thumb)
            $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($path)
            if ($cert.Thumbprint -ne $thumb) { throw 'Certificate thumbprint mismatch.' }
            foreach ($store in 'Root','TrustedPublisher') {
                & certutil.exe -addstore -f $store $path | Out-Host
                if ($LASTEXITCODE -ne 0 -or !(Test-Path "Cert:\LocalMachine\$store\$thumb")) { throw "Guest $store certificate import failed." }
            }
        } @(($deployment.Path + '\HarmoniaDevelopment.cer'),$thumbprint)
    }
    $stage = 'Install and test'
    Confirm-BenchAction "Install/update kernel driver from $($deployment.Path) for $hardwareId; existing-device updates gracefully reboot before validation"
    $install = Guest {
        param($path,$id)
        $inf = Join-Path $path 'HarmoniaVirtualAudio.inf'
        foreach ($file in 'HarmoniaVirtualAudio.sys','HarmoniaVirtualAudio.cat') {
            if ((Get-AuthenticodeSignature (Join-Path $path $file)).Status -ne 'Valid') { throw "Guest signature validation failed: $file" }
        }
        $devices = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\HarmoniaVirtualAudio' })
        if ($devices.Count -gt 1) { throw 'Multiple Harmonia devnodes; resolve manually.' }
        if ($devices.Count) { & "$path\devcon.exe" update $inf $id | Out-Host }
        else { & "$path\devcon.exe" install $inf $id | Out-Host }
        $code = $LASTEXITCODE
        if ($code -notin @(0,1)) { throw "DevCon failed: $code. Inspect SetupAPI logs." }
        [pscustomobject]@{Reboot=($code -eq 1 -or $devices.Count -gt 0)}
    } @($deployment.Path,$hardwareId)
    if ($install.Reboot) { Restart-Bench }
    $installed = Guest {
        $d = @(Get-CimInstance Win32_PnPEntity | Where-Object { @($_.HardwareID) -contains 'ROOT\HarmoniaVirtualAudio' -and $_.Present } | ForEach-Object { Get-PnpDevice -InstanceId $_.PNPDeviceID -PresentOnly })
        if ($d.Count -ne 1) { throw 'Expected exactly one present Harmonia Media device.' }
        $problem = Get-PnpDeviceProperty -InstanceId $d[0].InstanceId -KeyName DEVPKEY_Device_ProblemCode
        if ($problem.Data -ne 0) { throw "Harmonia PnP problem code $($problem.Data)." }
        $signed = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceID -eq $d[0].InstanceId)
        if ($signed.Count -ne 1 -or $signed[0].DriverProviderName -ne 'Harmonia') { throw 'Unexpected bound driver provider.' }
        $oem = $signed[0].InfName
        if ($oem -notmatch '^oem\d+\.inf$') { throw 'Unexpected driver-store INF name.' }
        if (!(Select-String -LiteralPath "$env:windir\INF\$oem" -SimpleMatch 'ROOT\HarmoniaVirtualAudio')) { throw 'Bound INF hardware ID mismatch.' }
        $svc = Get-CimInstance Win32_SystemDriver -Filter "Name='HarmoniaVirtualAudio'"
        if (!$svc -or $svc.State -ne 'Running') { throw 'Kernel driver is not running.' }
        foreach ($name in 'Audiosrv','AudioEndpointBuilder') { if ((Get-Service $name).Status -ne 'Running') { throw "$name is stopped." } }
        $endpoints = @(Get-PnpDevice -PresentOnly | Where-Object { $_.Class -eq 'AudioEndpoint' -and $_.FriendlyName -match 'Harmonia' -and $_.Status -eq 'OK' })
        if (!$endpoints.Count) { throw 'No healthy Harmonia audio endpoint; inspect audio logs. This is an enumeration smoke test, not an audio-stream test.' }
        [pscustomobject]@{InstanceId=$d[0].InstanceId;OemInf=$oem}
    }
    # Confirm the bound INF is the deployed INF, not an older Harmonia package.
    $boundHash = Guest { param($oem) (Get-FileHash "$env:windir\INF\$oem" -Algorithm SHA256).Hash } @($installed.OemInf)
    if ($boundHash -ne (Get-FileHash (Join-Path $package 'HarmoniaVirtualAudio.inf')).Hash) { throw 'Device bound to a different package INF.' }
    $boundSysHash = Guest {
        param($oem)
        $driver = Get-WindowsDriver -Online -Driver $oem -ErrorAction Stop
        $binary = Join-Path (Split-Path $driver.OriginalFileName) 'HarmoniaVirtualAudio.sys'
        (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
    } @($installed.OemInf)
    if ($boundSysHash -ne (Get-FileHash (Join-Path $package 'HarmoniaVirtualAudio.sys')).Hash) { throw 'Bound driver-store SYS differs from tested build.' }
    $results.Add([pscustomobject]@{Stage=$stage;Status='PASS';Detail='Driver and endpoint enumeration smoke test passed; streaming not tested.'})
    if ($RuntimeTests) {
        $stage='Live transport and WASAPI tests'
        & "$PSScriptRoot/Test-DriverRuntime.ps1" -Session $session -ObserveTransport -ExpectProtectedStream:$SimulateProtected | Out-Host
        $results.Add([pscustomobject]@{Stage=$stage;Status='PASS';Detail='Live guest probes passed; see runtime logs.'})
    }
    if ($Cleanup) {
        $stage = 'Cleanup'
        Confirm-BenchAction "Remove exact device $($installed.InstanceId) and driver-store package $($installed.OemInf)"
        $reboot = Guest {
            param($instance,$oem)
            & pnputil.exe /remove-device $instance | Out-Host
            if ($LASTEXITCODE -notin @(0,3010)) { throw 'Device removal failed.' }
            $restart = $LASTEXITCODE -eq 3010
            & pnputil.exe /delete-driver $oem | Out-Host
            if ($LASTEXITCODE -notin @(0,3010)) { throw 'Package removal failed; no force used.' }
            $restart -or ($LASTEXITCODE -eq 3010)
        } @($installed.InstanceId,$installed.OemInf)
        if ($reboot) { Restart-Bench }
        Guest {
            param($instance,$oem)
            if (@(Get-PnpDevice | Where-Object InstanceId -eq $instance).Count -or (Test-Path "$env:windir\INF\$oem")) { throw 'Removal incomplete.' }
        } @($installed.InstanceId,$installed.OemInf)
        $results.Add([pscustomobject]@{Stage=$stage;Status='PASS';Detail='Exact device and OEM package removed; guest certificate and boot configuration retained.'})
    }
} catch {
    $failed = $true
    Write-Host ($stage + ': ' + $_.Exception.Message)
    $results.Add([pscustomobject]@{Stage=$stage;Status='FAIL';Detail=$_.Exception.Message})
} finally {
    if (!$DryRun -and $session -and $mode -notin @('CollectLogsOnly','BuildOnly')) {
        try { Guest { 1 } | Out-Null; & "$PSScriptRoot/Collect-Logs.ps1" -Session $session | Out-Host }
        catch { $failed = $true; $results.Add([pscustomobject]@{Stage='Collect logs';Status='FAIL';Detail=$_.Exception.Message}) }
    }
    if ($session) { Remove-PSSession $session -ErrorAction SilentlyContinue }
    $credential = $null
    if (!$DryRun) {
        if (!$results.Count) { $results.Add([pscustomobject]@{Stage=$mode;Status='PASS';Detail='Completed'}) }
        $results | Format-Table -AutoSize -Wrap | Out-Host
        $reportDir = Join-Path $PSScriptRoot '../../VirtualAudio/.work/vm-reports'
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
        $results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $reportDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N') + '.json'))
    }
    if ($failed) { throw 'Harmonia VM workflow failed; see stage report and collected logs.' }
}
