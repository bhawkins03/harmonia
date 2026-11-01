[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$credential=Get-Credential -UserName Brandon -Message 'Harmonia Test Bench guest administrator, kept only for this protection/restore cycle'
if(!$credential){throw 'Guest credentials cancelled.'}
$devcon='C:\Program Files (x86)\Windows Kits\10\Tools\10.0.28000.0\x64\devcon.exe'
$session=$null
try {
    & "$PSScriptRoot/Test-Harmonia.ps1" -Configuration Debug -RuntimeTests -SimulateProtected -DevconPath $devcon -Credential $credential
    $vm=Get-VM -Name $script:BenchName
    $session=New-PSSession -VMId $vm.Id -Credential $credential
    & "$PSScriptRoot/Test-SuiteService.ps1" -Session $session -ExpectProtected
} finally {
    if($session){Remove-PSSession $session;$session=$null}
    try {
        & "$PSScriptRoot/Test-Harmonia.ps1" -Configuration Release -RuntimeTests -DevconPath $devcon -Credential $credential
        $vm=Get-VM -Name $script:BenchName
        $session=New-PSSession -VMId $vm.Id -Credential $credential
        & "$PSScriptRoot/Test-SuiteService.ps1" -Session $session
    } finally {if($session){Remove-PSSession $session};$credential=$null}
}
