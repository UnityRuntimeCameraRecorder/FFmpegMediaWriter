# FFmpegMediaWriter

A .NET Standard 2.0 library that assembles encoded video and raw audio into an MP4. FFmpeg copies the video without recompressing it and encodes audio as AAC.

Used by [UnityMediaRecorder](https://github.com/end3rbyte/UnityMediaRecorder) for audio/video muxing and MP4 finalization. The DLL itself does not depend on Unity or a GPU vendor.

## Requirements

Windows, Linux or macOS with a compatible .NET runtime. Linux/macOS execution has not yet been tested.

Install [FFmpeg](https://ffmpeg.org/) separately. Its build must support H.264/HEVC input, MKV/MP4 output and AAC encoding.

## Download and setup

Download FFmpeg from the [official download page](https://ffmpeg.org/download.html), extract or install it, and supply its executable path in `MediaWriterSettings.FfmpegPath`. Keep any required companion DLLs and license files.

## Usage

Start a writer with output paths, video format, FPS ceiling and audio sample rate/channel count. Send complete encoded video packets with monotonic timestamps in microseconds, and interleaved float32 PCM audio.

Feed video first. Check write results: `false` means data was rejected. Stop producers before calling `FinishCapture()`, then poll `IsFinalizationCompleted` before calling `CompleteFinalization()` and `Dispose()`.

The intermediate MKV is deleted after success and retained on failure. To archive it, set `KeepIntermediateFile = true` and supply `ArchivePath`.

For Unity camera and audio capture, use [UnityMediaRecorder](https://github.com/end3rbyte/UnityMediaRecorder).

## Build

With the .NET 10 SDK, run `dotnet build FFmpegMediaWriter.csproj -c Release`.

Output: `bin/Release/netstandard2.0/FFmpegMediaWriter.dll`.

## License

Our code uses [MIT](LICENSE). FFmpeg's license and codec patent rights are separate.
