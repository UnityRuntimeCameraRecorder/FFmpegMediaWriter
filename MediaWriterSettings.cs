using System;

namespace Landoria.FFmpegMediaWriter
{
    // Describes one FFmpeg capture and finalization session.
    public sealed class MediaWriterSettings
    {
        public string FfmpegPath { get; set; }
        public string TemporaryContainerPath { get; set; }
        public string ArchivePath { get; set; }
        public string OutputPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MaximumFrameRate { get; set; }
        public int AudioSampleRate { get; set; }
        public int AudioChannels { get; set; }
        public string GraphicsDeviceVendor { get; set; }
        public VideoStreamFormat VideoStreamFormat { get; set; }
        public Action<string> Warning { get; set; }
        public Action<Exception> Error { get; set; }
    }
}
