using System;

namespace Ekiden.Persistence.Container
{
    /// <summary>CRC32(多項式 0xEDB88320。検査値 CRC32("123456789") = 0xCBF43926。09 §4)。表引き。</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        /// <summary>続きから計算する(初回は crc = 0)。</summary>
        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            uint c = ~crc;
            for (int i = 0; i < data.Length; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return ~c;
        }

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Update(0, data);
        }
    }
}
