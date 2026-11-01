[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage=Join-Path $root ('dist/Suite-VMDevelopment-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach($component in @(@{Name='App';Project='Harmonia.csproj'},@{Name='Service';Project='Suite/Service/Harmonia.Suite.Service.csproj'})) {
    & dotnet publish (Join-Path $root $component.Project) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o (Join-Path $stage $component.Name)
    if($LASTEXITCODE){throw 'Suite development publication failed.'}
}
Copy-Item -LiteralPath (Join-Path $root 'scripts/VM/Install-SuiteDevelopment.ps1') -Destination $stage
$files=@(Get-ChildItem $stage -File -Recurse | ForEach-Object {@{Path=$_.FullName.Substring($stage.Length+1).Replace('\','/');SHA256=(Get-FileHash $_.FullName).Hash}})
@{Channel='suite-vm-development';PublicUpdatesEnabled=$false;Files=$files} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage 'development-hashes.json') -Encoding UTF8
@'
HARMONIA SUITE - VM DEVELOPMENT ONLY
Unsigned application/service build. Public updates are disabled.
No driver or development certificate is included. Requires the separately
validated Harmonia driver already installed in Harmonia Test Bench.
Run Install-SuiteDevelopment.ps1 inside that VM only, with administrator approval.
The script installs the restricted transport service and copies the desktop app.
Run App/Harmonia.exe without elevation. It does not alter default audio devices.
Physical outputs must be visible in the service audio session.
This archive is not an end-user Suite release or an automatic-update payload.
'@ | Set-Content (Join-Path $stage 'READ-ME-FIRST.txt') -Encoding UTF8
$zip=Join-Path $root ('dist/Harmonia-Suite-VMDevelopment-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
"VM development archive: $zip"
"Expanded package: $stage"
