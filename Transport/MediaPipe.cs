using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FFmpegMediaWriter
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
        private readonly NamedPipeServerStream _pipe;
        private readonly TcpListener _listener;
        private TcpClient _client;
        private Stream _stream;
        private Thread _writerThread;
        private volatile bool _disposed;
        private volatile bool _connected;
        private bool _writingCompleted;

        // Creates a uniquely named bounded media pipe.
        internal MediaPipe(string prefix, int capacity)
            : this(prefix, capacity, Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
        }

        // Selects the local transport explicitly for platform-independent transport tests.
        internal MediaPipe(string prefix, int capacity, bool useWindowsPipe)
        {
            string name = $"{prefix}_{Guid.NewGuid():N}";
            _queue = new BlockingCollection<MediaBuffer>(capacity);
            if (useWindowsPipe)
            {
                Path = $@"\\.\pipe\{name}";
                _pipe = new NamedPipeServerStream(
                    name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                    PipeOptions.WriteThrough, 0, 16 * 1024 * 1024);
                _stream = _pipe;
            }
            else
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(1);
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Path = $"tcp://127.0.0.1:{port}";
            }
        }

        internal string Path { get; }
        internal bool IsConnected => !_disposed && _connected;

        // Starts the background writer that waits for FFmpeg to connect.
        internal void BeginWaitForConnection()
        {
            _writerThread = new Thread(ConnectAndWrite) { IsBackground = true };
            _writerThread.Start();
        }

        // Enqueues a media buffer without blocking its producer thread.
        internal bool Write(byte[] buffer)
        {
            if (_disposed)
            {
                return false;
            }
            try
            {
                return _queue.TryAdd(new MediaBuffer { Data = buffer });
            }
            catch (InvalidOperationException) when (_disposed || _queue.IsAddingCompleted)
            {
                return false;
            }
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

        // Prevents new buffers while allowing the writer thread to drain everything already queued.
        internal void CompleteWriting()
        {
            if (_disposed || _writingCompleted)
            {
                return;
            }

            _writingCompleted = true;
            _queue.CompleteAdding();
        }

        // Waits for queued buffers to reach FFmpeg before closing the pipe endpoint.
        internal void WaitForCompletion()
        {
            if (_writerThread?.IsAlive == true && !_writerThread.Join(30_000))
            {
                MediaWriterLog.WriteWarning("A media pipe did not drain within thirty seconds.");
            }
        }

        // Stops the writer and releases the pipe and queue resources.
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CompleteWriting();
            _disposed = true;
            _listener?.Stop();
            _client?.Close();
            _stream?.Dispose();
            if (_writerThread?.IsAlive == true && !_writerThread.Join(5_000))
            {
                MediaWriterLog.WriteWarning("A media pipe writer did not stop within five seconds.");
            }

            if (_writerThread?.IsAlive != true)
            {
                _queue.Dispose();
            }
        }

        // Accepts the FFmpeg connection and drains queued buffers.
        private void ConnectAndWrite()
        {
            try
            {
                if (_pipe != null)
                {
                    _pipe.WaitForConnection();
                }
                else
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    _client = client;
                    _listener.Stop();
                    if (_disposed)
                    {
                        client.Close();
                        return;
                    }
                    client.NoDelay = true;
                    _stream = client.GetStream();
                }
                _connected = true;
                WriteQueuedData();
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
            catch (Exception exception)
            {
                if (!_disposed)
                {
                    MediaWriterLog.WriteError(exception);
                }
            }
            finally
            {
                _connected = false;
                _stream?.Dispose();
                _client?.Close();
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
                if (!_disposed)
                {
                    MediaWriterLog.WriteError(exception);
                }
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
