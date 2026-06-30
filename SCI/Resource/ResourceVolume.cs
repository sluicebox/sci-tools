using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ResourceVolume reads RESOURCE.### / RESSCI.### files.
// Requires RESOURCE.MAP / RESMAP.### to be read first.

namespace SCI.Resource
{
    public enum VolumeVersion
    {
        Unknown,
        SCI0,
        SCI1_Late,
        SCI11,
        SCI2,
        SCI3
    }

    public class VolumeEntry
    {
        public byte Type;
        public UInt16 Number;
        public UInt16 Compression;
        public UInt32 UnpackedSize;
        public UInt32 PackedSize;
        public int DataOffset;

        public override string ToString()
        {
            return string.Format("Type: {0:X2} Number: {1} Compression: {2} Unpacked: {3} Packed: {4} Data: {5}", Type, Number, Compression, UnpackedSize, PackedSize, DataOffset);
        }
    }

    public class ResourceVolume
    {
        public string Name;
        public VolumeVersion Version;
        public byte Number;
        public List<VolumeEntry> Entries;
        public Span Span;

        public override string ToString()
        {
            return string.Format("{0} Version: {1} Number: {2} Entries: {3}", Name, Version, Number, Entries?.Count);
        }

        public static ResourceVolume Read(ResourceMap map, string fileName, byte number = 0)
        {
            var span = new Span(File.ReadAllBytes(fileName));

            if (number == 0)
            {
                if (!byte.TryParse(Path.GetExtension(fileName).Replace(".", ""), out number))
                {
                    number = 0;
                }
            }

            var volume = Read(map, number, span);
            volume.Name = Path.GetFileName(fileName);
            return volume;
        }

        public static ResourceVolume Read(ResourceMap map, byte number, Span span)
        {
            if (map.Entries.All(e => e.VolumeNumber != number))
            {
                throw new Exception("No map entries for volume " + number);
            }

            var volume = new ResourceVolume();
            volume.Version = DetectVersion(map, number, span);
            volume.Number = number;
            volume.Span = span;

            if (volume.Version == VolumeVersion.Unknown)
            {
                throw new Exception("Unknown volume version");
            }

            volume.Entries = new List<VolumeEntry>();
            var stream = new SpanStream(span);
            foreach (var mapEntry in GetMapEntriesByVolume(map, number))
            {
                var volumeEntry = ReadVolumeEntry(volume.Version, mapEntry.VolumeOffset, stream);
                volume.Entries.Add(volumeEntry);
            }

            return volume;
        }

        public static VolumeVersion DetectVersion(ResourceMap map, byte number, Span span)
        {
            var potentialVersions = new List<VolumeVersion>(3);
            if (map.Version == MapVersion.SCI11)
            {
                potentialVersions.Add(VolumeVersion.SCI11);
            }
            else if (map.Version == MapVersion.SCI2)
            {
                potentialVersions.Add(VolumeVersion.SCI2);
                potentialVersions.Add(VolumeVersion.SCI3);
            }
            else
            {
                potentialVersions.Add(VolumeVersion.SCI0);
                potentialVersions.Add(VolumeVersion.SCI1_Late);
                if (map.Version == MapVersion.SCI1_Late)
                {
                    potentialVersions.Add(VolumeVersion.SCI11); // qfg1vga
                }
                potentialVersions.Add(VolumeVersion.SCI2);
            }

            var stream = new SpanStream(span);
            var mapEntriesByVolume = GetMapEntriesByVolume(map, number).ToList();
            foreach (var mapEntry in mapEntriesByVolume)
            {
                for (int i = 0; i < potentialVersions.Count; i++)
                {
                    if (!DoesVolumeVersionMatch(mapEntry, potentialVersions[i], stream, mapEntriesByVolume))
                    {
                        potentialVersions.RemoveAt(i);
                        i--;
                    }
                }

                if (potentialVersions.Count == 1)
                {
                    return potentialVersions[0];
                }
                else if (potentialVersions.Count == 0)
                {
                    Log.Warn("No potential volume versions");
                    return VolumeVersion.Unknown;
                }
            }

            // If the only two potential versions left are SCI2 and SCI3, use SCI2.
            // If compression isn't used then the formats are effectively identical.
            if (potentialVersions.Count == 2 &&
                potentialVersions[0] == VolumeVersion.SCI2 &&
                potentialVersions[1] == VolumeVersion.SCI3)
            {
                return VolumeVersion.SCI2;
            }

            Log.Warn("Too many potential volume versions: " + string.Join(", ", potentialVersions.Select(v => v.ToString())));
            return VolumeVersion.Unknown;
        }

        static IEnumerable<MapEntry> GetMapEntriesByVolume(ResourceMap map, byte number)
        {
            if (map.Version < MapVersion.SCI11)
            {
                return map.Entries.Where(e => e.VolumeNumber == number);
            }
            return map.Entries;
        }

        static bool DoesVolumeVersionMatch(MapEntry mapEntry, VolumeVersion version, SpanStream stream, IReadOnlyList<MapEntry> mapEntriesByVolume)
        {
            // entry must fit within file
            int entrySize = GetVolumeEntrySize(version);
            if ((UInt32)(mapEntry.VolumeOffset + entrySize) > (UInt32)stream.Length)
            {
                return false;
            }

            // read the volume entry as if it were the potential version
            var volumeEntry = ReadVolumeEntry(version, mapEntry.VolumeOffset, stream);

            // type and number must match
            if (!(mapEntry.Type == volumeEntry.Type && mapEntry.Number == volumeEntry.Number))
            {
                return false;
            }

            // packed size must fit within file
            if ((UInt32)(volumeEntry.DataOffset + volumeEntry.PackedSize) > (UInt32)stream.Length)
            {
                return false;
            }

            // packed size must be less than unpacked size if compression is set.
            // * i found one resource that is compressed to a larger size:
            //   sound 72 in qfg1-amiga-1.134. it doesn't affect detection outcome.
            // packed size must equal unpacked size if compression is zero.
            // this doesn't apply to SCI3 where the compression value is not
            // used by the interpreter, and is set even when compression isn't used.
            if (version != VolumeVersion.SCI3)
            {
                if (volumeEntry.PackedSize >= volumeEntry.UnpackedSize && volumeEntry.Compression != 0)
                {
                    return false;
                }
                if (volumeEntry.PackedSize != volumeEntry.UnpackedSize && volumeEntry.Compression == 0)
                {
                    return false;
                }
            }
            else
            {
                // SCI3: packed size must be less than or equal to unpacked size
                if (volumeEntry.PackedSize > volumeEntry.UnpackedSize)
                {
                    return false;
                }
            }

            // hacky test to disambiguate SCI1_Late from SCI11.
            // their only difference is PackedSize. but i don't want to require consecutive records,
            // as that's what allows me to detect sq1vga-rus or any other games with junk bytes in
            // between volume entries, which SSCI doesn't care about. so instead, if we're testing
            // SCI11, and PackedSize doesn't get us to a boundary but adding 4 does, then this is SCI1_Late.
            if (version == VolumeVersion.SCI11)
            {
                // if this entry doesn't end on a boundary but adding 4 to it does,
                // then it's SCI1_Late, so return false to disqualify SCI11.
                var entryEndsInBounds = (volumeEntry.DataOffset + volumeEntry.PackedSize) == stream.Length ||
                                        mapEntriesByVolume.Any(e => e.VolumeOffset == (volumeEntry.DataOffset + volumeEntry.PackedSize));
                if (!entryEndsInBounds)
                {
                    entryEndsInBounds = (volumeEntry.DataOffset + volumeEntry.PackedSize + 4) == stream.Length ||
                                        mapEntriesByVolume.Any(e => e.VolumeOffset == (volumeEntry.DataOffset + volumeEntry.PackedSize + 4));
                    if (entryEndsInBounds)
                    {
                        return false;
                    }
                }
            }

            // validate legal compression values
            if (version == VolumeVersion.SCI0)
            {
                // SCI0's max is 4, but LSCI uses 8
                if (volumeEntry.Compression > 8)
                {
                    return false;
                }
            }
            else if (version < VolumeVersion.SCI2)
            {
                if (volumeEntry.Compression > 20)
                {
                    return false;
                }
            }
            else if (version == VolumeVersion.SCI2)
            {
                // In SCI2 the only two values are 0 or 32
                if (volumeEntry.Compression != 0 &&
                    volumeEntry.Compression != 32)
                {
                    return false;
                }
            }
            else if (version == VolumeVersion.SCI3)
            {
                // In SCI3 the only two values are 0 or 0x94,
                // although the interpreter does not use them,
                // and even uncompressed resources have their
                // compression field set so it's not useful.
                byte sci3Compression = (byte)(volumeEntry.Compression & 0xff);
                if (sci3Compression != 0 &&
                    sci3Compression != 0x94)
                {
                    return false;
                }
            }

            return true;
        }

        static int GetVolumeEntrySize(VolumeVersion version)
        {
            switch (version)
            {
                case VolumeVersion.SCI0:
                    return 8;
                case VolumeVersion.SCI1_Late:
                case VolumeVersion.SCI11:
                    return 9;
                default:
                    return 13;
            }
        }

        static VolumeEntry ReadVolumeEntry(VolumeVersion version, UInt32 offset, SpanStream stream)
        {
            var volumeEntry = new VolumeEntry();
            stream.Seek(offset);

            UInt16 id;
            switch (version)
            {
                case VolumeVersion.SCI0:
                    id = stream.ReadUInt16LE();
                    volumeEntry.Type = (byte)(id >> 11); // high 5
                    volumeEntry.Number = (UInt16)(id & 0x07ff); // low 11
                    if (volumeEntry.Type == 0x1f) // LSCI
                    {
                        // LSCI uses large script numbers for scripts.
                        // This logic is just based on my observations.
                        volumeEntry.Type = 2; // script
                        volumeEntry.Number += 0xe800;
                    }
                    volumeEntry.PackedSize = (UInt16)(stream.ReadUInt16LE() - 4);
                    volumeEntry.UnpackedSize = stream.ReadUInt16LE();
                    volumeEntry.Compression = stream.ReadUInt16LE();
                    break;

                case VolumeVersion.SCI1_Late:
                    volumeEntry.Type = stream.ReadByte();
                    volumeEntry.Number = stream.ReadUInt16LE();
                    volumeEntry.PackedSize = (UInt16)(stream.ReadUInt16LE() - 4);
                    volumeEntry.UnpackedSize = stream.ReadUInt16LE();
                    volumeEntry.Compression = stream.ReadUInt16LE();
                    break;

                case VolumeVersion.SCI11:
                    volumeEntry.Type = stream.ReadByte();
                    volumeEntry.Number = stream.ReadUInt16LE();
                    volumeEntry.PackedSize = stream.ReadUInt16LE();
                    volumeEntry.UnpackedSize = stream.ReadUInt16LE();
                    volumeEntry.Compression = stream.ReadUInt16LE();
                    break;

                // SCI2 vs SCI3:
                // SCI2: compression = 32 for compressed, 0 for uncompressed.
                // SCI3: compression = "i'm afraid our best days are behind us".
                // Both SCI2 and SCI3 use Stacker LZS as their only compression.
                // In SCI3 the compression field is one byte, followed by a
                // checksum byte. At first that sounds good, except that the
                // interpreter no longer cares about the compression value and
                // the tools no longer care about writing anything meaningful.
                // SCI3 instead assumes compression if the packed and unpacked
                // values are different. In practice, the SCI3 compression field
                // is zero or 0x94, but no meaning can be derived from this.
                // For example, LSL7 MAP.65535 and VOCAB.993 are not compressed
                // even though compression is set to 0x94 like all the others.
                case VolumeVersion.SCI2:
                case VolumeVersion.SCI3:
                    volumeEntry.Type = stream.ReadByte();
                    volumeEntry.Number = stream.ReadUInt16LE();
                    volumeEntry.PackedSize = stream.ReadUInt32LE();
                    volumeEntry.UnpackedSize = stream.ReadUInt32LE();
                    volumeEntry.Compression = stream.ReadUInt16LE();
                    break;
            }

            volumeEntry.DataOffset = stream.Position;
            return volumeEntry;
        }
    }
}
