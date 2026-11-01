# D2 transport security proposal and permission gate

D2.1 supplies wire definitions/codecs/tests only. This proposal is not approval
to implement handlers, expose a device, or install a service. The D1 MEDIA INF
ACL applies to the audio endpoint and MUST NOT be reused for PCM export.

## Access proposal to review before D2.2

- Separate named control device/interface, independent of public render pins.
- Proposed service name: `HarmoniaAudioTransport`, standalone LocalService
  process, SERVICE_SID_TYPE_RESTRICTED, with its service SID in its token.
- Read-only `sc.exe showsid HarmoniaAudioTransport` computed the proposed SID:
  `S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259`.
  This lookup did not create/configure a service. Reverify it on the signed test
  machine and inspect the actual process token before relying on the ACL.
- Proposed protected control-device DACL:
  `D:P(A;;GA;;;SY)(A;;GRGW;;;S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259)`
- LocalSystem is trusted for system maintenance. Only the service SID gets
  ordinary read/write access. No Builtin Administrators, Everyone,
  Authenticated Users, interactive user, generic LocalService or NetworkService
  ACE. Administrators can still administratively reconfigure the OS; this
  design is not a boundary against a compromised administrator/kernel.
- A restricted service token also needs access to every resource it uses;
  review executable/config/log/device ACLs and protect service configuration
  against ordinary-user writes. Do not grant privileges simply to bypass tests.
- Open with GENERIC_READ | GENERIC_WRITE, no inheritance or sharing. Require
  FILE_READ_DATA | FILE_WRITE_DATA on ALL IOCTLs, including capabilities. No
  FILE_ANY_ACCESS, METHOD_NEITHER, arbitrary pointer, writable mapping or handle
  duplication to the tray process. Public UI requests must use a separately
  authorized service API (future work).
- Use an appropriate secured device-creation API with explicit security
  descriptor and FILE_DEVICE_SECURE_OPEN. Reject nonempty device-relative names
  and remote opens; verify the actual namespace behavior in an isolated VM.
- ACL authorization at open plus I/O-manager access bits and explicit
  IoValidateDeviceIoControlAccess (or supported equivalent) at dispatch. Bind
  the exclusive attachment to the owning file object; a guessed/copied session
  token on a different handle must fail. Service SID—not a PID or process name—
  supplies the authorization boundary. Handle cleanup revokes the session.

## Mandatory permission request before implementing handlers

The next agent must present this exact concrete access proposal to the user
and request approval **before writing any kernel IOCTL handler**:

> May I implement the separate transport control interface with LocalSystem
> maintenance access and read/write access restricted to the
> HarmoniaAudioTransport service SID shown in D2_SECURITY.md, with both
> FILE_READ_DATA and FILE_WRITE_DATA required for every operation, and one
> consumer bound to its owning handle? This authorizes source implementation
> only; service/driver installation, certificates and security-setting changes
> remain outside the task.

No such approval has been requested or granted as part of D2.1 because no
handlers are being implemented here. General permission to build D2.1 is not
approval of the future service identity or ACL. If the proposal changes, request
approval of the revised concrete policy before implementing it.

## Threat model and later acceptance tests

Assets: system-wide unprotected PCM, protected-content confidentiality, kernel
memory, bounded nonpaged allocation, stream availability, fresh generation data.
Untrusted inputs: requests/buffer lengths, output MDLs, stale tokens, raced
cancel/close/removal, malicious or compromised consumer, and ordinary local
processes. Even an authorized consumer must never supply memory addresses,
queue cursors, arbitrary allocation sizes or an instruction to override DRM.

D2.2 must prove ordinary users, a plain LocalService process and a process with
only read or write access cannot use the transport; the configured service can.
Verify restrictions on path suffixes, impersonation, handle inheritance and
unexpected duplicate handles. Test second attach, close/process kill,
cancel/remove/power races, exact-once completion, and refusal of stale sessions.
All arithmetic and copies are bounded; allocate only at PASSIVE_LEVEL. Use a
reviewed synchronization/rundown strategy, not the serializer as a lock.

Export is denied until the SysVAD content-rights audit proves the current
stream is eligible. Missing or ambiguous rights fail closed. Protected audio
must never enter the export queue or be returned to any authorized handle.
Wire validation of a Protected flag is not a DRM enforcement mechanism.
No D2.1 unit test establishes real ACL enforcement, DRM behavior, IRQL safety,
queue correctness, or cancellation/PnP safety.

Microsoft references:
- [Defining I/O control codes](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/defining-i-o-control-codes)
- [Windows security model for drivers](https://learn.microsoft.com/en-us/windows-hardware/drivers/driversecurity/windows-security-model)
- [Device namespace access](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/controlling-device-namespace-access)
- [I/O access validation](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdmsec/nf-wdmsec-wdmlibiovalidatedeviceiocontrolaccess)
- [Service SID types](https://devblogs.microsoft.com/oldnewthing/20231004-00/?p=108849)
