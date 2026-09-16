using System;

namespace FFmpegMediaWriter
{
    // Routes background writer diagnostics through the active session callbacks.
    internal static class MediaWriterLog
    {
        internal static Action<string> Warning { get; set; }
        internal static Action<Exception> Error { get; set; }

        // Reports a warning when a callback is configured.
        internal static void WriteWarning(string message) { Warning?.Invoke(message); }
        // Reports an exception when a callback is configured.
        internal static void WriteError(Exception exception) { Error?.Invoke(exception); }
    }
}
