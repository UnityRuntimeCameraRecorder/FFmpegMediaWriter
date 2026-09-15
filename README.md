# Landoria.FFmpegMediaWriter

Generic .NET Framework 4.8 library that sends audio and video streams to an external FFmpeg process, creates an intermediate MKV container and finalizes an MP4 output.

It has no dependency on Unity, Valheim, BepInEx, Direct3D or NVENC. Producers write raw audio and raw or encoded video through `IMediaWriter`.

FFmpeg must be installed separately. Released under the [MIT License](LICENSE).
