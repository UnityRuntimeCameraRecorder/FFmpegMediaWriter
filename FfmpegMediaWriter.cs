using System;
using System.IO;

namespace Landoria.FFmpegMediaWriter
{
    // Writes generic audio and video streams through an external FFmpeg process.
    public sealed class FfmpegMediaWriter : IMediaWriter
    {
        private MediaPipe _audioPipe;
        private MediaPipe _videoPipe;
        private FfmpegProcess _capture;
        private FfmpegProcess _finalization;
        private MediaWriterSettings _settings;

        public bool IsVideoInputConnected => _videoPipe?.IsConnected == true;
        public bool AreInputsConnected => IsVideoInputConnected && _audioPipe?.IsConnected == true;
        public bool IsFinalizing => _finalization != null;
        public bool IsFinalizationCompleted => _finalization?.HasExited == true;

        // Opens named inputs and starts FFmpeg for one recording session.
        public void Start(MediaWriterSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            MediaWriterLog.Warning = settings.Warning;
            MediaWriterLog.Error = settings.Error;
            _audioPipe = new MediaPipe("MediaWriterAudio", 256);
            _videoPipe = new MediaPipe("MediaWriterVideo", 64);
            _audioPipe.BeginWaitForConnection();
            _videoPipe.BeginWaitForConnection();
            _capture = FfmpegProcess.Start(settings.FfmpegPath, _videoPipe.Path, _audioPipe.Path,
                settings.AudioSampleRate, settings.AudioChannels, settings.TemporaryContainerPath,
                settings.Width, settings.Height, settings.MaximumFrameRate,
                settings.VideoStreamFormat, settings.GraphicsDeviceVendor);
        }

        // Queues one raw audio block without blocking its producer.
        public bool WriteAudio(byte[] data) { return _audioPipe?.Write(data) == true; }
        // Queues one raw video frame without blocking its producer.
        public bool WriteVideoFrame(byte[] data) { return _videoPipe?.Write(data) == true; }
        // Queues one timestamped encoded packet without breaking packet boundaries.
        public bool WriteVideoPacket(byte[] data, long timestampMicroseconds)
        { return _videoPipe?.WritePacket(data, timestampMicroseconds) == true; }

        // Closes capture and starts background MP4 finalization.
        public void FinishCapture()
        {
            CloseCapture();
            _finalization = FfmpegProcess.StartFinalization(_settings.FfmpegPath,
                _settings.TemporaryContainerPath, _settings.OutputPath,
                _settings.GraphicsDeviceVendor, _settings.VideoStreamFormat != VideoStreamFormat.RawRgba);
        }

        // Validates finalized output and preserves the intermediate MKV archive.
        public void CompleteFinalization()
        {
            if (_finalization == null || !_finalization.HasExited) throw new InvalidOperationException("Finalization is not complete.");
            bool succeeded = _finalization.Succeeded && File.Exists(_settings.OutputPath) && new FileInfo(_settings.OutputPath).Length > 0;
            _finalization.Dispose();
            _finalization = null;
            if (!succeeded) throw new InvalidOperationException($"FFmpeg did not create a valid output file; {_settings.TemporaryContainerPath} was kept.");
            File.Move(_settings.TemporaryContainerPath, _settings.ArchivePath);
        }

        // Stops active work without creating an output file.
        public void Abort() { CloseCapture(); _finalization?.Dispose(); _finalization = null; }
        // Releases all writer resources.
        public void Dispose() { Abort(); }

        // Closes pipes before requesting an orderly FFmpeg shutdown.
        private void CloseCapture()
        {
            _audioPipe?.Dispose(); _audioPipe = null;
            _videoPipe?.Dispose(); _videoPipe = null;
            _capture?.Stop(); _capture = null;
        }
    }
}
