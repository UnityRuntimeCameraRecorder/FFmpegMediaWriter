using System;
using System.IO;

namespace FFmpegMediaWriter
{
    // Wraps Annex-B access units in MPEG-TS so FFmpeg receives distinct PTS and DTS.
    // The elementary video is copied unchanged; only transport headers are added.
    internal sealed class EncodedVideoTransport
    {
        private readonly int _fps;
        private readonly byte _streamType;
        private long? _origin;
        private long _frames;
        private int _videoContinuity, _patContinuity, _pmtContinuity;
        internal EncodedVideoTransport(int fps, VideoStreamFormat codec)
        {
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
            _fps = fps;
            _streamType = codec == VideoStreamFormat.H264 ? (byte)0x1b : (byte)0x24;
        }
        internal byte[] Wrap(byte[] data, long presentationMicroseconds)
        {
            if (!_origin.HasValue) _origin = presentationMicroseconds;
            long pts = 90000 + (presentationMicroseconds - _origin.Value) * 90 / 1000 + 180000 / _fps;
            long dts = 90000 + _frames++ * 90000 / _fps;
            using (var output = new MemoryStream())
            {
                WriteSection(output, 0, new byte[] { 0, 0xb0, 13, 0, 1, 0xc1, 0, 0, 0, 1, 0xe1, 0 }, ref _patContinuity);
                WriteSection(output, 0x100, new byte[] { 2, 0xb0, 18, 0, 1, 0xc1, 0, 0, 0xe1, 1, 0xf0, 0, _streamType, 0xe1, 1, 0xf0, 0 }, ref _pmtContinuity);
                using (var pes = new MemoryStream())
                {
                    pes.Write(new byte[] { 0, 0, 1, 0xe0, 0, 0, 0x80, 0xc0, 10 }, 0, 9);
                    WriteTime(pes, pts, 3); WriteTime(pes, dts, 1);
                    pes.Write(data, 0, data.Length);
                    byte[] payload = pes.ToArray();
                    int offset = 0;
                    while (offset < payload.Length)
                    {
                        bool first = offset == 0;
                        int count = Math.Min(first ? 176 : 184, payload.Length - offset);
                        byte[] packet = new byte[188];
                        for (int i = 0; i < packet.Length; i++) packet[i] = 0xff;
                        packet[0] = 0x47; packet[1] = (byte)(1 | (first ? 0x40 : 0)); packet[2] = 1;
                        int adaptation = 184 - count;
                        packet[3] = (byte)((adaptation > 0 ? 0x30 : 0x10) | (_videoContinuity++ & 15));
                        int destination = 4;
                        if (adaptation > 0)
                        {
                            packet[4] = (byte)(adaptation - 1);
                            if (adaptation > 1) packet[5] = first ? (byte)0x10 : (byte)0;
                            if (first)
                            {
                                long clock = dts & 0x1ffffffffL;
                                packet[6] = (byte)(clock >> 25); packet[7] = (byte)(clock >> 17);
                                packet[8] = (byte)(clock >> 9); packet[9] = (byte)(clock >> 1);
                                packet[10] = (byte)(((clock & 1) << 7) | 0x7e); packet[11] = 0;
                            }
                            destination += adaptation;
                        }
                        Buffer.BlockCopy(payload, offset, packet, destination, count);
                        output.Write(packet, 0, packet.Length); offset += count;
                    }
                }
                return output.ToArray();
            }
        }
        private static void WriteTime(Stream output, long value, int prefix)
        {
            ulong time = (ulong)value & 0x1ffffffffUL;
            output.WriteByte((byte)(((ulong)prefix << 4) | ((time >> 29) & 14) | 1));
            output.WriteByte((byte)(time >> 22)); output.WriteByte((byte)(((time >> 14) & 0xfe) | 1));
            output.WriteByte((byte)(time >> 7)); output.WriteByte((byte)(((time << 1) & 0xfe) | 1));
        }
        private static void WriteSection(Stream output, int pid, byte[] section, ref int continuity)
        {
            byte[] packet = new byte[188];
            for (int i = 0; i < packet.Length; i++) packet[i] = 0xff;
            packet[0] = 0x47; packet[1] = (byte)(0x40 | (pid >> 8)); packet[2] = (byte)pid;
            packet[3] = (byte)(0x10 | (continuity++ & 15)); packet[4] = 0;
            Buffer.BlockCopy(section, 0, packet, 5, section.Length);
            uint crc = 0xffffffff;
            foreach (byte value in section)
            {
                crc ^= (uint)value << 24;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
            }
            for (int i = 0; i < 4; i++) packet[5 + section.Length + i] = (byte)(crc >> (24 - i * 8));
            output.Write(packet, 0, packet.Length);
        }
    }
}
