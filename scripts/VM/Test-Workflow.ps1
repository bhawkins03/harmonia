# No Hyper-V calls: parse every script, then exercise orchestration with mocks.
$ErrorActionPreference = 'Stop'
$failures = @()
Get-ChildItem $PSScriptRoot -Filter '*.ps1' | ForEach-Object {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$errors)
    $failures += $errors
}
if ($failures.Count) { throw ($failures | Out-String) }
. "$PSScriptRoot/Workflow.ps1"
$null = Get-HarmoniaInfIdentity "$PSScriptRoot/../../VirtualAudio/driver/TabletAudioSample/HarmoniaVirtualAudio.inx"
$temp = Join-Path ([IO.Path]::GetTempPath()) ('harmonia-inf-' + [guid]::NewGuid() + '.inf')
try {
    'Class=MEDIA' | Set-Content $temp
    $rejected=$false
    try { Get-HarmoniaInfIdentity $temp } catch { $rejected=$true }
    if (!$rejected) { throw 'Changed INF was accepted.' }
} finally { Remove-Item -LiteralPath $temp }
# Dry run calls are restricted to read-only VM queries by these mocks.
function Get-VM { [pscustomobject]@{Name='Harmonia Test Bench';Generation=2;State='Off';Id=[guid]::Empty} }
function Get-VMSnapshot { param($VM) [pscustomobject]@{Name='Harmonia pre-driver';Id=[guid]::Empty;CreationTime=[datetime]'2026-10-31'} }
function Get-VMFirmware {}
function Set-VMFirmware { throw 'Mutation attempted' }
function Start-VM { throw 'Mutation attempted' }
function Stop-VM { throw 'Mutation attempted' }
function New-PSSession { throw 'Connection attempted' }
function Get-Credential { throw 'Credential prompt attempted' }
# Run script text with the admin helper replaced, without weakening production checks.
$workflow = Get-Content "$PSScriptRoot/Test-Harmonia.ps1" -Raw
$workflow = $workflow.Replace('    Assert-BenchAdministrator', '    # Administrator mocked for offline tests')
$workflow = $workflow.Replace('$PSScriptRoot', $PSScriptRoot)
$run = [scriptblock]::Create($workflow)
foreach ($mode in @{},@{PrepareGuest=$true},@{CollectLogsOnly=$true},@{BuildOnly=$true},@{DeployOnly=$true}) {
    & $run -DryRun @mode
}
# Missing checkpoint must fail before start or connection.
function Get-VMSnapshot { param($VM) }
$rejected=$false
try { & $run -DryRun } catch { $rejected=$true }
if (!$rejected) { throw 'Missing checkpoint was accepted.' }
# Duplicate checkpoint and unsupported VM states also fail before mutations.
function Get-VMSnapshot { param($VM) 1..2 | ForEach-Object { [pscustomobject]@{Name='Harmonia pre-driver'} } }
$rejected=$false
try { & $run -DryRun } catch { $rejected=$true }
if (!$rejected) { throw 'Duplicate checkpoints accepted.' }
function Get-VMSnapshot { param($VM) [pscustomobject]@{Name='Harmonia pre-driver';Id=[guid]::Empty} }
foreach ($badState in 'Saved','Paused','Starting') {
    function Get-VM { [pscustomobject]@{Name='Harmonia Test Bench';Generation=2;State=$badState;Id=[guid]::Empty} }
    $rejected=$false
    try { & $run -DryRun } catch { $rejected=$true }
    if (!$rejected) { throw "Unsafe state accepted: $badState" }
}
'PASS: syntax, INF policy, all dry-run modes, absent/duplicate checkpoints, unsupported states; no VM accessed.'
