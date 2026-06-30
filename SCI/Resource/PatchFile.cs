using System;
using System.IO;

namespace SCI.Resource
{
    public enum PatchFileVersion
    {
        Unknown,
        SCI0,
        SCI11,
        SCI2,
    }

    public static class PatchFile
    {
        public static Span Read(string fileName, ResourceType type, PatchFileVersion patchFileVersion)
        {
            int headerSize;
            int lengthPosition;
            GetPatchFileHeaderInfo(type, patchFileVersion, out headerSize, out lengthPosition);

            var span = new Span(fileName);
            if (headerSize == 0)
            {
                return span;
            }

            headerSize += span[lengthPosition];
            return span.Slice(headerSize);
        }

        public static void Write(string fileName, Span resource, ResourceType type, 
                                 PatchFileVersion patchFileVersion,
                                 ResourceTypeVersion resourceTypeVersion)
        {
            int headerSize;
            int lengthPosition;
            GetPatchFileHeaderInfo(type, patchFileVersion, out headerSize, out lengthPosition);

            byte[] patch;
            if (headerSize == 0)
            {
                patch = resource.ToArray();
            }
            else
            {
                patch = new byte[headerSize + resource.Length];
                patch[0] = (byte)(0x80 | ResourceTypeMap.GetValue(type, resourceTypeVersion));
                resource.CopyTo(patch, headerSize, resource.Length);
            }

            File.WriteAllBytes(fileName, patch);
        }

        static void GetPatchFileHeaderInfo(ResourceType type,
                                           PatchFileVersion version,
                                           out int headerSize,
                                           out int lengthPosition)
        {
            if (version <= PatchFileVersion.SCI0) // includes Unknown
            {
                headerSize = 2;
                lengthPosition = 1;
            }
            else  if (version == PatchFileVersion.SCI11)
            {
                switch (type)
                {
                    case ResourceType.View:
                    case ResourceType.Pic:
                        headerSize = 26;
                        lengthPosition = 3;
                        break;
                    case ResourceType.Palette:
                        headerSize = 4;
                        lengthPosition = 3;
                        break;
                    default:
                        headerSize = 2;
                        lengthPosition = 1;
                        break;
                }
            }
            else // SCI2
            {
                // wav/audio/audio36 have no headers

                switch (type)
                {
                    case ResourceType.View:
                        headerSize = 26;
                        lengthPosition = 3;
                        break;
                    case ResourceType.Pic:
                    case ResourceType.Palette:
                        headerSize = 4;
                        lengthPosition = 3;
                        break;
                    case ResourceType.Wave:
                    case ResourceType.Audio:
                    case ResourceType.Audio36:
                        headerSize = 0;
                        lengthPosition = 0;
                        break;
                    default:
                        headerSize = 2;
                        lengthPosition = 1;
                        break;
                }
            }
        }
    }
}
