## Live speaker map meters

The feature/live-speaker-meters branch adds four animated signal bars above each speaker node, polled at 20 Hz. The meter samples the PCM that Harmonia places into each output buffer (post channel mapping/upmix), rather than assuming a node is active merely because a source advertises that channel. Rear-left and rear-right are separately measured even though they share one stereo headphone endpoint. Bars show short peak levels and decay when audio stops.

**Important:** These are software signal indicators, not hardware playback confirmation or actual acoustic levels. They cannot prove the physical speaker reproduced the signal or that synchronization is correct. With the current stereo upmixer, front/rear activity may overlap legitimately; the diagnostic strict-input mode helps distinguish native channel isolation.

The native 5.1 FLAC player discussed in issue #11 is a separate pending milestone, not part of this change. This branch has not yet been compiled on Windows.
