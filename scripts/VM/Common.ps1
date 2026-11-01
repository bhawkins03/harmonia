$ErrorActionPreference = 'Stop'
$script:BenchName = 'Harmonia Test Bench'
$script:CheckpointName = 'Harmonia pre-driver'

function Assert-BenchSession {
    param([System.Management.Automation.Runspaces.PSSession]$Session)
    $vm = Get-VM -Name $script:BenchName -ErrorAction Stop
    if ($vm.Generation -ne 2 -or $vm.State -ne 'Running') { throw 'The Generation 2 test bench must be running.' }
    if (!$Session -or $Session.State -ne 'Opened' -or
        $Session.ComputerType -ne 'VirtualMachine' -or
        $Session.Runspace.ConnectionInfo.VMGuid -ne $vm.Id) {
        throw 'Supply a fresh PowerShell Direct session from Connect-TestBench.ps1 for Harmonia Test Bench.'
    }
    $vm
}

function Get-BenchCheckpoint {
    param($VM)
    $points = @(Get-VMSnapshot -VM $VM -ErrorAction Stop | Where-Object Name -eq $script:CheckpointName)
    if ($points.Count -ne 1) { throw 'Create and verify exactly one Harmonia pre-driver checkpoint using Initialize-Checkpoint.ps1 before deployment.' }
    $points[0]
}
