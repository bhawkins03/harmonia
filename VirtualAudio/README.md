# Harmonia virtual audio endpoint

Status: D2.2 source includes a bounded private PCM queue, approved restricted
control-device ACL, v1 nonblocking IOCTLs, conservative packet/rights gates and
lifecycle handling. It builds unsigned; it is not installed or runtime-verified.
The desktop application still uses its existing WASAPI loopback backend.

See [D2.2 audit](D2_2_AUDIT.md), [validation/limitations](D2_2_STATUS.md),
[wire protocol](PROTOCOL.md), and [approved security design](D2_SECURITY.md).
No service/client or driver-to-app routing integration is supplied yet.
Unknown rights or missing packet commits deliberately block PCM export.

From the repository root, Build.ps1 runs all available tests and builds the app
and driver. Add -Analyze for a forced static-analysis driver rebuild. Never
interpret compilation, unit tests or offline ACL checks as installed-driver
verification. Microsoft MIT source notices remain in driver/.
