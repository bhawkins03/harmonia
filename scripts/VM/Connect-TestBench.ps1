[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$vm = Get-VM -Name $script:BenchName -ErrorAction Stop
if ($vm.Generation -ne 2 -or $vm.State -ne 'Running') { throw 'Start the Generation 2 test bench manually first.' }
$credential = Get-Credential -UserName 'Brandon' -Message 'Guest local administrator credentials (Harmonia Test Bench only)'
if (!$credential) { throw 'Credential prompt cancelled.' }
try {
    New-PSSession -VMId $vm.Id -Credential $credential -ErrorAction Stop
} finally { $credential = $null }
