using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ResourceMap reads RESOURCE.MAP / RESMAP.00# files.
// All versions are supported and detected.

namespace SCI.Resource
{
    public enum MapVersion
    {
        Unknown,
        SCI0,
        SCI1_Middle,
        SCI1_Kq5FmTowns,
        SCI1_Late,
        SCI11,
        SCI2
    }

    public class MapEntry
    {
        public byte Type;
        public UInt16 Number;
        public byte VolumeNumber;
        public uint VolumeOffset;

        public override string ToString()
        {
            return string.Format("Type: {0:X2} Number: {1} Volume: {2} Offset: {3}", Type, Number, VolumeNumber, VolumeOffset);
        }
    }

    public class ResourceMap
    {
        public string Name;
        public MapVersion Version;
        public byte Number; // SCI2 multi-disc, otherwise zero
        public List<MapEntry> Entries;

        public override string ToString()
        {
            return string.Format("Version: {0} Number: {1} Entries: {2}", Version, Number, Entries?.Count);
        }

        public static ResourceMap Read(string fileName, byte sci2MapNumber = 0, MapVersion version = MapVersion.Unknown)
        {
            var span = new Span(File.ReadAllBytes(fileName));

            if (sci2MapNumber == 0)
            {
                if (!byte.TryParse(Path.GetExtension(fileName).Replace(".", ""), out sci2MapNumber))
                {
                    sci2MapNumber = 0;
                }
            }

            var map = Read(span, sci2MapNumber, version);
            map.Name = new FileInfo(fileName).Name;
            return map;
        }

        public static ResourceMap Read(Span span, byte sci2MapNumber = 0, MapVersion version = MapVersion.Unknown)
        {
            var resourceMap = new ResourceMap();
            resourceMap.Number = sci2MapNumber;
            if (version == MapVersion.Unknown)
            {
                resourceMap.Version = DetectVersion(span);
            }
            else
            {
                resourceMap.Version = version;
            }

            if (resourceMap.Version != MapVersion.SCI2 && resourceMap.Number != 0)
            {
                throw new Exception("SCI2 map number provided but version is: " + resourceMap.Version);
            }

            switch (resourceMap.Version)
            {
                case MapVersion.SCI0:
                case MapVersion.SCI1_Middle:
                case MapVersion.SCI1_Kq5FmTowns:
                    resourceMap.Entries = ReadResourceMap0(resourceMap.Version, span);
                    break;
                case MapVersion.SCI1_Late:
                case MapVersion.SCI11:
                case MapVersion.SCI2:
                    resourceMap.Entries = ReadResourceMap1(span, resourceMap.Version, resourceMap.Number);
                    break;
                default:
                    throw new Exception("Unknown map version");
            }

            return resourceMap;
        }

        public static MapVersion DetectVersion(Span span)
        {
            if (span.Length < 7) throw new Exception("Map file is too short");

            // SCI0:         6 byte entries terminated by 6 0xFF bytes
            // SCI1 Middle:  same but volume number and offset changed size
            // KQ5 FM-TOWNS: 7 byte entries

            // KQ5 FM-TOWNS
            if (span.Length % 7 == 0 && span.Skip(span.Length - 7).All(b => b == 0xff))
            {
                return MapVersion.SCI1_Kq5FmTowns;
            }

            // SCI0 / SCI1 Middle
            if (span.Length % 6 == 0 && span.Skip(span.Length - 6).All(b => b == 0xff))
            {
                // u16  id: same in both versions
                // u32  volume/offset: 6/26 => 4/28
                bool topSixBitsSet = false;
                var entryCount = (span.Length / 6) - 1;
                for (int i = 0; i < entryCount; ++i)
                {
                    UInt32 volume = span.GetUInt32LE(i * 6 + 2);
                    if (volume == 0x04000000 || // SCI0 volume 1
                        volume == 0x08000000 || // SCI0 volume 2
                        volume == 0x0c000000)   // SCI0 volume 3
                    {
                        return MapVersion.SCI0;
                    }

                    if (volume >= 0x04000000)
                    {
                        topSixBitsSet = true;
                    }
                }
                return topSixBitsSet ?
                       MapVersion.SCI1_Middle :
                       MapVersion.SCI0;
            }

            // SCI1 Late / SCI11 / SCI2: "Let's stop repeating resource type"
            //
            // The map is now two sections:
            //
            // Header records: six bytes each, one per resource type.
            //    u8  resource type (high bit always set unless SCI2)
            //    u16 offset to start of directory records for that type
            //    Final header record has 0xff resource type and file length for offset,
            //    except for shiver2, it has a different smaller value.
            //    SCI3 then has a 32-bit version number afterwards, default 0x00001234.
            //
            // Directory records:
            //    SCI1 Late: 6 bytes
            //      u16 resource number
            //      u32 volume/offset 4/28
            //    SCI1.1:  5 bytes, no volume number
            //      u16 resource number
            //      u24 offset divided by two
            //    SCI2: 6 bytes, no volume number, more offset
            //      u16 resource number
            //      u32 offset
            //
            // This is basically ScummVM's detection loop.
            // It doesn't touch the directory records, instead it parses every
            // header record and uses the type to detect SCI2 and the directory
            // size to determine if SCI1 Late or SCI1.1. Any contradictions
            // result in an exception.
            //
            // SCI Companion's detection looks really complicated, and only
            // operates on the first seven directory records.

            var detectedVersion = MapVersion.Unknown;
            for (int i = 0; i + 3 <= span.Length; i += 3)
            {
                byte directoryType = span[i];
                UInt16 directoryOffset = span.GetUInt16LE(i + 1);

                // check for terminator
                if (directoryType == 0xff)
                {
                    if (directoryOffset == span.Length)
                    {
                        if (detectedVersion == MapVersion.Unknown)
                        {
                            throw new Exception("Terminator reached without figuring it out!");
                        }
                        return detectedVersion;
                    }
                    // the two bytes after the terminator don't match the span length,
                    // but these bytes apparently became meaningless, so to be confident
                    // we've reached the terminator, look for the u32 map version afterwards.
                    // this was used in shivers (sci3) and although it *could* be anything,
                    // in the demo it's just the default version 00001234.
                    if (i + 7 < span.Length && span.GetUInt32LE(i + 3) == 0x00001234)
                    {
                        return MapVersion.SCI2;
                    }
                    else
                    {
                        throw new Exception("Invalid terminator");
                    }
                }

                // only SCI2 has the high bit clear.
                // this is the scummvm check i re-worked to be more readable,
                // but i don't fully understand it. SCI Companion may be doing
                // a more accurate range check.
                if (directoryType < 0x80 && detectedVersion == MapVersion.Unknown)
                {
                    detectedVersion = MapVersion.SCI2;
                }
                else if (detectedVersion != MapVersion.SCI2)
                {
                    if (directoryType < 0x80 || (directoryType & 0x7f) > 0x20)
                    {
                        throw new Exception("Invalid directory type");
                    }
                }

                // validate directory offset
                if (directoryOffset > span.Length) throw new Exception("Invalid directory offset");

                if (i + 6 > span.Length) throw new Exception("No terminator");

                UInt16 nextDirectoryOffset = span.GetUInt16(i + 4);
                if (nextDirectoryOffset <= directoryOffset) throw new Exception("Invalid next directory offset");

                UInt16 directorySize = (UInt16)(nextDirectoryOffset - directoryOffset);
                if (directorySize % 6 == 0 && directorySize % 5 != 0)
                {
                    // could be SCI1 Late or SCI2
                    if (detectedVersion == MapVersion.Unknown)
                    {
                        detectedVersion = MapVersion.SCI1_Late;
                    }
                    else if (detectedVersion != MapVersion.SCI1_Late &&
                             detectedVersion != MapVersion.SCI2)
                    {
                        throw new Exception("Conflicting directory size");
                    }
                }
                else if (directorySize % 5 == 0 && directorySize % 6 != 0)
                {
                    // could only be SCI1.1
                    if (detectedVersion == MapVersion.Unknown)
                    {
                        detectedVersion = MapVersion.SCI11;
                    }
                    else if (detectedVersion != MapVersion.SCI11)
                    {
                        throw new Exception("Conflicting directory size");
                    }
                }
            }

            throw new Exception("I don't know!");
        }

        static List<MapEntry> ReadResourceMap0(MapVersion version, Span span)
        {
            // SCI0:
            //   u16  resource type/number 5/11
            //   u32  volume number/offset 6/26
            // SCI1 Middle:
            //   u16  resource type/number 5/11
            //   u32  volume number/offset 4/28
            // KQ5 FM-TOWNS:
            //   u8   resource type
            //   u16  resource number
            //   u32  volume number/offset 4/28

            int entrySize = (version == MapVersion.SCI1_Kq5FmTowns) ? 7 : 6;
            int entryCount = span.Length / entrySize;
            var entries = new List<MapEntry>(entryCount);
            var stream = new SpanStream(span);
            for (int i = 0; i < entryCount - 1; ++i)
            {
                var entry = new MapEntry();
                UInt16 id;
                UInt32 volume;
                switch (version)
                {
                    case MapVersion.SCI0:
                        id = stream.ReadUInt16LE();
                        volume = stream.ReadUInt32LE();
                        entry.Type = (byte)(id >> 11); // high 5
                        entry.Number = (UInt16)(id & 0x07ff); // low 11
                        if (entry.Type == 0x1f) // LSCI
                        {
                            // LSCI uses large script numbers for scripts.
                            // This logic is just based on my observations.
                            entry.Type = 2; // script
                            entry.Number += 0xe800;
                        }
                        entry.VolumeNumber = (byte)(volume >> 26); // high 6
                        entry.VolumeOffset = volume & 0x03ffffff; // low 26
                        break;

                    case MapVersion.SCI1_Middle:
                        id = stream.ReadUInt16LE();
                        volume = stream.ReadUInt32LE();
                        entry.Type = (byte)(id >> 11); // high 5
                        entry.Number = (UInt16)(id & 0x07ff); // low 11
                        entry.VolumeNumber = (byte)(volume >> 28); // high 4
                        entry.VolumeOffset = volume & 0x0fffffff; // low 28
                        break;

                    case MapVersion.SCI1_Kq5FmTowns:
                        entry.Type = stream.ReadByte();
                        entry.Number = stream.ReadUInt16LE();
                        volume = stream.ReadUInt32LE();
                        entry.VolumeNumber = (byte)(volume >> 28); // high 4
                        entry.VolumeOffset = volume & 0x0fffffff; // low 28
                        break;
                }
                entries.Add(entry);
            }
            return entries;
        }

        static List<MapEntry> ReadResourceMap1(Span span, MapVersion version, byte sci2MapNumber)
        {
            var entries = new List<MapEntry>();
            for (int i = 0; span[i] != 0xff; i += 3)
            {
                byte type = span[i];
                UInt16 directoryOffset = span.GetUInt16LE(i + 1);
                UInt16 nextDirectoryOffset = span.GetUInt16LE(i + 4);
                int directorySize = nextDirectoryOffset - directoryOffset;
                int directoryEntrySize = (version == MapVersion.SCI11) ? 5 : 6;
                //int directoryEntryCount = directorySize / directoryEntrySize;
                for (int j = directoryOffset; j < nextDirectoryOffset; j += directoryEntrySize)
                {
                    var entry = new MapEntry();
                    entry.Type = type;
                    entry.Number = span.GetUInt16LE(j);
                    UInt32 volume;
                    switch (version)
                    {
                        case MapVersion.SCI1_Late:
                            volume = span.GetUInt32LE(j + 2);
                            entry.VolumeNumber = (byte)(volume >> 28); // high 4
                            entry.VolumeOffset = volume & 0x0fffffff; // low 28
                            break;

                        case MapVersion.SCI11:
                            volume = span.GetUInt16LE(j + 2);
                            volume |= (UInt32)(span[j + 4] << 16);
                            volume *= 2;
                            entry.VolumeNumber = 0; // SCI1.1 has one giant volume 0
                            entry.VolumeOffset = volume;
                            break;

                        case MapVersion.SCI2:
                            entry.VolumeNumber = sci2MapNumber;
                            entry.VolumeOffset = span.GetUInt32LE(j + 2);
                            break;
                    }
                    entries.Add(entry);
                }
            }
            return entries;
        }
    }
}
