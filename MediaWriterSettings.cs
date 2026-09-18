using System;

namespace FFmpegMediaWriter
{
    // Describes one FFmpeg capture and finalization session.
    public enum AudioEncodingCodec { Mp3, Aac }

    public sealed class MediaWriterSettings
    {
        public string FfmpegPath { get; set; }
        public string TemporaryContainerPath { get; set; }
        public string ArchivePath { get; set; }
        public bool KeepIntermediateFile { get; set; }
        public string OutputPath { get; set; }
        public int MaximumFrameRate { get; set; }
        public int AudioSampleRate { get; set; }
        public int AudioChannels { get; set; }
        public AudioEncodingCodec AudioCodec { get; set; } = AudioEncodingCodec.Mp3;
        public int AudioBitRate { get; set; } = 192000;
        public int OutputAudioSampleRate { get; set; }
        public int OutputAudioChannels { get; set; }
        public bool EncodedVideoHasPresentationTimestamps { get; set; }
        public VideoStreamFormat VideoStreamFormat { get; set; } = VideoStreamFormat.H264;
        public Action<string> Warning { get; set; }
        public Action<Exception> Error { get; set; }
    }
}
