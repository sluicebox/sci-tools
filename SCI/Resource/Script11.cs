using System;
using System.Collections.Generic;
using System.Linq;

// Script11: Second generation of the Script resource format.
//
// - Separate heap and hunk resources: "Heap" and "Script".
// - Complicated! At least, to me. It's really one resource split
//   into two co-dependent formats designed to minimize heap usage.
// - Little endian on PC, big endian on Mac.
// - No format variations; only the bytecode changes.
// - Bytecode could be either SCI0_11 or SCI2.
// - All bytecode is in one buffer instead of blocks.
// - No Saids or Synonyms; these are post-parser games.
// - Detecting strings is a little fuzzy.
//
// * This format is also used in LSCI for Imagination Network (INN)
//   and The Realm; both are network clients.
//   I have only detected one difference in LSCI script resources:
//   the offsets to object dictionaries are sometimes set to zero.
//   Strangely though, not always. I have no idea why you'd do that.
//   Without those offsets, I just read from the current position,
//   which makes me wonder if that would have always worked and the
//   offsets' initial values never mattered.

namespace SCI.Resource
{
    public class Script11
    {
        public int Number;
        public Span Heap; // HEP
        public Span Hunk; // SCR

        public UInt16 HeapRelocationPosition;
        public List<Relocation16> HeapRelocations;
        public UInt16 HunkRelocationPosition;
        public List<Relocation16> HunkRelocations;

        public List<Local>    Locals;    // heap
        public List<Export>   Exports;   // hunk
        public List<Object11> Objects;   // both
        public List<String>   Strings;   // heap

        public UInt16 ByteCodePosition;  // hunk
        public UInt16 ByteCodeLength;    // hunk

        public UInt16 ScriptNodeHeapAddress; // hunk
        public bool HasFarText;              // hunk

        public override string ToString()
        {
            string description = "SCI11 Script " + Number;
            foreach (var export in Exports.Where(e => e.Type == ExportType.Object))
            {
                var obj = Objects.FirstOrDefault(o => o.Position == export.Value);
                if (obj != null)
                {
                    return description + " - " + obj.Name;
                }
            }
            foreach (var cls in Objects.Where(o => o.IsClass))
            {
                return description + " - " + cls.Name;
            }
            return description;
        }

        public Script11(int number, Span heap, Span hunk)
        {
            Number = number;
            Heap = heap;
            Hunk = hunk;

            var heapStream = new SpanStream(heap);
            var hunkStream = new SpanStream(hunk);

            Initialize(heapStream, hunkStream);
        }

        void Initialize(SpanStream heap, SpanStream hunk)
        {
            //
            // start with the heap
            //

            HeapRelocationPosition = heap.ReadUInt16();

            // locals
            UInt16 localCount = heap.ReadUInt16();
            Locals = new List<Local>(localCount);
            for (UInt16 i = 0; i < localCount; ++i)
            {
                var local = new Local();
                local.Position = heap.Position16;
                local.Value = heap.ReadUInt16();
                Locals.Add(local);
            }

            // heap relocations (detour to end of file)
            // need these for detecting string properties when parsing objects
            int objectsHeapOffset = heap.Position;
            heap.Seek(HeapRelocationPosition);
            UInt16 heapRelocationCount = heap.ReadUInt16();
            HeapRelocations = new List<Relocation16>(heapRelocationCount);
            for (UInt16 i = 0; i < heapRelocationCount; ++i)
            {
                var relocation = new Relocation16();
                relocation.Position = heap.Position16;
                relocation.Value = heap.ReadUInt16();
                HeapRelocations.Add(relocation);
            }
            heap.Seek(objectsHeapOffset);

            //
            // switch to the hunk
            //

            HunkRelocationPosition = hunk.ReadUInt16();
            ScriptNodeHeapAddress = hunk.ReadUInt16();
            HasFarText = hunk.ReadUInt16() != 0;

            // hunk relocations (detour to end of file)
            // need these to identify if an export is an object or a procedure
            int exportHunkOffset = hunk.Position;
            hunk.Seek(HunkRelocationPosition);
            UInt16 hunkRelocationCount = hunk.ReadUInt16();
            HunkRelocations = new List<Relocation16>(hunkRelocationCount);
            for (UInt16 i = 0; i < hunkRelocationCount; ++i)
            {
                var relocation = new Relocation16();
                relocation.Position = hunk.Position16;
                relocation.Value = hunk.ReadUInt16();
                HunkRelocations.Add(relocation);
            }
            hunk.Seek(exportHunkOffset);

            // exports
            // if an export has a relocation then i assume it's an object
            // otherwise i assume it's code. later, i validate that all
            // code exports point within the byte buffer.
            // note that zero is an insane special case that gets treated
            // as the start of the bytecode buffer, so zero is legal, even
            // though it can also be bogus. later i validate this too.
            UInt16 exportCount = hunk.ReadUInt16();
            Exports = new List<Export>(exportCount);
            for (UInt16 i = 0; i < exportCount; ++i)
            {
                var export = new Export();
                export.Position = hunk.Position16;
                export.Value = hunk.ReadUInt16();
                if (HunkRelocations.Any(r => r.Value == export.Position))
                {
                    export.Type = ExportType.Object;
                }
                else
                {
                    export.Type = ExportType.Code;
                }
                Exports.Add(export);
            }

            //
            // objects: heap and hunk
            //

            // loop through the heap until magic number isn't 0x1234.
            // there should be a terminating 0x0000 to make this work.
            Objects = new List<Object11>();
            while (true)
            {
                UInt16 objectPosition = heap.Position16;

                UInt16 blockType = heap.ReadUInt16();
                if (blockType != 0x1234)
                {
                    break;
                }

                var obj = new Object11();
                obj.Position = objectPosition;

                // properties. we'll identify strings later once we've parsed them.
                UInt16 propertyCount = heap.ReadUInt16();
                obj.Properties = new List<Property16>(propertyCount);
                // name property is optional
                if (propertyCount < 8) throw new Exception("Not enough properties: " + propertyCount);
                for (UInt16 i = 0; i < propertyCount; ++i)
                {
                    var property = new Property16();
                    if (i == (UInt16)PropertyIndex11.MagicNumber)
                    {
                        property.Position = objectPosition;
                        property.Value = blockType; // 0x1234
                    }
                    else if (i == (UInt16)PropertyIndex11.PropertyCount)
                    {
                        property.Position = (UInt16)(objectPosition + 2);
                        property.Value = propertyCount;
                    }
                    else
                    {
                        property.Position = heap.Position16;
                        property.Value = heap.ReadUInt16();
                    }
                    obj.Properties.Add(property);
                }

                obj.IsClass = (obj.Properties[(int)PropertyIndex11.Info].Value & 0x8000) != 0;

                // class property selectors come from PropDict
                if (obj.IsClass)
                {
                    obj.ClassPropertySelectors = new List<UInt16>(propertyCount);
                    UInt16 propDictHunkOffset = obj.Properties[(int)PropertyIndex11.PropDictHunkOffset].Value;
                    // LSCI sets PropDict to zero (but not always).
                    // In that case, just don't seek, because we're already pointed at the dictionary.
                    if (propDictHunkOffset != 0)
                    {
                        hunk.Seek(propDictHunkOffset);
                    }
                    for (UInt16 i = 0; i < propertyCount; ++i)
                    {
                        obj.ClassPropertySelectors.Add(hunk.ReadUInt16());
                    }
                }

                // methods aka MethDict
                UInt16 methDictHunkOffset = obj.Properties[(int)PropertyIndex11.MethDictHunkOffset].Value;
                // LSCI sets MethDict to zero (but not always).
                // In that case, just don't seek, because we're already pointed at the dictionary.
                if (methDictHunkOffset != 0)
                {
                    hunk.Seek(methDictHunkOffset);
                }
                UInt16 methodCount = hunk.ReadUInt16();
                obj.Methods = new List<Method11>(methodCount);
                for (UInt16 i = 0; i < methodCount; ++i)
                {
                    var method = new Method11();
                    method.Position = hunk.Position16;
                    method.Selector = hunk.ReadUInt16();
                    method.Code = hunk.ReadUInt16();
                    obj.Methods.Add(method);
                }

                Objects.Add(obj);
            }

            //
            // back to heap (for the last time)
            //

            // strings. just parse until HeapRelocationOffset reached
            Strings = new List<String>();
            while (heap.Position < HeapRelocationPosition)
            {
                var str = new String();
                str.Position = heap.Position16;
                str.Text = heap.ReadString();
                str.Length = (UInt16)(heap.Position - str.Position - 1);
                Strings.Add(str);

                if (heap.Position > HeapRelocationPosition) throw new Exception("Runaway string parsing");
            }

            //
            // and now to wrap up with hunk
            //

            // byte code. at this point, hunk is pointing here and after
            // that it's just the relocation table.
            ByteCodePosition = hunk.Position16;
            ByteCodeLength = (UInt16)(HunkRelocationPosition - ByteCodePosition);

            //
            // heap and hunk have now been fully parsed
            //

            // figure out string properties now that we've parsed everything.
            // unlike SCI0, there are no junk bytes in between strings. yay!
            // that means it's okay to simply parse the strings as null separated
            // and expect all valid references to point to the start of a
            // parsed string.
            foreach (var obj in Objects)
            {
                // skip the hard-coded properties we know aren't strings
                for (int i = (int)PropertyIndex11.NameHeapOffset; i < obj.Properties.Count; ++i)
                {
                    // a string property must have a relocation
                    var property = obj.Properties[i];
                    if (HeapRelocations.Any(r => r.Value == property.Position))
                    {
                        // and it must have a string
                        var str = Strings.FirstOrDefault(s => s.Position == property.Value);
                        if (str != null)
                        {
                            property.String = str;

                            // set Name property
                            if (i == (int)PropertyIndex11.NameHeapOffset)
                            {
                                obj.Name = str.Text;
                            }
                        }
                        else
                        {
                            // I was logging properties that had relocations but didn't
                            // point to the start of a string; they should indeed be ignored.
                            // I assume these are all string properties in a superclass that
                            // were inherited by an object in a different script.
                            //
                            // QFG1VGA script 206: gloryInv property #27 (normalHeading)
                            //                     0x0EEB points to "m" in "QG1InvItem",
                            //                     the first string. SCI Companion treats
                            //                     it as a number in decompilation,
                            //                     empty string in disassembly.
                            Log.Debug("Script: " + Number + " Property has relocation but not string. Object: " +
                                      obj.Name + " Property index: " + i + " Value: " + property.Value.Print());
                        }
                    }
                }
            }

            // validate that every method's code offset is valid
            foreach (var obj in Objects)
            {
                foreach (var method in obj.Methods)
                {
                    if (!(ByteCodePosition <= method.Code && method.Code < ByteCodePosition + ByteCodeLength))
                    {
                        throw new Exception("Method out of bounds for object: " + obj.Name + " selector: " + method.Selector);
                    }
                }
            }

            // validate code exports.
            // Script.DiscoverFunctions() assumes the binary parser has at least guaranteed
            // that each offset is in bounds. i also do some SCI11-specific validation here
            // to identify if an export with offset zero is invalid or potentially okay.
            for (int i = 0; i < Exports.Count; ++i)
            {
                var export = Exports[i];
                if (export.Type == ExportType.Code)
                {
                    // export values are offsets relative to the start of the script resource,
                    // except for zero, which is a special case. zero is treated as the start
                    // of the bytecode buffer. this is SCI32-only behavior, and it doesn't look
                    // intentional. it looks like a consequence of the c++ rewrite that some
                    // games accidentally depend on; the ShakePlane proc has an empty (zero)
                    // export in many games but it happens to be located at the start of the
                    // bytecode buffer so it works, even though it probably wasn't exported
                    // correctly.
                    //
                    // but zero can also be an invalid export, so identify easy ones here.
                    // this parser doesn't know if it's SCI32 or not, so just validate as
                    // if it is, and a higher level can kill off the remaining zeros on SCI16.
                    if (export.Value == 0)
                    {
                        // do any methods point to the start of the bytecode buffer?
                        // if so then this zero is an invalid export.
                        if (Objects.Any(o => o.Methods.Any(m => m.Code == ByteCodePosition)))
                        {
                            export.Type = ExportType.Invalid;
                            continue;
                        }
                        // do any exports explicitly point to the start of the bytecode buffer?
                        // if so then this zero is an invalid export.
                        if (Exports.Any(e => e.Type == ExportType.Code && e.Value == ByteCodePosition))
                        {
                            export.Type = ExportType.Invalid;
                            continue;
                        }
                        // are any previous exports zero?
                        // if so then the jury is still out on it, but *this* zero is an invalid export.
                        if (Exports.Any(e => e.Position < export.Position && e.Value == 0))
                        {
                            export.Type = ExportType.Invalid;
                            continue;
                        }
                    }

                    // now the basic boundary check; gotta be in the bytecode buffer
                    uint exportValue = (export.Value != 0) ? export.Value : ByteCodePosition;
                    if (!(ByteCodePosition <= exportValue && exportValue < ByteCodePosition + ByteCodeLength))
                    {
                        export.Type = ExportType.Invalid;
                        //Log.Debug("Script: " + Number + " Invalid Export: " + i + " Value: " + export.Value.ToString("X4"));
                    }
                }
            }

            IdentifyLocalTypes(Locals, HeapRelocations, Strings);
        }

        public static Endian DetectEndian(Span heap, Span hunk)
        {
            var result = Endian.Unknown;

            foreach (var span in new [] { heap, hunk})
            {
                span.Endian = Endian.Little;
                bool leValid = IsRelocationTableValid(span);
                span.Endian = Endian.Big;
                bool beValid = IsRelocationTableValid(span);
                if (leValid && !beValid)
                {
                    result = Endian.Little;
                    break;
                }
                else if (!leValid && beValid)
                {
                    result = Endian.Big;
                    break;
                }
            }

            heap.Endian = hunk.Endian = result;
            return result;
        }

        static bool IsRelocationTableValid(Span span)
        {
            // heap and hunk have same relocation format

            // relocation offset must be within span
            UInt16 relocationOffset = span.GetUInt16(0);
            if (!(relocationOffset < span.Length - 2)) return false;

            // relocation count must describe a table that fits within the resource
            UInt16 relocationCount = span.GetUInt16(relocationOffset);
            int relocationTableEnd = relocationOffset + 2 + (relocationCount * 2);
            if (!(relocationTableEnd <= span.Length)) return false;

            // if we could guarantee that there are never junk bytes at the end,
            // we could require the relocation table is exactly EOF, but this
            // is good enough and i doubt that never happened.
            return true;
        }

        // identify locals that point to strings.
        // they will have a heap relocation pointing to the local.
        static void IdentifyLocalTypes(IReadOnlyList<Local> locals, IReadOnlyList<Relocation16> relocations,
                                       IReadOnlyList<String> strings)
        {
            foreach (var local in locals)
            {
                if (strings.Any(s => s.Position == local.Value) &&
                    relocations.Any(r => r.Value == local.Position))
                {
                    local.Type = LocalType.String;
                }
                // sanity check
                else if (relocations.Any(r => r.Value == local.Position))
                {
                    throw new Exception("local has relocation but there is no string");
                }
            }
        }
    }

    // The first nine properties are hard-coded
    public enum PropertyIndex11
    {
        MagicNumber        = 0,
        PropertyCount      = 1,
        PropDictHunkOffset = 2,
        MethDictHunkOffset = 3,
        Class              = 4,
        Species            = 5,
        Super              = 6,
        Info               = 7,
        NameHeapOffset     = 8
    }

    // Objects are defined in the heap
    public class Object11
    {
        public UInt16 Position; // heap
        public bool IsClass;    // from -info- property
        public string Name;     // from name property

        public List<Property16> Properties;
        public List<UInt16> ClassPropertySelectors; // classes only
        public List<Method11> Methods;

        // Brain2: Species is 0xffff for cSound so SuperClass is better to use for instances
        public UInt16 Species    { get { return Properties[(int)PropertyIndex11.Species].Value; } }
        public UInt16 SuperClass { get { return Properties[(int)PropertyIndex11.Super].Value; } }
        public UInt16 Info       { get { return Properties[(int)PropertyIndex11.Info].Value; } }

        public override string ToString()
        {
            return Name + (IsClass ? " (Class)" : " (Object)") + " at: " + Position.Print();
        }
    }

    // Methods are part of the Object structure in the heap
    public class Method11
    {
        public UInt16 Position;  // heap
        public UInt16 Selector;  // selector for this method
        public UInt16 Code;      // location of method bytecode (hunk offset)

        public override string ToString()
        {
            return "Selector: " + Selector.Print() + ", Code offset: " + Code.Print();
        }
    }
}
