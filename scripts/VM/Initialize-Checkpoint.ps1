[CmdletBinding()]
param([Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session)
. "$PSScriptRoot/Common.ps1"
$vm = Assert-BenchSession $Session
$existing = @(Get-VMSnapshot -VM $vm | Where-Object Name -eq $script:CheckpointName)
if ($existing.Count -gt 0) {
    Get-BenchCheckpoint $vm | Select-Object VMName,Name,Id,CreationTime,SnapshotType
    return
}
# Do not label a known installed-driver state as a pre-driver baseline.
$installed = Invoke-Command -Session $Session -ScriptBlock {
    @(Get-WindowsDriver -Online -All | Where-Object { $_.OriginalFileName -match 'Harmonia' }).Count -gt 0 -or
    @(Get-CimInstance Win32_SystemDriver | Where-Object Name -match 'Harmonia').Count -gt 0 -or
    @(Get-PnpDevice | Where-Object { $_.InstanceId -match '^ROOT\\HarmoniaVirtualAudio' -or $_.FriendlyName -match 'Harmonia' }).Count -gt 0
}
if ($installed) { throw 'Harmonia installation evidence exists; inspect the guest before establishing a baseline.' }
Checkpoint-VM -VM $vm -SnapshotName $script:CheckpointName -ErrorAction Stop
Get-BenchCheckpoint $vm | Select-Object VMName,Name,Id,CreationTime,SnapshotType
