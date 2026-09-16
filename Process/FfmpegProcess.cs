using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FFmpegMediaWriter
{
    // Owns an FFmpeg process used for capture or background MP4 finalization.
    internal sealed class FfmpegProcess : IDisposable
    {
        private readonly Process _process;

        // Wraps an already started FFmpeg process.
        private FfmpegProcess(Process process)
        {
            _process = process;
        }

        internal bool HasExited => _process.HasExited;
        internal bool Succeeded => _process.HasExited && _process.ExitCode == 0;

        // Starts FFmpeg with encoded video packets and raw audio samples.
        internal static FfmpegProcess Start(
            string ffmpegPath,
            string videoPipe,
            string audioPipe,
            int audioSampleRate,
            int audioChannels,
            string output,
            int frameRate,
            VideoStreamFormat videoStreamFormat)
        {
            string executable = ResolveExecutable(ffmpegPath);
            string arguments = BuildArguments(
                audioPipe,
                videoPipe,
                audioSampleRate,
                audioChannels,
                output,
                frameRate,
                videoStreamFormat);
            Process process = Process.Start(CreateStartInfo(executable, arguments));
            if (process == null)
            {
                throw new InvalidOperationException("FFmpeg did not start.");
            }

            return new FfmpegProcess(process);
        }

        // Requests an orderly FFmpeg shutdown and kills it after a timeout.
        internal void Stop()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.WriteLine("q");
                    if (!_process.WaitForExit(15_000))
                    {
                        _process.Kill();
                    }
                }
            }
            catch (Exception exception)
            {
                MediaWriterLog.WriteError(exception);
            }
            finally
            {
                _process.Dispose();
            }
        }

        // Waits for FFmpeg to finish after both media inputs reach end-of-stream.
        internal void WaitForExit()
        {
            try
            {
                if (!_process.HasExited && !_process.WaitForExit(30_000))
                {
                    MediaWriterLog.WriteWarning("FFmpeg did not finish within thirty seconds and was stopped.");
                    _process.StandardInput.WriteLine("q");
                    if (!_process.WaitForExit(5_000))
                    {
                        _process.Kill();
                    }
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        // Starts background MP4 muxing with video stream copying and AAC audio encoding.
        internal static FfmpegProcess StartFinalization(
            string ffmpegPath,
            string inputPath,
            string outputPath)
        {
            string executable = ResolveExecutable(ffmpegPath);
            string arguments = $"-hide_banner -y -i \"{inputPath}\" -c:v copy " +
                               $"-c:a aac -b:a 192k -shortest -movflags +faststart \"{outputPath}\"";
            Process process = Process.Start(CreateStartInfo(executable, arguments));
            if (process == null)
            {
                throw new InvalidOperationException("FFmpeg finalization did not start.");
            }

            return new FfmpegProcess(process);
        }

        // Releases the wrapped process resources.
        public void Dispose()
        {
            _process.Dispose();
        }

        // Resolves and validates the configured FFmpeg executable.
        private static string ResolveExecutable(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new ArgumentException("An explicit FFmpeg executable path is required.", nameof(configuredPath));
            }
            string executable = Path.GetFullPath(configuredPath);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("The configured FFmpeg executable does not exist.", executable);
            }
            using (Process process = Process.Start(CreateStartInfo(executable, "-hide_banner -version")))
            {
                if (process == null || !process.WaitForExit(5_000) || process.ExitCode != 0)
                {
                    throw new FileNotFoundException($"FFmpeg is unavailable: {executable}");
                }
            }

            return executable;
        }

        // Creates hidden process settings suitable for background FFmpeg work.
        private static ProcessStartInfo CreateStartInfo(string executable, string arguments)
        {
            return new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        // Builds muxing arguments without invoking a video encoder.
        private static string BuildArguments(
            string audioPipe,
            string videoPipe,
            int audioSampleRate,
            int audioChannels,
            string output,
            int frameRate,
            VideoStreamFormat videoStreamFormat)
        {
            string rate = audioSampleRate.ToString(CultureInfo.InvariantCulture);
            string channels = audioChannels.ToString(CultureInfo.InvariantCulture);
            string inputFormat = GetInputFormat(videoStreamFormat);
            return $"-hide_banner -y -probesize 32 -analyzeduration 0 -use_wallclock_as_timestamps 1 " +
                   $"-framerate {frameRate} -f {inputFormat} -i \"{videoPipe}\" " +
                   $"-f f32le -ar {rate} -ac {channels} -i \"{audioPipe}\" " +
                   $"-r {frameRate} -c:v copy -c:a pcm_f32le " +
                   $"-fps_mode cfr -f matroska \"{output}\"";
        }

        // Maps a backend stream description to its FFmpeg elementary input format.
        private static string GetInputFormat(VideoStreamFormat streamFormat)
        {
            switch (streamFormat)
            {
                case VideoStreamFormat.H264:
                    return "h264";
                case VideoStreamFormat.Hevc:
                    return "hevc";
                default:
                    throw new ArgumentOutOfRangeException(nameof(streamFormat));
            }
        }

    }
}
