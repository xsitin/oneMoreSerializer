namespace Serialization
{
    public interface ICompressor
    {
        public byte[] Compress(byte[] data);
        public byte[] Decompress(byte[] data);
    }
}