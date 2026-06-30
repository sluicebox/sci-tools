namespace SCI.Resource.Decompressors
{
    public enum Compression
    {
        None,
        Huffman,
        Lzw0,
        Lzw1,
        Lzw1View,
        Lzw1Pic,
        DclImplode,
        StackerLzs
    }

    public enum CompressionFormat
    {
        SCI0,
        SCI1
    }

    public static class Decompressor
    {
        public static byte[] Decompress(Compression compression, Span source, int uncompressedSize)
        {
            switch (compression)
            {
                case Compression.Huffman:
                    return HuffmanDecompressor.Decompress(source, uncompressedSize);
                case Compression.Lzw0:
                    return LzwDecompressor.Decompress(CompressionFormat.SCI0, source, uncompressedSize);
                case Compression.Lzw1:
                    return LzwDecompressor.Decompress(CompressionFormat.SCI1, source, uncompressedSize);
                case Compression.Lzw1View:
                    return LzwViewPicDeompressor.DecompressView(source, uncompressedSize);
                case Compression.Lzw1Pic:
                    return LzwViewPicDeompressor.DecompressPic(source, uncompressedSize);
                case Compression.DclImplode:
                    return DclImplodeDecompressor.Decompress(source, uncompressedSize);
                case Compression.StackerLzs:
                   return StackerLzsDecompressor.Decompress(source, uncompressedSize);
            }
            return null;
        }
    }
}