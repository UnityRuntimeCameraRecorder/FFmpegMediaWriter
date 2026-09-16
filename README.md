# FFmpegMediaWriter

A cross-platform .NET library that assembles encoded video and raw audio into an MP4. It does not capture images or encode video, and does not depend on Unity or a GPU vendor.

## Requirements

Use the same .NET Standard 2.0 DLL on Windows, Linux or macOS with a compatible runtime, including .NET Framework 4.8 and compatible Unity versions. Windows uses named pipes; Linux/macOS use TCP bound to `127.0.0.1` on automatically assigned ports.

Install [FFmpeg](https://ffmpeg.org/) separately, including any DLLs required by your chosen build. The library launches its executable in the background; FFmpeg is not embedded in this DLL. The build must support H.264/HEVC input, MKV/MP4 output and AAC encoding.

Supply the executable path in `FfmpegPath`. Relative paths use the application's current working directory.

On Linux/macOS, install FFmpeg with your package manager and supply its executable path, for example `/usr/bin/ffmpeg` on Linux. Ensure the executable has permission to run and supports the TCP protocol.

## Download and setup

1. Download FFmpeg for Windows from the [official download page](https://ffmpeg.org/download.html).
2. Right-click the ZIP and choose **Extract All**. Keep the extracted files together; do not run the executable from inside the ZIP.
3. Find `bin\ffmpeg.exe` in the extracted folder and copy its full path.
4. Supply that path in `MediaWriterSettings.FfmpegPath` as shown below. The installation folder is your choice; the example path is not a default.

Keep the license files supplied with your chosen build. FFmpeg licensing and codec patent rights are separate.

## Code example

Create the output directory and choose unused filenames. The executable path below is an example, not a default.

```csharp
using FFmpegMediaWriter;

var writer = new FfmpegMediaWriter();
writer.Start(new MediaWriterSettings
{
    FfmpegPath = @"C:\tools\ffmpeg\bin\ffmpeg.exe",
    TemporaryContainerPath = @"C:\Captures\session.mkv.tmp",
    OutputPath = @"C:\Captures\session.mp4",
    VideoStreamFormat = VideoStreamFormat.Hevc,
    MaximumFrameRate = 60,
    AudioSampleRate = 48000,
    AudioChannels = 2,
    KeepIntermediateFile = false
});
```

Your producers supply complete encoded video packets and interleaved float32 PCM audio. Video timestamps are monotonic microseconds from a shared session clock. Check return values: `false` means the queue rejected the data. Feed video first; do not wait for both inputs to connect before sending initial video packets.

```csharp
bool videoAccepted = writer.WriteVideoPacket(encodedPacket, timestampMicroseconds);
bool audioAccepted = writer.WriteAudio(float32AudioBytes);
```

Stop the producers, then finalize:

```csharp
writer.FinishCapture();

// Poll later, without blocking the rendering thread.
if (writer.IsFinalizationCompleted)
{
    writer.CompleteFinalization();
    writer.Dispose();
}
```

FFmpeg copies the video without recompressing it and encodes audio as AAC. The MKV is deleted after success, but retained on finalization failure. To keep it after success, set `KeepIntermediateFile = true` and supply `ArchivePath`. Dispose the writer on errors too.

## Build and snapshots

Install the .NET 10 SDK, then run `dotnet build FFmpegMediaWriter.csproj -c Release` on Windows, Linux or macOS. Output: `bin/Release/netstandard2.0/FFmpegMediaWriter.dll`. FFmpeg is not needed to compile.

Our code uses the [MIT license](LICENSE). FFmpeg's license and codec patent rights are separate.
