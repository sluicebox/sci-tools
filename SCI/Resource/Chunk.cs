using System;
using System.Collections.Generic;

// Chunks are resources that contain other resources. They're rarely used and
// their purpose is unclear, although it was probably some kind of optimization.
// I haven't  found any comments in sierra source as to why chunks exist. You
// can get pretty far by ignoring them, and that's what I recommend.
//
// There is a LoadChunk kernel call, but no games call it. Sierra's interpreter
// somehow automatically uses them. For example, dumping 65535.MAP to a patch
// file crashes KQ7 2.00 DOS because a copy of 65535.MAP also exists in 0.CHK.
//
// Chunks only appear in KQ7, Lighthouse, and an early Lighthouse SCI21 demo.
// In KQ7 and Lighthouse (non-demo), every resource in every chunk is identical
// to the version in the normal resource volumes. KQ7 has just one chunk in the
// patch file 0.CHK. You can delete it and KQ7 still runs, and some localized
// versions don't even include it. ScummVM doesn't use it. Lighthouse has many
// chunks in its resource volumes. Lighthouse's chunks are in a newer format
// that ScummVM doesn't even have a parser for. Again, they're all duplicates.
//
// That leaves the Lighthouse SCI21 demo, where all the scripts and heaps are
// only in chunk 0 within the resource volume. This is the one game that ScummVM
// has to use chunks for. Now my ResourceManager does too. It's just a demo that
// plays a single VMD movie and quits.
//
// You really don't care about chunks unless you're doing something with KQ7
// that could conflict with a resource in 0.CHK. Audio maps cause a crash and
// there may be other conflicts. I assume that's why 0.CHK wasn't included in
// the French, Spanish, and Italian versions. I was helping someone port German
// resources from 1.51 to English 2.00 so they could run KQ7 German in DOS.
// I worked around the conflict by removing audio maps and fonts from 0.CHK.

namespace SCI.Resource
{
    public enum ChunkVersion
    {
        Unknown,
        SCI2, // KQ7, Lighthouse SCI21 demo
        SCI3  // Lighthouse
    }

    public class ChunkEntry
    {
        public ResourceId Id;
        public int DataOffset;
        public int UnpackedSize;
        public int PackedSize;
        public byte Compression;
        public byte Checksum;

        public override string ToString()
        {
            return string.Format("{0} Unpacked: {1} Packed: {2} Data: {3}", Id, UnpackedSize, PackedSize, DataOffset);
        }
    }

    public class Chunk
    {
        public ChunkVersion Verison;
        public UInt16 Number;
        public Span Span;
        public List<ChunkEntry> Entries;

        public static Chunk Read(UInt16 number, Span span, ChunkVersion version = ChunkVersion.Unknown)
        {
            if (version == ChunkVersion.Unknown)
            {
                version = DetectVersion(span);
            }

            if (version == ChunkVersion.SCI2)
            {
                return ReadChunk2(number, span);
            }
            else
            {
                return ReadChunk3(number, span);
            }
        }

        static Chunk ReadChunk2(UInt16 chunkNumber, Span span)
        {
            var chunk = new Chunk();
            chunk.Verison = ChunkVersion.SCI2;
            chunk.Number = chunkNumber;
            chunk.Span = span;
            chunk.Entries = new List<ChunkEntry>();

            // SCI2 chunk format:
            // 11 byte headers containing resource id, offset, and length.
            // The first entry's offset identifies the end of the headers.

            int firstOffset = span.GetInt32(3);
            int resourceCount = (firstOffset / 11);
            var stream = new SpanStream(span);
            for (int i = 0; i < resourceCount; i++)
            {
                var entry = new ChunkEntry();
                chunk.Entries.Add(entry);

                byte type = stream.ReadByte();
                UInt16 number = stream.ReadUInt16();
                entry.DataOffset = stream.ReadInt32();
                entry.UnpackedSize = stream.ReadInt32();
                entry.PackedSize = entry.UnpackedSize; // no compression in SCI2 chunks

                var resType = ResourceTypeMap.GetType(type, ResourceTypeVersion.SCI21);
                entry.Id = new ResourceId(resType, number);
            }

            return chunk;
        }

        static Chunk ReadChunk3(UInt16 chunkNumber, Span span)
        {
            var chunk = new Chunk();
            chunk.Verison = ChunkVersion.SCI3;
            chunk.Number = chunkNumber;
            chunk.Span = span;
            chunk.Entries = new List<ChunkEntry>();

            // SCI3 chunk format:
            // Volume entry  - 13 bytes, same format as in resource volumes.
            // Resource data - possibly compressed, according to entry.
            // Next volume entry...

            var stream = new SpanStream(span);
            while (!stream.EOF)
            {
                var entry = new ChunkEntry();
                chunk.Entries.Add(entry);

                byte type = stream.ReadByte();
                UInt16 number = stream.ReadUInt16();
                entry.PackedSize = stream.ReadInt32();
                entry.UnpackedSize = stream.ReadInt32();
                entry.Compression = stream.ReadByte();
                entry.Checksum = stream.ReadByte();

                var resType = ResourceTypeMap.GetType(type, ResourceTypeVersion.SCI21);
                entry.Id = new ResourceId(resType, number);

                entry.DataOffset = stream.Position;
                stream.Skip(entry.PackedSize);
            }

            return chunk;
        }

        public static ChunkVersion DetectVersion(Span span)
        {
            if (IsChunk2Valid(span))
            {
                return ChunkVersion.SCI2;
            }
            else
            {
                return ChunkVersion.SCI3;
            }
        }

        static bool IsChunk2Valid(Span span)
        {
            // SCI2: starts with 11 byte headers, followed immediately
            // by the data in the first header. If headers can be parsed
            // as SCI2, then assume SC2, otherwise SCI3.
            int firstOffset = span.GetInt32(3);
            if (firstOffset <= 0 || firstOffset % 11 != 0) return false;

            int resourceCount = (firstOffset / 11);
            UInt32 dataPosition = (UInt32)firstOffset;
            resourceCount = Math.Min(5, resourceCount); // only need to see a few
            for (int i = 0; i < resourceCount; i++)
            {
                // validate type
                byte type = span[i * 11];
                if ((type & 0x7f) >= ResourceTypeMap.Sci21.Length)
                {
                    return false;
                }

                // validate offset
                UInt32 offset = span.GetUInt32(i * 11 + 3);
                if (offset != dataPosition)
                {
                    return false;
                }

                UInt32 length = span.GetUInt32(i * 11 + 7);
                dataPosition += length;
                if (dataPosition > span.Length)
                {
                    return false;
                }
            }

            return true;
        }
    }
}