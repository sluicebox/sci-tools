using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SCI.Resource
{
    public enum PatchFileNameFormat
    {
        Unknown,
        SCI0, // script.XXX
        SCI1, // X.SCR, X.HEP
        SCI3  // X.CSC (everything else is the same as SCI1)
    }

    public static class PatchFileNames
    {
        public static IReadOnlyDictionary<string, ResourceType> Names = new Dictionary<string, ResourceType>
        {
            { "view", ResourceType.View },
            { "pic", ResourceType.Pic },
            { "script", ResourceType.Script },
            { "text", ResourceType.Text },
            { "sound", ResourceType.Sound },
            { "memory", ResourceType.Memory },
            { "vocab", ResourceType.Vocab },
            { "font", ResourceType.Font },
            { "cursor", ResourceType.Cursor },
            { "patch", ResourceType.Patch },
            { "bitmap", ResourceType.Bitmap },
            { "palette", ResourceType.Palette },
            { "cdaudio", ResourceType.CdAudio },
            { "audio", ResourceType.Audio },
            { "sync", ResourceType.Sync },
            { "message", ResourceType.Message },
            { "map", ResourceType.Map },
            { "heap", ResourceType.Heap }, // pretty sure i made this up
        };

        public static IReadOnlyDictionary<string, ResourceType> Suffixes = new Dictionary<string, ResourceType>
        {
            { "v56", ResourceType.View },
            { "p56", ResourceType.Pic },
            { "scr", ResourceType.Script },
            { "so",  ResourceType.Script }, // LSCI
            { "csc", ResourceType.Script }, // SCI3
            { "tex", ResourceType.Text },
            { "to",  ResourceType.Text },   // LSCI
            { "snd", ResourceType.Sound },
            { "voc", ResourceType.Vocab },
            { "fon", ResourceType.Font },
            { "cur", ResourceType.Cursor },
            { "pat", ResourceType.Patch },
            { "bit", ResourceType.Bitmap },
            { "pal", ResourceType.Palette },
            { "cda", ResourceType.CdAudio },
            { "aud", ResourceType.Audio },
            { "syn", ResourceType.Sync36 },
            { "msg", ResourceType.Message },
            { "map", ResourceType.Map },
            { "hep", ResourceType.Heap },
            { "chk", ResourceType.Chunk },
        };

        public static ResourceId Parse(string fileName)
        {
            PatchFileNameFormat format;
            return Parse(fileName, out format);
        }

        public static ResourceId Parse(string fileName, out PatchFileNameFormat format)
        {
            format = PatchFileNameFormat.Unknown;
            fileName = Path.GetFileName(fileName);
            int extensionPos = fileName.LastIndexOf('.');
            if (extensionPos == -1) return null;

            string prefix = fileName.Substring(0, extensionPos).ToLower();
            string suffix = fileName.Substring(extensionPos + 1).ToLower();
            ResourceType type;
            UInt16 number;
            if (Names.TryGetValue(prefix, out type) && UInt16.TryParse(suffix, out number))
            {
                format = PatchFileNameFormat.SCI0;
                return new ResourceId(type, number);
            }
            if (Suffixes.TryGetValue(suffix, out type) && UInt16.TryParse(prefix, out number))
            {
                if (suffix == "csc")
                {
                    format = PatchFileNameFormat.SCI3;
                }
                else
                {
                    format = PatchFileNameFormat.SCI1;
                }
                return new ResourceId(type, number);
            }
            return null;
        }

        public static string GetName(ResourceId id, PatchFileNameFormat format)
        {
            if (format == PatchFileNameFormat.SCI0)
            {
                return string.Format("{0}.{1:000}", Names.First(kv => kv.Value == id.Type).Key, id.Number);
            }

            string suffix = Suffixes.First(kv => kv.Value == id.Type).Key;
            if (id.Type == ResourceType.Script)
            {
                if (format == PatchFileNameFormat.SCI1)
                {
                    suffix = "scr";
                }
                else
                {
                    suffix = "csc";
                }
            }
            return string.Format("{0}.{1}", id.Number, suffix);
        }
    }
}
