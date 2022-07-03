using System.IO;
using System.IO.Compression;

namespace Serialization
{
    public class SZipCompressor : ICompressor
    {
        public byte[] Compress(byte[] bytes)
        {
            return SevenZip.Compression.LZMA.SevenZipHelper.Compress(bytes);
        }


        public byte[] Decompress(byte[] bytes)
        {
            return SevenZip.Compression.LZMA.SevenZipHelper.Decompress(bytes);
        }
    }
}