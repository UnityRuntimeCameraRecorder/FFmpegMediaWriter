using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;

namespace Landoria.FFmpegMediaWriter
{
    // Streams queued media buffers to FFmpeg through a named pipe.
    internal sealed class MediaPipe : IDisposable
    {
        // Stores one media buffer and its optional monotonic presentation timestamp.
        private sealed class MediaBuffer
        {
            internal byte[] Data { get; set; }
            internal long? TimestampMicroseconds { get; set; }
        }

        private readonly BlockingCollection<MediaBuffer> _queue;
        private readonly NamedPipeServerStream _stream;
        private Thread _writerThread;
        private bool _disposed;

        // Creates a uniquely named bounded media pipe.
        internal MediaPipe(string prefix, int capacity)
        {
            string name = $"{prefix}_{Guid.NewGuid():N}";
            Path = $@"\\.\pipe\{name}";
            _queue = new BlockingCollection<MediaBuffer>(capacity);
            _stream = new NamedPipeServerStream(
                name,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.WriteThrough,
                0,
                16 * 1024 * 1024);
        }

        internal string Path { get; }
        internal bool IsConnected => !_disposed && _stream.IsConnected;

        // Starts the background writer that waits for FFmpeg to connect.
        internal void BeginWaitForConnection()
        {
            _writerThread = new Thread(ConnectAndWrite) { IsBackground = true };
            _writerThread.Start();
        }

        // Enqueues a media buffer without blocking its producer thread.
        internal bool Write(byte[] buffer)
        {
            return !_disposed && _queue.TryAdd(new MediaBuffer { Data = buffer });
        }

        // Enqueues a timestamped indivisible packet while preserving stream integrity.
        internal bool WritePacket(byte[] buffer, long timestampMicroseconds)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                _queue.Add(new MediaBuffer
                {
                    Data = buffer,
                    TimestampMicroseconds = timestampMicroseconds
                });
                return true;
            }
            catch (InvalidOperationException) when (_disposed || _queue.IsAddingCompleted)
            {
                return false;
            }
        }

        // Stops the writer and releases the pipe and queue resources.
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _stream.Dispose();
            if (_writerThread?.IsAlive == true && !_writerThread.Join(5_000))
            {
                MediaWriterLog.WriteWarning("A media pipe writer did not stop within five seconds.");
            }

            _queue.Dispose();
        }

        // Accepts the FFmpeg connection and drains queued buffers.
        private void ConnectAndWrite()
        {
            try
            {
                _stream.WaitForConnection();
                WriteQueuedData();
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
            catch (Exception exception)
            {
                MediaWriterLog.WriteError(exception);
            }
        }

        // Writes every queued buffer to the connected pipe in order.
        private void WriteQueuedData()
        {
            try
            {
                long? firstTimestamp = null;
                var playbackClock = new Stopwatch();
                foreach (MediaBuffer buffer in _queue.GetConsumingEnumerable())
                {
                    if (buffer.TimestampMicroseconds.HasValue)
                    {
                        if (!firstTimestamp.HasValue)
                        {
                            firstTimestamp = buffer.TimestampMicroseconds.Value;
                            playbackClock.Start();
                        }

                        long dueMicroseconds = buffer.TimestampMicroseconds.Value - firstTimestamp.Value;
                        WaitUntil(playbackClock, dueMicroseconds);
                    }

                    _stream.Write(buffer.Data, 0, buffer.Data.Length);
                }
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
            catch (Exception exception)
            {
                MediaWriterLog.WriteError(exception);
            }
        }

        // Waits until a timestamp is due while remaining responsive to disposal.
        private void WaitUntil(Stopwatch clock, long dueMicroseconds)
        {
            while (!_disposed)
            {
                long elapsedMicroseconds = clock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
                long remainingMicroseconds = dueMicroseconds - elapsedMicroseconds;
                if (remainingMicroseconds <= 0)
                {
                    return;
                }

                if (remainingMicroseconds > 2_000)
                {
                    Thread.Sleep((int)Math.Min(remainingMicroseconds / 1_000 - 1, 10));
                }
                else
                {
                    Thread.SpinWait(64);
                }
            }
        }
    }
}
