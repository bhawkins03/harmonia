# Diagnose surround channel separation

1. Select beta 5.1 and assign headphones to Rear stereo pair.
2. Stop routing. Click Test rear L, then Test rear R. Each must play only one earcup. This test bypasses upmix and capture.
3. Play media with routing enabled and read the input status. Stereo input means the player or default playback device already reduced audio to two channels.
4. Enable Diagnostic: no stereo upmix, restart routing. For stereo input, only front L/R should play; center and rear will be silent. Native 5.1 PCM still maps six independent channels.

IMPORTANT: The existing stereo upmix derives rear channels using inverted differences (L-R and R-L). These are NOT independent rear audio, which explains mono-like headphone effects. A six-channel FLAC can still be downmixed by the player or Windows mix format. Normal MP3 is mono/stereo, regardless of video title. The default source still plays independently and may create audible duplicates. A future version must inspect the WAVEFORMATEXTENSIBLE channel mask.

No Windows audio settings are changed. This branch requires compilation and hardware verification.