[CmdletBinding()]
param([Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session)
. "$PSScriptRoot/Common.ps1"
$null = Assert-BenchSession $Session
$destination = Join-Path $PSScriptRoot ('../../VirtualAudio/.work/vm-logs/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$stage = Invoke-Command -Session $Session -ScriptBlock {
    $ErrorActionPreference = 'Stop'
    $stage = Join-Path $env:TEMP ('HarmoniaLogs-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    $notes = [Collections.Generic.List[string]]::new()
    Get-ChildItem -LiteralPath "$env:windir\INF" -Filter 'setupapi*.log' -File | Copy-Item -Destination $stage
    $channels = 'System','Application','Microsoft-Windows-CodeIntegrity/Operational','Microsoft-Windows-Kernel-PnP/Configuration','Microsoft-Windows-DeviceSetupManager/Admin'
    $channels += @(& wevtutil.exe el | Where-Object { $_ -match 'Audio|Kernel-PnP' })
    $channels = @($channels | Select-Object -Unique)
    foreach ($channel in $channels) {
        $output = Join-Path $stage (($channel -replace '[/\\]','_') + '.evtx')
        $message = & wevtutil.exe epl $channel $output '/q:*[System[TimeCreated[timediff(@SystemTime) <= 604800000]]]' 2>&1
        if ($LASTEXITCODE -ne 0) { $notes.Add("$channel export failed: $message") }
    }
    # Diagnostic output has no established D2.2 location yet. Collect conventional
    # guest-owned output directories without copying deployed binaries or secrets elsewhere.
    foreach ($path in 'C:\HarmoniaTest\Logs','C:\HarmoniaTest\Diagnostics','C:\ProgramData\Harmonia\Logs') {
        if (Test-Path -LiteralPath $path -PathType Container) {
            $label = $path -replace '[:\\]','_'
            Copy-Item -LiteralPath $path -Destination (Join-Path $stage $label) -Recurse
        } else { $notes.Add("No diagnostic directory: $path") }
    }
    $notes | Set-Content -LiteralPath (Join-Path $stage 'collection-notes.txt') -Encoding UTF8
    $stage
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -FromSession $Session -Path "$stage\*" -Destination $destination -Recurse -ErrorAction Stop
Write-Output "Logs retrieved to $((Resolve-Path $destination).Path). Guest staging retained at $stage."
