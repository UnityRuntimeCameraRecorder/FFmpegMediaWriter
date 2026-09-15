using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Landoria.FFmpegMediaWriter
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

        // Starts FFmpeg with raw video and audio pipe inputs.
        internal static FfmpegProcess Start(
            string ffmpegPath,
            string videoPipe,
            string audioPipe,
            int audioSampleRate,
            int audioChannels,
            string output,
            int width,
            int height,
            int frameRate,
            VideoStreamFormat videoStreamFormat,
            string gpuVendor)
        {
            string executable = ResolveExecutable(ffmpegPath);
            string encoder = videoStreamFormat == VideoStreamFormat.RawRgba
                ? SelectEncoder(executable, gpuVendor)
                : null;
            string arguments = BuildArguments(
                audioPipe,
                videoPipe,
                audioSampleRate,
                audioChannels,
                output,
                width,
                height,
                frameRate,
                videoStreamFormat,
                encoder);
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

        // Starts asynchronous MP4 creation with video copying or hardware transcoding.
        internal static FfmpegProcess StartFinalization(
            string ffmpegPath,
            string inputPath,
            string outputPath,
            string gpuVendor,
            bool copyVideo)
        {
            string executable = ResolveExecutable(ffmpegPath);
            string options = copyVideo
                ? "-c:v copy"
                : GetHevcOptions(SelectHevcEncoder(executable, gpuVendor));
            string arguments = $"-hide_banner -y -i \"{inputPath}\" {options} " +
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
            string executable = string.IsNullOrWhiteSpace(configuredPath) ? "ffmpeg.exe" : configuredPath;
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

        // Builds capture arguments for raw pipe inputs and a lossless container.
        private static string BuildArguments(
            string pipe,
            string videoPipe,
            int audioSampleRate,
            int audioChannels,
            string output,
            int width,
            int height,
            int frameRate,
            VideoStreamFormat videoStreamFormat,
            string encoder)
        {
            string rate = audioSampleRate.ToString(CultureInfo.InvariantCulture);
            string channels = audioChannels.ToString(CultureInfo.InvariantCulture);
            bool encodedVideo = videoStreamFormat != VideoStreamFormat.RawRgba;
            string videoOptions = encodedVideo ? null : GetVideoOptions(encoder);
            string inputFormat = GetInputFormat(videoStreamFormat);
            string videoInput = encodedVideo
                ? $"-probesize 32 -analyzeduration 0 -use_wallclock_as_timestamps 1 " +
                  $"-framerate {frameRate} -f {inputFormat} -i \"{videoPipe}\" "
                : $"-use_wallclock_as_timestamps 1 -f rawvideo -pixel_format rgba " +
                  $"-video_size {width}x{height} -framerate {frameRate} -i \"{videoPipe}\" ";
            string videoEncoding = encodedVideo ? "-c:v copy" : videoOptions;
            return $"-hide_banner -y {videoInput}" +
                   $"-f f32le -ar {rate} -ac {channels} -i \"{pipe}\" " +
                   $"-r {frameRate} {videoEncoding} -c:a pcm_f32le " +
                   $"-fps_mode cfr -f matroska \"{output}\"";
        }

        // Maps a backend stream description to its FFmpeg elementary input format.
        private static string GetInputFormat(VideoStreamFormat streamFormat)
        {
            switch (streamFormat)
            {
                case VideoStreamFormat.RawRgba:
                    return "rawvideo";
                case VideoStreamFormat.H264:
                    return "h264";
                case VideoStreamFormat.Hevc:
                    return "hevc";
                default:
                    throw new ArgumentOutOfRangeException(nameof(streamFormat));
            }
        }

        // Selects the best supported intermediate encoder for the active GPU.
        private static string SelectEncoder(string executable, string gpuVendor)
        {
            string encoders = ReadEncoderList(executable);
            string vendor = gpuVendor ?? string.Empty;
            if (vendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 && encoders.Contains("h264_nvenc"))
            {
                return "h264_nvenc";
            }

            if (vendor.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 && encoders.Contains("h264_amf"))
            {
                return "h264_amf";
            }

            if (vendor.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 && encoders.Contains("h264_qsv"))
            {
                return "h264_qsv";
            }

            return "h264_mf";
        }

        // Selects a supported hardware HEVC encoder for final compression.
        private static string SelectHevcEncoder(string executable, string gpuVendor)
        {
            string encoders = ReadEncoderList(executable);
            string vendor = gpuVendor ?? string.Empty;
            if (vendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 && encoders.Contains("hevc_nvenc"))
            {
                return "hevc_nvenc";
            }

            if (vendor.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 && encoders.Contains("hevc_amf"))
            {
                return "hevc_amf";
            }

            if (vendor.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 && encoders.Contains("hevc_qsv"))
            {
                return "hevc_qsv";
            }

            throw new NotSupportedException("No compatible hardware HEVC encoder is available in FFmpeg.");
        }

        // Reads the encoder capabilities reported by FFmpeg.
        private static string ReadEncoderList(string executable)
        {
            var info = CreateStartInfo(executable, "-hide_banner -encoders");
            info.RedirectStandardOutput = true;
            using (Process process = Process.Start(info))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("FFmpeg encoder detection did not start.");
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }

        // Returns lossless intermediate video options for the selected encoder.
        private static string GetVideoOptions(string encoder)
        {
            switch (encoder)
            {
                case "h264_nvenc":
                    return "-c:v h264_nvenc -preset p7 -tune lossless -pix_fmt yuv444p";
                default:
                    return "-c:v ffv1 -level 3 -coder 1 -context 1 -g 1 -pix_fmt bgra";
            }
        }

        // Returns very-high-quality HEVC options for the selected encoder.
        private static string GetHevcOptions(string encoder)
        {
            switch (encoder)
            {
                case "hevc_nvenc":
                    return "-c:v hevc_nvenc -preset p7 -tune hq -rc vbr -cq 16 -b:v 0";
                case "hevc_amf":
                    return "-c:v hevc_amf -quality quality -rc cqp -qp_i 16 -qp_p 18";
                case "hevc_qsv":
                    return "-c:v hevc_qsv -preset slow -global_quality 16";
                default:
                    throw new NotSupportedException($"Unsupported HEVC encoder: {encoder}");
            }
        }

    }
}
