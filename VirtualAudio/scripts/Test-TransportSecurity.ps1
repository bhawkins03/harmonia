[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
# Offline configuration check. Does not create a device/token/service or claim
# that the real Windows ACL/namespace behavior has been tested.
$source=Get-Content (Join-Path $PSScriptRoot '../driver/HarmoniaTransport.cpp') -Raw
$match=[regex]::Match($source,'UNICODE_STRING sddl=RTL_CONSTANT_STRING\(L"([^"]+)"\)')
if (!$match.Success) { throw 'Control-device SDDL literal not found.' }
$descriptor=[System.Security.AccessControl.RawSecurityDescriptor]::new($match.Groups[1].Value)
$protected=[System.Security.AccessControl.ControlFlags]::DiscretionaryAclProtected
if (($descriptor.ControlFlags -band $protected) -ne $protected) { throw 'Control DACL must be protected.' }
$expected=@{
    'S-1-5-18'=0x10000000L
    'S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259'=0xc0000000L
}
if ($descriptor.DiscretionaryAcl.Count -ne 2) { throw 'Unexpected principal in the transport DACL.' }
foreach($ace in $descriptor.DiscretionaryAcl) {
    $sid=$ace.SecurityIdentifier.Value
    if (!$expected.ContainsKey($sid) -or $ace.AceType -ne [System.Security.AccessControl.AceType]::AccessAllowed -or
        $ace.AceFlags -ne [System.Security.AccessControl.AceFlags]::None -or
        ([long]$ace.AccessMask -band 0xffffffffL) -ne $expected[$sid]) { throw 'Transport DACL differs from approved policy.' }
    $expected.Remove($sid)
}
if ($expected.Count) { throw 'Approved transport principal is missing.' }
Write-Output 'Offline control DACL check passed: protected, LocalSystem + exact service SID only; no broad-user ACE.'
