using System;

namespace FFmpegMediaWriter
{
    // Defines a replaceable destination for synchronized audio and video streams.
    public interface IMediaWriter : IDisposable
    {
        bool IsVideoInputConnected { get; }
        bool AreInputsConnected { get; }
        bool IsFinalizing { get; }
        bool IsFinalizationCompleted { get; }

        // Opens the media inputs and starts the container writer.
        void Start(MediaWriterSettings settings);
        // Queues one raw audio block.
        bool WriteAudio(byte[] data);
        // Queues one encoded video packet with its presentation timestamp.
        bool WriteVideoPacket(byte[] data, long timestampMicroseconds);
        // Closes capture and starts output finalization.
        void FinishCapture();
        // Validates finalized output and archives the intermediate container.
        void CompleteFinalization();
        // Stops all work without starting finalization.
        void Abort();
    }
}
