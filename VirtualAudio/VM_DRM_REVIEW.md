# VM-discovered default-content rights decision

Validation update, 2026-11-01: the rebuilt runtime probes passed live PCM
publication and six-channel tone isolation through both SYSTEM and restricted
LocalService on the currently installed guest driver. The 32 native rights
policy checks passed. Development-only simulated CopyProtect testing also passed:
SYSTEM and restricted LocalService both exported zero PCM frames with Protected
replies while generated-tone playback and the device clock advanced. See
VM_HANDOFF.md for counts, log locations and remaining validation. The historical
discovery and approved policy decision follow.

The loaded driver renders 48 kHz / six-channel Float32 and its restricted
LocalService transport client passes. An attached LocalSystem observer received
zero PCM frames and 126 Protected replies during a generated, event-driven test
tone. The current D2.2 policy intentionally requires a nonzero content ID and an
upstream SetContentId callback before granting export. This prevents ordinary
Windows default-content streams from working.

Approved by the owner in this VM debugging session on 2026-10-31. Implementation and live validation are in progress:

- Retain the existing control-device ACL, exclusive handle/process ownership,
  protocol, protected-content denial and bounded nonpaged queue.
- Treat initial content ID zero as the Windows-defined default-rights state,
  rather than inventing a nonzero upstream content ID or assuming a failed
  rights query means permission.
- Ask DRMK CreateContentMixed to derive a temporary nonzero ID from the current
  stream ID, then independently query its rights. Grant only after both calls
  succeed, the derived ID is nonzero and Reserved/CopyProtect/
  DigitalOutputDisable are clear. Destroy only the derived ID, never ID zero.
- Establish initial default rights after stream registration. Every subsequent
  SetContentId still revokes and flushes before updating/querying rights. Explicit
  protected flags, unknown IDs, missing interfaces, query failures, removal and
  power invalidation remain denied. No user-mode permission override is added.
- Preserve generation tickets and the requirement for a fresh packet commit after
  a successful grant. No prior buffer contents become exportable on a rights change.
- Add native policy tests, a live publication test with the generated tone, and a
  development-only simulated-protection test using Microsoft's recommended
  protected-rights injection approach. Actual licensed-content DRM certification
  remains outside these VM tests.

This changes the previous approved rule that all zero-ID/missing-callback streams
remain denied. The owner explicitly approved this separate policy review before implementation.

References:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/portcls/nf-portcls-pcdestroycontent
  specifies that zero represents a stream with default DRM content rights.
- https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/content-ids-and-content-rights
  describes querying composite rights through CreateContentMixed/GetContentRights.
- https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/developing-and-debugging-drm-drivers
  describes testing protection with generated unprotected audio and injected
  CopyProtect rights, without playing protected content.
