# Landoria.FFmpegMediaWriter

Generic .NET Framework 4.8 library that sends audio and video streams to an external FFmpeg process, creates an intermediate MKV container and finalizes an MP4 output.

Set `MediaWriterSettings.KeepIntermediateFile` to `true` to retain that MKV after successful MP4 finalization. It is deleted by default, but is always kept when finalization fails so the recording can be recovered.

It has no dependency on Unity, Valheim, BepInEx, Direct3D or NVENC. Producers write raw audio and raw or encoded video through `IMediaWriter`.

FFmpeg must be installed separately. Released under the [MIT License](LICENSE).
