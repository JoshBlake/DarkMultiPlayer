using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DarkMultiPlayerCommon
{
    public static class CompressionHelper
    {
        public static void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }

        public static byte[] Compress(string data)
        {
            byte[] stringBytes = Encoding.UTF8.GetBytes(data);

            return CLZF2.Compress(stringBytes);
        }

        public static string CompressBase64(string data)
        {
            byte[] bytes = Compress(data);
            return Convert.ToBase64String(bytes);
        }

        public static string Decompress(byte[] bytes)
        {
            byte[] stringBytes = CLZF2.Decompress(bytes);
            return Encoding.UTF8.GetString(stringBytes);
        }

        public static string DecompressBase64(string data64)
        {

            byte[] bytes = Convert.FromBase64String(data64);
            return Decompress(bytes);
        }
    }
}
