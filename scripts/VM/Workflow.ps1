# Shared policy helpers. No actions occur when this file is dot-sourced.
function Confirm-BenchAction([string]$Action) {
    if ((Read-Host "$Action on Harmonia Test Bench only. Type APPROVE to continue") -cne 'APPROVE') {
        throw "Approval declined: $Action"
    }
}
function Invoke-BenchNative([string]$File, [string[]]$Arguments, [int[]]$SuccessCodes = @(0)) {
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -notin $SuccessCodes) { throw "$File failed with exit code $LASTEXITCODE" }
}
function Get-HarmoniaInfIdentity([string]$Path) {
    $inf = Get-Content -LiteralPath $Path -Raw
    if ($inf -notmatch '(?im)^Class\s*=\s*MEDIA\s*$' -or
        $inf -notmatch '(?im)^CatalogFile\s*=\s*HarmoniaVirtualAudio.cat\s*$' -or
        $inf -notmatch '(?im)^%DeviceName%\s*=\s*HarmoniaDevice,ROOT\\HarmoniaVirtualAudio\s*$' -or
        $inf -notmatch '(?im)^ServiceBinary\s*=\s*%13%\\HarmoniaVirtualAudio.sys\s*$') {
        throw 'INF requirements changed; review installation logic before proceeding.'
    }
    'ROOT\HarmoniaVirtualAudio'
}
function Get-BenchPlan([string]$Mode) {
    'Read-only: administrator, Hyper-V tools, actual VM state, Gen 2, unique pre-driver checkpoint, artifacts.'
    if ($Mode -eq 'BuildOnly') { 'Build with existing Build-Harmonia.ps1; validate artifacts.'; return }
    'Prompt once for guest administrator credentials; start VM only if Off; reconnect PowerShell Direct with timeout.'
    'Inspect guest administrator, Windows 11 x64, audio services, BitLocker, VBS, Secure Boot and BCD.'
    switch ($Mode) {
        PrepareGuest { 'APPROVE gates: guest Secure Boot off (graceful shutdown/start), BCD testsigning on (restart). BitLocker must be fully decrypted; encrypted guests require manual recovery-key backup and preparation first.' }
        CollectLogsOnly { 'Collect SetupAPI, event and audio logs using Collect-Logs.ps1.' }
        default {
            'Build unless DeployOnly; create/reuse host development certificate, sign SYS, generate CAT, sign CAT, verify both and catalog membership.'
            'Deploy verified package and public certificate; never copy private key.'
            if ($Mode -ne 'DeployOnly') {
                'APPROVE gate: trust exact public certificate in guest Root and TrustedPublisher.'
                'APPROVE gate: DevCon install root device if absent, otherwise update exact existing instance; reboot gracefully if required.'
                'Verify bound package, PnP problem code, kernel service, audio services and Harmonia endpoint; collect logs even after failure.'
                'Optional -Cleanup: APPROVE removal of exact device and its verified OEM INF; verify absence.'
            }
        }
    }
    'Never restore, create or overwrite checkpoints; never change host boot or security settings.'
}
function Assert-BenchAdministrator {
    if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run from elevated PowerShell.' }
}
