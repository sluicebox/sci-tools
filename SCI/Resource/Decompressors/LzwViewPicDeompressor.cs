using System;

// SCI1 VGA views and pics use format-specific compression before applying LZW.
//
// Both formats are preprocessed to remove known bytes and reorder sections for
// better LZW compression. For example, an embedded palette always has a table
// with the same 256 bytes, so the table is discarded. After the resource is
// decompressed with LZW, the known table is added back.
//
// Views are restructured to group data by type for efficient LZW compression.
// Cel headers are grouped together, and each cel's image is parsed to group all
// the RLE compression bytes from all the cels followed by all the pixel bytes.
//
// Pics are restructured by assuming they start with a SetPalette instruction
// and contain an EmbeddedView instruction that draws to coordinates (0,0).
// The known bytes for these two instructions are discarded and any instructions
// before and after EmbeddedView are stored in two sections. The embedded view's
// cel image is also separated into RLE and pixel bytes. If a pic differs from
// the required structure then the normal LZW1 compression method is used.
//
// According to debug symbols, these routines are named dcompview and dcomppic.
//
// Sierra's dcompview has an inconsequential bug that writes cel header data to
// the 4 byte palette timestamp. The interpreter doesn't use this value so it
// shouldn't matter, but most SCI tools implement this behavior because their
// decompression code descends from Carl's SCI Decoder. I want my output to
// match theirs for testing, so I've joined in and cheerfully added the bug too.
// Only SCI Viewer is above all this, it sets the timestamp to zero. Respect.
//
// Note that the uncompressed size passed to LZW decompression is not the size
// of LZW decompression, it is the size after both stages of decompression.

namespace SCI.Resource.Decompressors
{
    public static class LzwViewPicDeompressor
    {
        public static byte[] DecompressView(Span span, int uncompressedSize)
        {
            //
            // Decompress using LZW1
            //

            byte[] input = LzwDecompressor.Decompress(CompressionFormat.SCI1, span, uncompressedSize);
            var stream = new SpanStream(input);

            //
            // Read the compressed view
            //

            // header
            int celDecodedLengthsOffset = stream.ReadUInt16LE() + 2;
            byte loopCount = stream.ReadByte();
            byte loopHeaderCount = stream.ReadByte(); // non-mirrored loops
            UInt16 mirrorMask = stream.ReadUInt16LE();
            UInt16 version = stream.ReadUInt16LE();
            UInt16 paletteOffset = stream.ReadUInt16LE(); // uncompressed offset
            UInt16 celHeaderCount = stream.ReadUInt16LE(); // non-mirrored loops

            // table of cel counts for each loop
            Span celCounts = stream.ReadBytes(loopHeaderCount);

            // table of cel headers (8th byte omitted, zero is used)
            Span celHeaders = stream.ReadBytes(7 * celHeaderCount);

            // palette colors (optional)
            Span paletteColors = null;
            if (paletteOffset != 0)
            {
                paletteColors = stream.ReadBytes(1024);
            }

            // table of all decoded cel lengths
            stream.Seek(celDecodedLengthsOffset); // redundant
            UInt16[] celDecodedLengths = new UInt16[celHeaderCount];
            for (int i = 0; i < celHeaderCount; i++)
            {
                celDecodedLengths[i] = stream.ReadUInt16LE();
            }

            // RLE data for all cels followed by pixel data for all cels.
            // There is no offset that tells us when the pixel data begins.
            // We must parse all of the RLE data to see where it ends.
            int rleDataStartPos = stream.Position;
            for (int i = 0; i < celHeaderCount; i++)
            {
                SkipViewCelRleBytes(stream, celDecodedLengths[i]);
            }
            int rleDataLength = stream.Position - rleDataStartPos;
            var rleData = new SpanStream(stream.Data.Slice(rleDataStartPos, rleDataLength));
            var pixelData = new SpanStream(stream.ReadBytes(stream.Length - stream.Position));

            //
            // Write the uncompressed view
            //

            // view header
            byte[] output = new byte[uncompressedSize];
            output[0] = loopCount;
            output[1] = 0x80; // flags
            WriteUInt16LE(output, 2, mirrorMask);
            WriteUInt16LE(output, 4, version);
            WriteUInt16LE(output, 6, paletteOffset);

            // loop offset table, to be filled in as we write loop headers
            int outputPos = 8 + (loopCount * 2);

            // write non-mirror loops and write all loops to offset table
            UInt16 loopTableIndex = 0;
            UInt16 celTableIndex = 0;
            for (UInt16 loop = 0; loop < loopCount; loop++)
            {
                int loopOffsetPos = 8 + (2 * loop);
                if (((1 << loop) & mirrorMask) == 0)
                {
                    // write loop offset to offset table
                    WriteUInt16LE(output, loopOffsetPos, (UInt16)outputPos);

                    // write loop header, 4 bytes but we leave last 3 as zero
                    byte celCount = celCounts[loopTableIndex];
                    output[outputPos] = celCount; // expands byte to word
                    outputPos += 4;               // unknown field: 00 00

                    // write entire cel offset table, using the cel length table
                    int celOffset = outputPos + (2 * celCount);
                    for (UInt16 cel = 0; cel < celCount; cel++)
                    {
                        WriteUInt16LE(output, outputPos, (UInt16)celOffset);
                        outputPos += 2;
                        celOffset += 8 + celDecodedLengths[celTableIndex + cel];
                    }

                    // write cels
                    for (UInt16 cel = 0; cel < celCount; cel++)
                    {
                        // write cel header, copy 7 from input, the last is zero
                        celHeaders.CopyTo(celTableIndex * 7, output, outputPos, 7);
                        outputPos += 8;

                        // decode cel pixels
                        byte[] celPixels = DecodeViewCel(rleData,
                                                         pixelData,
                                                         celDecodedLengths[celTableIndex]);

                        // write cel pixels
                        Array.Copy(celPixels, 0, output, outputPos, celPixels.Length);
                        outputPos += celDecodedLengths[celTableIndex];

                        celTableIndex++;
                    }
                    loopTableIndex++;
                }
                else
                {
                    // mirror: write previous loop's offset to the offset table
                    output[loopOffsetPos + 0] = output[loopOffsetPos - 2];
                    output[loopOffsetPos + 1] = output[loopOffsetPos - 1];
                }
            }

            // write palette (optional)
            if (paletteOffset != 0)
            {
                // palette header
                output[paletteOffset - 3] = (byte)'P';
                output[paletteOffset - 2] = (byte)'A';
                output[paletteOffset - 1] = (byte)'L';

                // palette translation map (0 to 255)
                for (int i = 0; i < 256; i++)
                {
                    output[paletteOffset + i] = (byte)i;
                }

                // palette timestamp
                // Implement Sierra's decompression bug by writing the cel header
                // bytes that precede palette colors into the palette timestamp.
                // Maintains identical output with ScummVM, SCI Companion, etc.
                // The value doesn't matter to interpreters; they don't use it.
                // Remove this line for a zero timestamp to match SCI Viewer.
                celHeaders.CopyTo(celHeaders.Length - 4, output, paletteOffset + 256, 4);

                // palette colors
                paletteColors.CopyTo(output, paletteOffset + 260, 1024);
            }

            return output;
        }

        public static byte[] DecompressPic(Span span, int uncompressedSize)
        {
            //
            // Decompress using LZW1
            //

            byte[] input = LzwDecompressor.Decompress(CompressionFormat.SCI1, span, uncompressedSize);
            var stream = new SpanStream(input);

            //
            // Read the compressed pic
            //

            UInt16 viewCelDataSize = stream.ReadUInt16LE();
            UInt16 viewStartPos = stream.ReadUInt16LE(); // uncompressed offset
            UInt16 viewPixelDataSize = stream.ReadUInt16LE();
            Span viewHeaderData = stream.ReadBytes(7);
            Span paletteColors = stream.ReadBytes(1024);

            // pic instructions in between SetPalette and EmbeddedView.
            // this data is optional (confirmed in dcomppic disassembly)
            // but I have found no pics without it.
            Span preViewData = null;
            if (viewStartPos != 1286)
            {
                preViewData = stream.ReadBytes(viewStartPos - 1286);
            }

            // pic instructions after EmbeddedView.
            // this data is optional (confirmed in dcomppic disassembly)
            // but all pics must have it or else they wouldn't have the
            // required 0xFF terminator byte.
            Span postViewData = null;
            if (uncompressedSize != viewStartPos + viewCelDataSize + 15)
            {
                postViewData = stream.ReadBytes(uncompressedSize - (viewStartPos + viewCelDataSize + 15));
            }

            var viewPixelData = new SpanStream(stream.ReadBytes(viewPixelDataSize));
            var viewRleData = new SpanStream(stream.ReadBytes(stream.Length - stream.Position));

            //
            // Write the uncompressed pic
            //

            // begin SetPalette instruction
            byte[] output = new byte[uncompressedSize];
            output[0] = 0xfe; // PIC_OP_OPX
            output[1] = 0x02; // PIC_OPX_VGA_SET_PALETTE

            // palette translation map (0 to 255)
            for (int i = 0; i < 256; i++)
            {
                output[2 + i] = (byte)i;
            }

            // palette timestamp: 00 00 00 00, already zero

            // palette colors: 256 4-byte entries, copy from input
            paletteColors.CopyTo(output, 262, 1024);

            // pre-view instructions (optional): copy from input if they exist
            int outputPos = 1286;
            if (preViewData != null)
            {
                preViewData.CopyTo(output, outputPos, preViewData.Length);
                outputPos += preViewData.Length;
            }

            // begin EmbeddedView instruction
            int v = outputPos;
            output[v + 0] = 0xfe; // PIC_OP_OPX
            output[v + 1] = 0x01; // PIC_OPX_VGA_EMBEDDED_VIEW

            // coordinates (0,0): 00 00 00, already zero

            // view size: 8 header bytes + the uncompressed cel data
            UInt16 viewSize = (UInt16)(8 + viewCelDataSize);
            WriteUInt16LE(output, v + 5, viewSize);

            // view header: 8 bytes, copy 7 from input, the last is zero
            viewHeaderData.CopyTo(output, v + 7, 7);

            // view cel: decode RLE and pixel data
            byte[] viewCelData = DecodeViewCel(viewRleData,
                                               viewPixelData,
                                               viewCelDataSize);
            Array.Copy(viewCelData, 0, output, v + 15, viewCelDataSize);
            outputPos += 15 + viewCelDataSize;

            // post-view instructions: copy from input if they exist
            // (again, they must, otherwise there's no 0xFF terminator)
            if (preViewData != null)
            {
                postViewData.CopyTo(output, outputPos, postViewData.Length);
            }

            return output;
        }

        static byte[] DecodeViewCel(SpanStream rle, SpanStream pixel, int uncompressedSize)
        {
            byte[] output = new byte[uncompressedSize];
            int outputPos = 0;

            while (outputPos < uncompressedSize)
            {
                byte b = rle.ReadByte();
                output[outputPos++] = b;
                switch (b & 0xc0)
                {
                    case 0x00:
                    case 0x40:
                        for (int i = 0; i < b; i++)
                        {
                            output[outputPos++] = pixel.ReadByte();
                        }
                        break;
                    case 0x80:
                        output[outputPos++] = pixel.ReadByte();
                        break;
                }
            }
            return output;
        }

        static void SkipViewCelRleBytes(SpanStream rle, int uncompressedSize)
        {
            int outputPos = 0;
            while (outputPos < uncompressedSize)
            {
                byte b = rle.ReadByte();
                outputPos++;
                switch (b & 0xc0)
                {
                    case 0x00:
                    case 0x40:
                        outputPos += b;
                        break;
                    case 0x80:
                        outputPos++;
                        break;
                }
            }
        }

        static void WriteUInt16LE(byte[] buffer, int pos, UInt16 value)
        {
            buffer[pos + 0] = (byte)(value & 0xff);
            buffer[pos + 1] = (byte)(value >> 8);
        }
    }
}
