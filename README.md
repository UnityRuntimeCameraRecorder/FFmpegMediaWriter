# FFmpegMediaWriter

Generic .NET Framework 4.8 library that sends audio and video streams to an external FFmpeg process, creates an intermediate MKV container and finalizes an MP4 output.

Set `MediaWriterSettings.KeepIntermediateFile` to `true` to retain that MKV after successful MP4 finalization. It is deleted by default, but is always kept when finalization fails so the recording can be recovered.

It has no dependency on Unity, Valheim, BepInEx, Direct3D or NVENC. Producers write raw audio and already encoded H.264 or HEVC video through `IMediaWriter`. FFmpeg copies the video stream without re-encoding it and converts audio to AAC during MP4 finalization. There is no video encoding fallback.

FFmpeg must be installed separately. Released under the [MIT License](LICENSE).
