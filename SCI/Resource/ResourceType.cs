using System;

namespace SCI.Resource
{
    public enum ResourceType
    {
        View,
        Pic,
        Script,
        Text,
        Sound,
        Memory,
        Vocab,
        Font,
        Cursor,
        Patch,
        Bitmap,
        Palette,
        CdAudio,
        Wave,
        Audio,
        Sync,
        Message,
        Map,
        Heap,
        Audio36,
        Sync36,
        Translation,
        Rave,  // KQ6 hires portraits

        // SCI2.1
        Robot,
        VMD,
        Chunk,
        Animation,

        // SCI3
        Etc,
        Duck,
        Clut,
        TGA,
        ZZZ
    }

    public enum ResourceTypeVersion
    {
        Unknown, // Default behavior is SCI0
        SCI0,
        SCI21
    }

    public static class ResourceTypeMap
    {
        public static ResourceType[] Sci0 = {
            /* 00  0 */ ResourceType.View,
            /* 01  1 */ ResourceType.Pic,
            /* 02  2 */ ResourceType.Script,
            /* 03  3 */ ResourceType.Text,
            /* 04  4 */ ResourceType.Sound,
            /* 05  5 */ ResourceType.Memory,
            /* 06  6 */ ResourceType.Vocab,
            /* 07  7 */ ResourceType.Font,
            /* 08  8 */ ResourceType.Cursor,
            /* 09  9 */ ResourceType.Patch,
            /* 0a 10 */ ResourceType.Bitmap,
            /* 0b 11 */ ResourceType.Palette,
            /* 0c 12 */ ResourceType.CdAudio,
            /* 0d 13 */ ResourceType.Audio,
            /* 0e 14 */ ResourceType.Sync,
            /* 0f 15 */ ResourceType.Message,
            /* 10 16 */ ResourceType.Map,
            /* 11 17 */ ResourceType.Heap,
            /* 12 18 */ ResourceType.Audio36,
            /* 13 19 */ ResourceType.Sync36,
            /* 14 20 */ ResourceType.Translation,
            /* 15 21 */ ResourceType.Rave
        };

        public static ResourceType[] Sci21 = {
            /* 00  0 */ ResourceType.View,
            /* 01  1 */ ResourceType.Pic,
            /* 02  2 */ ResourceType.Script,
            /* 03  3 */ ResourceType.Animation,
            /* 04  4 */ ResourceType.Sound,
            /* 05  5 */ ResourceType.Etc, // SCI3, unused in SCI21
            /* 06  6 */ ResourceType.Vocab,
            /* 07  7 */ ResourceType.Font,
            /* 08  8 */ ResourceType.Cursor,
            /* 09  9 */ ResourceType.Patch,
            /* 0a 10 */ ResourceType.Bitmap,
            /* 0b 11 */ ResourceType.Palette,
            /* 0c 12 */ ResourceType.Wave,
            /* 0d 13 */ ResourceType.Audio,
            /* 0e 14 */ ResourceType.Sync,
            /* 0f 15 */ ResourceType.Message,
            /* 10 16 */ ResourceType.Map,
            /* 11 17 */ ResourceType.Heap, // SCI21, unused in SCI3
            /* 12 18 */ ResourceType.Chunk,
            /* 13 19 */ ResourceType.Audio36,
            /* 14 20 */ ResourceType.Sync36,
            /* 15 21 */ ResourceType.Translation,
            /* 16 22 */ ResourceType.Robot,
            /* 17 23 */ ResourceType.VMD,
            /* 18 24 */ ResourceType.Duck,
            /* 19 25 */ ResourceType.Clut,
            /* 1a 26 */ ResourceType.TGA,
            /* 1b 27 */ ResourceType.ZZZ
        };

        public static ResourceType GetType(byte value, ResourceTypeVersion version)
        {
            ResourceType[] map = (version == ResourceTypeVersion.SCI21) ? Sci21 : Sci0;
            return map[value & 0x7f];
        }

        public static byte GetValue(ResourceType type, ResourceTypeVersion version)
        {
            ResourceType[] map = (version == ResourceTypeVersion.SCI21) ? Sci21 : Sci0;
            for (int i = 0; i < map.Length; i++)
            {
                if (type == map[i])
                {
                    return (byte)i;
                }
            }

            throw new Exception("Resource type not found in map: " + type + " for version: " + version);
        }
    }
}
