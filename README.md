# FFmpegMediaWriter

A .NET Standard 2.0 library that assembles encoded video and raw audio into an MP4. FFmpeg copies the video without recompressing it and encodes audio as MP3 at 192 kb/s.

Used by [UnityRuntimeCameraRecorder](https://github.com/end3rbyte/UnityRuntimeCameraRecorder) for audio/video muxing and MP4 finalization. The DLL itself does not depend on Unity or a GPU vendor.

## Requirements

Windows, Linux or macOS with a compatible .NET runtime. Linux/macOS execution has not yet been tested.

Install [FFmpeg](https://ffmpeg.org/) separately. Its build must support H.264/HEVC input, MKV/MP4 output and MP3 encoding with libmp3lame.

## Download and setup

Download FFmpeg from the [official download page](https://ffmpeg.org/download.html), extract or install it, and supply its executable path in `MediaWriterSettings.FfmpegPath`. Keep any required companion DLLs and license files.

## Usage

Start a writer with output paths, video format, FPS ceiling and audio sample rate/channel count. Send complete encoded video packets with monotonic timestamps in microseconds, and interleaved float32 PCM audio.

Feed video first. Check write results: `false` means data was rejected. Stop producers before calling `FinishCapture()`, then poll `IsFinalizationCompleted` before calling `CompleteFinalization()` and `Dispose()`.

The intermediate MKV is deleted after success and retained on failure. To archive it, set `KeepIntermediateFile = true` and supply `ArchivePath`.

For Unity camera and audio capture, use [UnityRuntimeCameraRecorder](https://github.com/end3rbyte/UnityRuntimeCameraRecorder).

## Build

With the .NET 10 SDK, run `dotnet build FFmpegMediaWriter.csproj -c Release`.

Output: `bin/Release/netstandard2.0/FFmpegMediaWriter.dll`.

## License

Our code uses [MIT](LICENSE). FFmpeg's license and codec patent rights are separate.

## AAC audio output

Media Recorder supplies these settings; FFmpegMediaWriter encodes the final audio using FFmpeg's native `aac` encoder.

| Setting | Low | Medium | High |
|---|---|---|---|
| Codec | AAC | AAC | AAC |
| Target bitrate | 128 kbit/s | 192 kbit/s | 192 kbit/s |
| Sample rate / channels | 48000 Hz / stereo | Same | Same |

Capture accepts interleaved float32 PCM at the Unity input format, preserves PCM in the intermediate MKV, then resamples/mixes and encodes AAC during MP4 finalization. Video is copied without re-encoding. The measured audio bitrate may differ from its target.

API: `AudioCodec`, `AudioBitRate` (bits/s), `OutputAudioSampleRate`, `OutputAudioChannels`. Zero output rate/channels retain the input format. Standalone writer defaults remain MP3 192000 bit/s; the table describes Media Recorder profiles. The FFmpeg executable must include `aac`.

Source: [FFmpeg AAC encoder documentation](https://ffmpeg.org/ffmpeg-codecs.html#aac). The Low/Medium/High bitrate mapping is our profile policy, not an FFmpeg preset. **HDR video is not supported today by the Media Recorder pipeline; AAC has no HDR setting.**

## Encoded video timestamps

For encoders with B-frames, set `EncodedVideoHasPresentationTimestamps = true` and pass each access unit with its source monotonic presentation timestamp. The writer adds MPEG-TS PTS/DTS headers (H.264 or HEVC) without changing the elementary payload, allowing FFmpeg to remux decode-order packets correctly. This mode assumes a fixed requested frame cadence with at most 2 B-frames; DTS advances at that cadence and PTS preserves captured presentation gaps. The default remains the legacy raw elementary stream/wall-clock path for existing consumers. Final audio is encoded independently.
