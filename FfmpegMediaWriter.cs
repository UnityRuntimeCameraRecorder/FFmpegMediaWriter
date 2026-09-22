using System;
using System.IO;

namespace FFmpegMediaWriter
{
    // Writes generic audio and video streams through an external FFmpeg process.
    public sealed class FfmpegMediaWriter : IMediaWriter
    {
        private MediaPipe _audioPipe;
        private MediaPipe _videoPipe;
        private FfmpegProcess _capture;
        private FfmpegProcess _finalization;
        private MediaWriterSettings _settings;
        private EncodedVideoTransport _videoTransport;
        public bool IsVideoInputConnected => _videoPipe?.IsConnected == true;
        public bool AreInputsConnected => IsVideoInputConnected && _audioPipe?.IsConnected == true;
        public bool IsFinalizing => _finalization != null;
        public bool IsFinalizationCompleted => _finalization?.HasExited == true;

        // Opens named inputs and starts FFmpeg for one recording session.
        public void Start(MediaWriterSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.FfmpegPath))
            {
                throw new ArgumentException("FfmpegPath must specify the external FFmpeg executable.", nameof(settings));
            }
            if (settings.VideoStreamFormat != VideoStreamFormat.H264 && settings.VideoStreamFormat != VideoStreamFormat.Hevc)
            {
                throw new NotSupportedException("The media writer accepts only encoded H.264 or HEVC video.");
            }

            if (!Enum.IsDefined(typeof(AudioEncodingCodec), settings.AudioCodec) || settings.AudioBitRate <= 0 || settings.OutputAudioSampleRate < 0 || settings.OutputAudioChannels < 0)
            {
                throw new ArgumentException("Invalid output audio configuration.", nameof(settings));
            }

            _videoTransport = settings.EncodedVideoHasPresentationTimestamps ? new EncodedVideoTransport(settings.MaximumFrameRate, settings.VideoStreamFormat) : null;
            MediaWriterLog.Warning = settings.Warning;
            MediaWriterLog.Error = settings.Error;
            try
            {
                _audioPipe = new MediaPipe("MediaWriterAudio", 256);
                _videoPipe = new MediaPipe("MediaWriterVideo", 64);
                _audioPipe.BeginWaitForConnection();
                _videoPipe.BeginWaitForConnection();
                _capture = FfmpegProcess.Start(settings.FfmpegPath, _videoPipe.Path, _audioPipe.Path, settings.AudioSampleRate, settings.AudioChannels, settings.TemporaryContainerPath, settings.MaximumFrameRate, settings.VideoStreamFormat, settings.EncodedVideoHasPresentationTimestamps);
            }
            catch
            {
                Abort();
                throw;
            }
        }

        // Starts background finalization from a container left by an interrupted capture.
        public void Recover(MediaWriterSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (!File.Exists(settings.TemporaryContainerPath))
            {
                throw new FileNotFoundException(
                    "The temporary recording container does not exist.",
                    settings.TemporaryContainerPath);
            }

            MediaWriterLog.Warning = settings.Warning;
            MediaWriterLog.Error = settings.Error;
            _finalization = FfmpegProcess.StartFinalization(
                settings.FfmpegPath, settings.TemporaryContainerPath,
                settings.OutputPath, settings.AudioCodec,
                settings.AudioBitRate, settings.OutputAudioSampleRate,
                settings.OutputAudioChannels);
        }

        // Queues one raw audio block without blocking its producer.
        public bool WriteAudio(byte[] data)
        {
            return _audioPipe?.Write(data) == true;
        }

        // Queues one timestamped encoded packet without breaking packet boundaries.
        public bool WriteVideoPacket(byte[] data, long timestampMicroseconds)
        {
            return _videoTransport != null ? _videoPipe?.Write(_videoTransport.Wrap(data, timestampMicroseconds)) == true : _videoPipe?.WritePacket(data, timestampMicroseconds) == true;
        }

        // Closes capture and starts background MP4 finalization.
        public void FinishCapture()
        {
            CloseCapture();
            _finalization = FfmpegProcess.StartFinalization(_settings.FfmpegPath, _settings.TemporaryContainerPath, _settings.OutputPath, _settings.AudioCodec, _settings.AudioBitRate, _settings.OutputAudioSampleRate, _settings.OutputAudioChannels);
        }

        // Validates finalized output and deletes the intermediate container after success.
        public void CompleteFinalization()
        {
            if (_finalization == null || !_finalization.HasExited)
            {
                throw new InvalidOperationException("Finalization is not complete.");
            }

            bool succeeded = _finalization.Succeeded && File.Exists(_settings.OutputPath) && new FileInfo(_settings.OutputPath).Length > 0;
            _finalization.Dispose();
            _finalization = null;
            if (!succeeded)
            {
                throw new InvalidOperationException($"FFmpeg did not create a valid output file; {_settings.TemporaryContainerPath} was kept.");
            }

            File.Delete(_settings.TemporaryContainerPath);
        }

        // Stops active work without creating an output file.
        public void Abort()
        {
            _audioPipe?.Dispose();
            _audioPipe = null;
            _videoPipe?.Dispose();
            _videoPipe = null;
            _capture?.Stop();
            _capture = null;
            _finalization?.Dispose();
            _finalization = null;
        }

        // Releases all writer resources.
        public void Dispose()
        {
            Abort();
        }

        // Closes pipes before requesting an orderly FFmpeg shutdown.
        private void CloseCapture()
        {
            MediaPipe audioPipe = _audioPipe;
            MediaPipe videoPipe = _videoPipe;
            _audioPipe = null;
            _videoPipe = null;
            audioPipe?.CompleteWriting();
            videoPipe?.CompleteWriting();
            audioPipe?.WaitForCompletion();
            videoPipe?.WaitForCompletion();
            audioPipe?.Dispose();
            videoPipe?.Dispose();
            _capture?.WaitForExit();
            _capture = null;
        }
    }
}
