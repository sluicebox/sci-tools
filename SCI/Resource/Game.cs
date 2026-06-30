using System;
using System.Collections.Generic;
using System.Linq;

// Game & Friends are the top-level classes of SCI.Resource.
//
// Input:  A game directory via constructor
// Output: A Game with a ResourceManager, Script objects, Selectors, a Class
//         table, and the version info required to pass code to ByteCodeParser.
//
// All script objects, properties, methods, and procedures are parsed.
// All resources can be found with ResourceManager, but if you don't care about
// scripts then maybe just use ResourceManager directly and skip Game.
//
// The goal is for a Game object to have everything necessary to hand off to a
// decompiler that would use ByteCodeParser on every function.
//
// Each script is first independently parsed into a version-specific class:
// Script0, Script11, Script3, or ScriptL. These are the reference parsers.
// Afterwards, they are tediously mapped into a generic Script class so that
// callers can operate on all games and scripts the same regardless of version.
// The Script class contains a reference to the version-specific script class
// in its Source property, for cases where it provides useful context.

namespace SCI.Resource
{
    public class Game
    {
        public string Directory { get { return ResourceManager.Directory; } }
        public string Name { get { return ResourceManager.Name; } }
        public string Id { get; private set; } // game object name

        public ResourceManager ResourceManager { get; private set; }
        public UInt16[] ClassTable { get; private set; }
        public string[] Selectors { get; private set; }
        public string[] KernelTable { get; private set; }
        public Vocab Vocab { get; private set; } // nullable
        public ScriptFormat ScriptFormat { get; private set; }
        public ByteCodeVersion ByteCodeVersion { get; private set; }
        public LofsAddressType LofsAddressType { get; private set; }

        public List<Script> Scripts { get; private set; }

        public Game(string gameDirectory)
        {
            ResourceManager = new ResourceManager(gameDirectory);
            ReadClassTable(); // vocab 996
            ReadSelectors();  // vocab 997

            // guess the script format by the presence of heaps and known system scripts
            if (ResourceManager.Has(ResourceType.Script))
            {
                if (ResourceManager.Has(ResourceType.Heap))
                {
                    ScriptFormat = ScriptFormat.SCI11;
                }
                else if (ResourceManager.Has(ResourceType.Script, 64999))
                {
                    // Obj in LSL7, Code in Phant2
                    ScriptFormat = ScriptFormat.SCI3;
                }
                else if (ResourceManager.Has(ResourceType.Script, 60000))
                {
                    // Obj in INN
                    ScriptFormat = ScriptFormat.LSCI;
                }
                else
                {
                    ScriptFormat = ScriptFormat.SCI0;
                }
            }

            // guess byte code version by script format, plus the presence
            // of system script 64999 which indicates 32-bit SCI.
            if (ScriptFormat == ScriptFormat.SCI0)
            {
                ByteCodeVersion = ByteCodeVersion.SCI0_11;
            }
            else if (ScriptFormat == ScriptFormat.LSCI)
            {
                ByteCodeVersion = ByteCodeVersion.LSCI;
            }
            else if (ScriptFormat == ScriptFormat.SCI11)
            {
                if (!ResourceManager.Has(ResourceType.Script, 64999) &&
                    !ResourceManager.Has(ResourceType.Script, 60000))
                {
                    ByteCodeVersion = ByteCodeVersion.SCI0_11;
                }
                else
                {
                    ByteCodeVersion = ByteCodeVersion.SCI2;
                }
            }
            else if (ScriptFormat == ScriptFormat.SCI3)
            {
                ByteCodeVersion = ByteCodeVersion.SCI3;
            }

            // read vocab from vocab.000 or vocab.900
            if (ResourceManager.Has(ResourceType.Vocab, 0))
            {
                // vocab.000 could be any vocab version; use detection
                Vocab = Vocab.Read(ResourceManager.GetResource(ResourceType.Vocab, 0), VocabVersion.None);
            }
            else if (ResourceManager.Has(ResourceType.Vocab, 900))
            {
                // vocab.900 is always the later vocab format
                Vocab = Vocab.Read(ResourceManager.GetResource(ResourceType.Vocab, 900), VocabVersion.SCI1);
            }

            // parse every script according to the rules of the game's script format,
            // then create a common Script object from each format-specific object.
            // afterwards, we'll locate procedures using the common Script objects.
            var scriptIds = ResourceManager.GetResources(ResourceType.Script).ToList();
            Scripts = new List<Script>(scriptIds.Count);
            if (ScriptFormat == ScriptFormat.SCI0)
            {
                // parse all scripts as SCI0
                var script0s = new List<Script0>(Scripts.Count);
                foreach (var scriptId in scriptIds)
                {
                    script0s.Add(new Script0(scriptId.Number, ResourceManager.GetResource(scriptId), Vocab));
                }

                // adjust the selector table if this is SCI0 early
                if (script0s[0].HasOldHeader)
                {
                    Selectors = SelectorVocab.CreateSci0EarlyTable(Selectors);
                }

                // build dictionary of class selectors
                var classSelectors = new Dictionary<UInt16, List<UInt16>>();
                for (UInt16 species = 0; species < ClassTable.Length; species++)
                {
                    var script = script0s.FirstOrDefault(s => s.Number == ClassTable[species]);
                    if (script != null)
                    {
                        var class_ = script.Objects.FirstOrDefault(o => o.IsClass && o.Species == species);
                        if (class_ != null)
                        {
                            classSelectors.Add(species, class_.ClassPropertySelectors);
                        }
                    }
                }

                // convert scripts
                foreach (var script0 in script0s)
                {
                    Scripts.Add(new Script(this, script0, classSelectors));
                }
            }
            else if (ScriptFormat == ScriptFormat.LSCI)
            {
                // parse all scripts as LSCI
                var scriptLs = new List<ScriptL>(Scripts.Count);
                foreach (var scriptId in scriptIds)
                {
                    scriptLs.Add(new ScriptL(scriptId.Number, ResourceManager.GetResource(scriptId), Vocab));
                }

                // build dictionary of class selectors
                var classSelectors = new Dictionary<UInt16, List<UInt16>>();
                for (UInt16 species = 0; species < ClassTable.Length; species++)
                {
                    var script = scriptLs.FirstOrDefault(s => s.Number == ClassTable[species]);
                    if (script != null)
                    {
                        var class_ = script.Objects.FirstOrDefault(o => o.IsClass && o.Species == species);
                        if (class_ != null)
                        {
                            classSelectors.Add(species, class_.ClassPropertySelectors);
                        }
                    }
                }

                // convert scripts
                foreach (var scriptL in scriptLs)
                {
                    Scripts.Add(new Script(this, scriptL, classSelectors));
                }
            }
            else if (ScriptFormat == ScriptFormat.SCI11)
            {
                // detect script endian. we only need to do this for SCI11.
                // i have a good heuristic that works on both heap and hunk and
                // we only need one resource that brings back a conclusive result
                // to move forward. otherwise Unknown is used, which has the same
                // effect as Little endian. we'll then set all the spans to this
                // in the script parsing loop. by doing this here, we can
                // automatically handle a directory of dumped mac patch files.
                var scriptEndian = Endian.Unknown;
                foreach (var scriptId in scriptIds)
                {
                    var heapId = new ResourceId(ResourceType.Heap, scriptId.Number);
                    if (!ResourceManager.Has(heapId)) continue; // ignore for now, we'll throw later

                    var heap = ResourceManager.GetResource(heapId);
                    var hunk = ResourceManager.GetResource(scriptId);

                    scriptEndian = Script11.DetectEndian(heap, hunk);
                    if (scriptEndian != Endian.Unknown) break;
                }

                // parse all scripts as SCI11
                var script11s = new List<Script11>(Scripts.Count);
                foreach (var scriptId in scriptIds)
                {
                    var heapId = new ResourceId(ResourceType.Heap, scriptId.Number);
                    if (!ResourceManager.Has(heapId)) throw new Exception("No heap for script: " + scriptId.Number);

                    var heap = ResourceManager.GetResource(heapId);
                    var hunk = ResourceManager.GetResource(scriptId);

                    // "TEMPORARY" KQ6 MAC HACK: SKIP TRUNCATED DEBUG SCRIPT
                    if (scriptEndian == Endian.Big && scriptId.Number == 911 && hunk.Length == 2926)
                    {
                        continue;
                    }

                    // set endian on all spans to what we've detected so that Script11 parses correctly
                    // and subsequent ByteCodeParser usage parses this correctly.
                    heap.Endian = hunk.Endian = scriptEndian;

                    script11s.Add(new Script11(scriptId.Number, heap, hunk));
                }

                // build dictionary of class property selectors
                var classSelectors = new Dictionary<UInt16, List<UInt16>>();
                for (UInt16 species = 0; species < ClassTable.Length; ++species)
                {
                    var script = script11s.FirstOrDefault(s => s.Number == ClassTable[species]);
                    if (script != null)
                    {
                        var class_ = script.Objects.FirstOrDefault(o => o.IsClass && o.Species == species);
                        if (class_ != null)
                        {
                            classSelectors.Add(species, class_.ClassPropertySelectors);
                        }
                    }
                }

                // convert scripts
                foreach (var script11 in script11s)
                {
                    Scripts.Add(new Script(this, script11, classSelectors));
                }
            }
            else if (ScriptFormat == ScriptFormat.SCI3)
            {
                // parse all scripts as SCI3 and convert
                foreach (var scriptId in scriptIds)
                {
                    var script3 = new Script3(scriptId.Number, ResourceManager.GetResource(scriptId));
                    Scripts.Add(new Script(this, script3));
                }
            }

            foreach (var script in Scripts)
            {
                script.DiscoverFunctions();
                script.IdentityObjectProcedures();
            }

            // resolve every object's class
            foreach (var obj in Scripts.SelectMany(s => s.Objects))
            {
                obj.Class = GetClass(obj);
            }

            // detect lofs address type by parsing each function until
            // we get an unambiguous answer
            foreach (var script in Scripts)
            {
                LofsAddressType = script.DetectLofsAddressType();
                if (LofsAddressType != LofsAddressType.Unknown) break;
            }

            // fixup any SCI0 strings that were incorrectly parsed due to junk bytes.
            // for now, i am doing SCI11 too, but i haven't seen any junk bytes there.
            if (ScriptFormat == ScriptFormat.SCI0 || ScriptFormat == ScriptFormat.SCI11)
            {
                foreach (var script in Scripts)
                {
                    script.FixupSci0Strings();
                }
            }

            // Id is the name of the game object; export 0 of script 0
            Id = ReadGameObjectName();

            // build out the kernel table last; it has many heuristics
            KernelTable = KernelTableBuilder.Build(this);

            // HACK: remove GK1's broken duplicate sysLogger, riddled with corruption.
            if (Id == "GK")
            {
                Scripts.RemoveAll(s => s.Number == 952);
            }
            // HACK: remove Hoyle3's broken unused Inventory script, it's truncated.
            if (Id == "hoyle3")
            {
                Scripts.RemoveAll(s => s.Number == 995 && s.Span.Length == 2632);
            }
        }

        public override string ToString()
        {
            if (Id == "")
            {
                return Name;
            }
            return Name + " [" + Id + "]";
        }

        public string GetSelectorName(int number)
        {
            // use sci companion syntax for selectors without entries
            return (number < Selectors.Length) ? Selectors[number] : ("sel_" + number);
        }

        public Object GetClass(Object obj)
        {
            return GetClass(obj.Script, obj.SuperClass);
        }

        public Object GetClass(Script caller, int classNumber)
        {
            // Obj is king of the hill
            if (classNumber == 0xffff) return null;

            // first check the object's script in case it's local to the script.
            // there can be multiple classes with the same species in a game.
            // only one can be in the class table, but the local ones can still
            // be used by objects within their own script.
            var cls = caller.GetClass(classNumber);
            if (cls != null) return cls;

            // next check the class table. some classes have species that
            // are greater than the class table. these are entirely local.
            // scummvm has hard-coded exceptions for these but i think they
            // should just resize the table for any species that's > table.
            // there are also bogus values in scrap scripts like Holye5 Bridge,
            // so we do need to verify the range.
            if (classNumber < ClassTable.Length)
            {
                var classScriptNumber = ClassTable[classNumber];
                var classScript = Scripts.FirstOrDefault(s => s.Number == classScriptNumber);
                if (classScript != null) // it doesn't always exist
                {
                    cls = classScript.GetClass(classNumber);
                    if (cls != null) return cls;
                }
            }

            // the class table can be wrong! for example, Blk in SQ3 German
            // should be 998 but is instead a non-existent script. in that case,
            // just scan the game and take the first one (kind of what scummvm does
            // with its class initialization, which is how SQ3 German Blk works)
            var classes = new List<Object>();
            foreach (var script in Scripts)
            {
                cls = script.GetClass(classNumber);
                if (cls != null) classes.Add(cls);
            }

            if (classes.Count == 1)
            {
                return classes[0];
            }
            else if (classes.Count > 1)
            {
                Log.Warn(this, "Ambiguous class number " + classNumber + ": " + string.Join(", ", classes.Select(c => c.ToString())));
            }
            return null;
        }

        void ReadClassTable()
        {
            var vocab = ResourceManager.GetResource(ResourceType.Vocab, 996);
            if (vocab != null)
            {
                ClassTable = ClassTableVocab.Read(vocab);
            }
            else
            {
                // no class table
                ClassTable = new UInt16[0];
            }
        }

        void ReadSelectors()
        {
            var vocab = ResourceManager.GetResource(ResourceType.Vocab, 997);
            if (vocab != null)
            {
                // selector table endian is auto detected and handled
                Selectors = SelectorVocab.Read(vocab);
            }
            else
            {
                // no selector table
                Selectors = new string[0];
            }
        }

        string ReadGameObjectName()
        {
            var script = Scripts.FirstOrDefault(s => s.Number == 0);
            if (script != null)
            {
                var gameObject = script.GetExportedObject(0);
                if (gameObject != null)
                {
                    return gameObject.Name;
                }
            }
            return "";
        }
    }

    public enum ScriptFormat
    {
        Unknown,
        SCI0,
        SCI11,
        SCI3,
        LSCI
    }

    public partial class Script
    {
        public object Source { get; private set; }
        public Game Game { get; private set; }
        public int Number { get; private set; }
        public Span Span { get; private set; } // hunk on SCI11, use Source to get SCI11 heap

        public Dictionary<UInt32, UInt32> Relocations { get; private set; } // SCI3, LSCI

        public List<Export> Exports { get; private set; }
        public List<Local> Locals { get; private set; }
        public List<Object> Objects { get; private set; }

        public List<String> Strings { get; private set; }
        public List<Said> Saids { get; private set; }
        public List<Synonym> Synonyms { get; private set; }

        public List<Procedure> Procedures { get; private set; }
        public List<Function> Functions { get; private set; }

        public override string ToString()
        {
            string description = "Script " + Number;
            // note that this logic does not exactly match decompiler logic in Symbols.cs
            foreach (var export in Exports.Where(e => e.Type == ExportType.Object))
            {
                var obj = Objects.FirstOrDefault(o => o.Position == export.Value);
                if (obj != null && !string.IsNullOrWhiteSpace(obj.Name))
                {
                    return description + " - " + obj.Name;
                }
            }
            foreach (var cls in Objects.Where(o => o.IsClass))
            {
                return description + " - " + cls.Name;
            }
            foreach (var cls in Objects.Where(o => !o.IsClass))
            {
                return description + " - " + cls.Name;
            }
            return description;
        }

        public Script(Game game, Script0 source, IReadOnlyDictionary<UInt16, List<UInt16>> classPropertySelectors)
        {
            Source = source;
            Game = game;
            Number = source.Number;
            Span = source.Span;
            Relocations = new Dictionary<UInt32, UInt32>();
            Exports = source.Exports;
            Locals = source.Locals;
            Objects = new List<Object>(source.Objects.Count);
            foreach (var obj in source.Objects)
            {
                Objects.Add(new Object(this, obj, classPropertySelectors));
            }
            Strings = source.Strings;
            Saids = source.Saids;
            Synonyms = source.Synonyms;
        }

        public Script(Game game, ScriptL source, IReadOnlyDictionary<UInt16, List<UInt16>> classPropertySelectors)
        {
            Source = source;
            Game = game;
            Number = source.Number;
            Span = source.Span;
            // LSCI stores relocations in separate tables that hang off of blocks.
            // Translate them from block indexes into absolute positions and store
            // them in a single dictionary. This lets me treat SCI3 relocations
            // and LSCI relocations the same in the common layer.
            Relocations = new Dictionary<UInt32, UInt32>();
            foreach (var block in source.Blocks)
            {
                // "relocation" is an offset relative to the start of the block
                // that contains a 16-bit index for a block.
                foreach (UInt16 relocation in block.Relocations)
                {
                    UInt32 absoluteRelocation = block.Position + relocation;
                    UInt16 targetBlockIndex = Span.GetUInt16((int)absoluteRelocation);
                    BlockL targetBlock = source.Blocks[targetBlockIndex];
                    Relocations.Add(absoluteRelocation, targetBlock.Position);
                }
            }

            Exports = new List<Export>(source.Exports.Count);
            // LSCI code and object exports are block indexes.
            // Convert them to the block content position.
            // For code, that's the start of bytecode.
            // For objects, it's the identifying position (Object.Position).
            // Export.Position is currently 16-bit so it would be a problem if
            // the export table appeared after 64k, but that never happens.
            foreach (var sourceExport in source.Exports)
            {
                // make a copy so that we can keep the original in Source
                if (sourceExport.Type == ExportType.Code ||
                    sourceExport.Type == ExportType.Object)
                {
                    var newExport = new Export();
                    newExport.Type = sourceExport.Type;
                    newExport.Position = sourceExport.Position;
                    newExport.Value = source.Blocks[(int)sourceExport.Value].Position;
                    Exports.Add(newExport);
                }
                else
                {
                    Exports.Add(sourceExport);
                }
            }
            // Locals that point to strings or saids use block indexes.
            // Convert these index values to absolute positions.
            // Local.Position and .Value are currently 16-bit so it would
            // be a problem if they appear after 64k, so far hasn't happened.
            Locals = new List<Local>(source.Locals.Count);
            foreach (var sourceLocal in source.Locals)
            {
                // make a copy so that we can keep the original in Source
                if (sourceLocal.Type == LocalType.String ||
                    sourceLocal.Type == LocalType.Said)
                {
                    var newLocal = new Local();
                    newLocal.Type = sourceLocal.Type;
                    newLocal.Position = sourceLocal.Position;
                    // i suspect this just never happens. 64k before locals??
                    if (source.Blocks[sourceLocal.Value].Position >= UInt16.MaxValue)
                    {
                        throw new Exception("LSCI Local points to block beyond 64k");
                    }
                    newLocal.Value = (UInt16)source.Blocks[sourceLocal.Value].Position;
                    Locals.Add(newLocal);
                }
                else
                {
                    Locals.Add(sourceLocal);
                }
            }
            Objects = new List<Object>(source.Objects.Count);
            foreach (var obj in source.Objects)
            {
                Objects.Add(new Object(this, obj, classPropertySelectors, source.Blocks));
            }
            Strings = source.Strings;
            Saids = source.Saids;
            // TODO
            Synonyms = new List<Synonym>();
        }

        public Script(Game game, Script11 source, IReadOnlyDictionary<UInt16, List<UInt16>> classPropertySelectors)
        {
            Source = source;
            Game = game;
            Number = source.Number;
            Span = source.Hunk;
            Relocations = new Dictionary<UInt32, UInt32>();
            // SCI11 code exports are relative to script resource, unless the offset
            // is zero, in which case SCI32 would special case that to the start
            // of the bytecode buffer. but, it could also mean an invalid export.
            // Script11 filtered out the obvious ones, but now we know the bytecode
            // version, so we can handle the others.
            Exports = new List<Export>(source.Exports.Count);
            foreach (var sourceExport in source.Exports)
            {
                if (sourceExport.Type == ExportType.Code && sourceExport.Value == 0)
                {
                    if (game.ByteCodeVersion == ByteCodeVersion.SCI0_11)
                    {
                        // SCI16: a zero export is invalid
                        sourceExport.Type = ExportType.Invalid;
                        Exports.Add(sourceExport);
                    }
                    else
                    {
                        // SCI32: a zero export could be valid, and we already
                        // flagged the obvious ones as invalid in Script11.
                        // make a copy so that we can keep the original in Source.
                        var newExport = new Export();
                        newExport.Type = sourceExport.Type;
                        newExport.Position = sourceExport.Position;
                        newExport.Value = source.ByteCodePosition;
                        Exports.Add(newExport);
                    }
                }
                else
                {
                    Exports.Add(sourceExport);
                }
            }
            Locals = source.Locals;
            Objects = new List<Object>(source.Objects.Count);
            foreach (var obj in source.Objects)
            {
                Objects.Add(new Object(this, obj, classPropertySelectors));
            }
            Strings = source.Strings;
            Saids = new List<Said>(0);
            Synonyms = new List<Synonym>(0);
        }

        public Script(Game game, Script3 source)
        {
            Source = source;
            Game = game;
            Number = source.Number;
            Span = source.Span;
            Relocations = source.Relocations.ToDictionary(k => k.Offset, v => v.Value);
            Exports = new List<Export>(source.Exports.Count);
            // SCI3 code exports are relative to bytecode buffer so adjust them
            foreach (var sourceExport in source.Exports)
            {
                // make a copy so that we can keep the original in Source
                if (sourceExport.Type == ExportType.Code)
                {
                    var newExport = new Export();
                    newExport.Type = sourceExport.Type;
                    newExport.Position = sourceExport.Position;
                    newExport.Value = source.ByteCodePosition + sourceExport.Value;
                    Exports.Add(newExport);
                }
                else
                {
                    Exports.Add(sourceExport);
                }
            }
            Locals = source.Locals;
            Objects = new List<Object>(source.Objects.Count);
            foreach (var obj in source.Objects)
            {
                Objects.Add(new Object(this, obj, source.ByteCodePosition));
            }
            Strings = source.Strings;
            Saids = new List<Said>(0);
            Synonyms = new List<Synonym>(0);
        }

        public Object GetExportedObject(int index)
        {
            if (index >= Exports.Count) return null;
            if (Exports[index].Type != ExportType.Object) return null;
            UInt32 position;
            if (!Relocations.TryGetValue(Exports[index].Position, out position))
            {
                position = Exports[index].Value;
            }
            return Objects.First(o => o.Position == position);
        }

        public Procedure GetExportedProcedure(int index)
        {
            if (index >= Exports.Count) return null;
            if (Exports[index].Type != ExportType.Code) return null;
            UInt32 position = Exports[index].Value;
            return Procedures.First(p => p.CodePosition == position);
        }

        public void IdentityObjectProcedures()
        {
            if (Procedures.Count == 0) return;

            // if a procedure is surrounded by an object's methods, assign it to the object.
            // in SCI0, this is restricted to the code block that the procedure is in.
            foreach (var obj in Objects.Where(o => o.Methods.Any()))
            {
                foreach (var proc in Procedures)
                {
                    if (proc.Object != null) break;

                    IReadOnlyList<Method> methods;
                    if (Game.ScriptFormat == ScriptFormat.SCI0)
                    {
                        var source = (Script0)Source;

                        var block = (from b in source.Blocks
                                     where b.Position <= proc.CodePosition &&
                                           proc.CodePosition < b.Position + b.Length
                                     select b).First();

                        methods = (from m in obj.Methods
                                   where block.Position <= m.CodePosition &&
                                         m.CodePosition < block.Position + block.Length
                                   select m).ToList();
                    }
                    else
                    {
                        methods = obj.Methods;
                    }

                    var first = methods.OrderBy(o => o.CodePosition).FirstOrDefault();
                    var last = methods.OrderByDescending(o => o.CodePosition).FirstOrDefault();
                    if (first != null && last != null &&
                        first.CodePosition < proc.CodePosition &&
                        proc.CodePosition < last.CodePosition)
                    {
                        proc.SetObject(obj);
                    }
                }
            }

            // identify procedures that contain property operations
            var objProcs = new HashSet<Procedure>();
            var parser = new ByteCodeParser();
            foreach (var proc in Procedures.Where(p => p.Object == null))
            {
                parser.Parse(proc);
                while (parser.Next())
                {
                    switch (parser.Operation)
                    {
                        case Operation.pToa:
                        case Operation.pTos:
                        case Operation.aTop:
                        case Operation.sTop:
                        case Operation.ipToa:
                        case Operation.ipTos:
                        case Operation.dpToa:
                        case Operation.dpTos:
                            objProcs.Add(proc);
                            parser.Stop();
                            break;
                    }
                }
            }
            if (objProcs.Count == 0) return;

            // scan all methods for calls to object procedures.
            // if a call is found, that's it, the method's object owns it.
            // this doesn't need to be any more complicated; object
            // procedures are rare.
            foreach (var method in Objects.SelectMany(o => o.Methods))
            {
                parser.Parse(method);
                while (parser.Next())
                {
                    if (parser.Operation == Operation.call)
                    {
                        int procPosition = (int)method.CodePosition + parser.NextInstructionPosition + parser.GetSignedOperand(0);
                        var proc = objProcs.FirstOrDefault(p => p.CodePosition == procPosition);
                        if (proc != null)
                        {
                            proc.SetObject(method.Object);
                            objProcs.Remove(proc);

                            // stop once we've identified everyone
                            if (objProcs.Count == 0) return;
                        }
                    }
                }
            }
        }

        public LofsAddressType DetectLofsAddressType()
        {
            if (Source is Script11) return LofsAddressType.Absolute;
            if (Source is Script3) return LofsAddressType.Relocated;
            if (Source is ScriptL) return LofsAddressType.Relocated;

            var blocks = ((Script0)Source).Blocks;
            var parser = new ByteCodeParser();
            foreach (var function in Functions)
            {
                parser.Parse(function);
                while (parser.Next())
                {
                    if (parser.Operation == Operation.lofsa ||
                        parser.Operation == Operation.lofss)
                    {
                        int relative = (int)function.CodePosition +
                                       parser.NextInstructionPosition +
                                       parser.GetSignedOperand(0);
                        int absolute = parser.GetSignedOperand(0);

                        bool isRelative = IsLofsAddressValid(relative, blocks);
                        bool isAbsolute = IsLofsAddressValid(absolute, blocks);
                        if (isRelative && !isAbsolute)
                        {
                            return LofsAddressType.Relative;
                        }
                        else if (isAbsolute && !isRelative)
                        {
                            return LofsAddressType.Absolute;
                        }
                    }
                }
            }
            return LofsAddressType.Unknown;
        }

        // does the lofs address land in an appropriate SCI0 block?
        // (i don't want to depend on exact matches for this)
        static bool IsLofsAddressValid(int offset, IEnumerable<Block> blocks)
        {
            return blocks.Any(b => b.Position <= offset && offset < b.Position + b.Length &&
                                   (b.Type == BlockType.Class ||
                                    b.Type == BlockType.Instance ||
                                    b.Type == BlockType.Saids ||
                                    b.Type == BlockType.Strings));
        }

        public Object GetClass(int species)
        {
            return Objects.FirstOrDefault(o => o.IsClass && o.Species == species);
        }

        // Parsing strings from SCI0 scripts is tricky. Script0 takes the naive
        // approach and gets most of them right, the rest are figured out here.
        //
        // Strings are separated by null terminators, but some also have junk bytes
        // after the null terminator. This makes strings appear longer and have an
        // incorrect start offset. We need to look at string references to identify
        // the correct offsets, but SCI has bogus string references too, and these
        // garbage values occasionally happen to point within real strings.
        // (KQ5CD script 758: KQInv:empty correctly points at "Nothing!", but
        // KQInv:curScore has a relocation and happens to point to the "!")
        // For example, if a superclass property points to a string, and a subclass
        // in another script doesn't override this, then superclass' offset property
        // is copied into the subclass script, where it points to whatever.
        //
        // ----- Script0 string parsing -----
        // 1. Naively parse strings as null delimited. Gets most right.
        // 2. If the "name" property of an object points within a string, then
        //    we know that's wrong, so create a new string with that offset.
        //    Only "name" can be trusted.
        // ----- FixupSci0Strings() -----
        // 1. Record all strings that are referenced by properties. These are correct.
        // 2. Record all strings that are referenced by a lofsa instruction, and record
        //    all lofsa offsets that point within a string, because those indicate an
        //    incorrectly parsed string.
        //    (Longbow 1.0 script 851 "9Aye" => "Aye")
        //    One reason to do this all of this outside Script0 is that it doesn't
        //    know if lofsa offsets are relative or absolute.
        //    Another reason is that Script11 might need this too.
        // 3. Adjust all the strings with lofsa offsets pointing within them, unless
        //    the string is already referenced. (should never happen?)
        // 4. Treat all object properties that point within a string we know to be unused
        //    as a string reference, and set the correct string offset.
        //    (QFG2 1.000 script 865 feat1:lookString)
        // 5. If any locals have a relocation and point within an unused string then fix it.
        //    (Brain1 1.1 script 285, local44)
        //
        // I am now applying this to SCI11 too, but it's had no effect. I guess it has no
        // junk bytes in between the strings. Still, I wouldn't be surprised if some fan
        // stuff produces weird things. Leaving it in for now.
        public void FixupSci0Strings()
        {
            if (!Strings.Any()) return;

            var usedStrings = new HashSet<String>();
            var unknownStringOffsets = new HashSet<int>();
            var relocations = (Source is Script0) ?
                              new HashSet<UInt16>((Source as Script0).Relocations.Select(r => r.Value)) :
                              new HashSet<UInt16>((Source as Script11).HeapRelocations.Select(r => r.Value));

            // record strings that have a property correctly pointed at them
            foreach (var property in Objects.SelectMany(o => o.Properties))
            {
                if (property.String != null)
                {
                    usedStrings.Add(Strings.First(s => s.Position == property.Value));
                }
            }

            // record strings that have a lofsa correctly pointed at them,
            // and record lofsa offsets that point within strings.
            var parser = new ByteCodeParser();
            foreach (var function in Functions)
            {
                // find all offsets that fall within a string
                parser.Parse(function);
                while (parser.Next())
                {
                    if (parser.Operation == Operation.lofsa ||
                        parser.Operation == Operation.lofss)
                    {
                        int offset = parser.GetSignedOperand(0);
                        if (Game.LofsAddressType == LofsAddressType.Relative)
                        {
                            offset += parser.NextInstructionPosition + (int)function.CodePosition;
                        }
                        var str = Strings.FirstOrDefault(s => s.Position <= offset && offset < s.Position + s.Length);
                        if (str != null)
                        {
                            if (str.Position == offset)
                            {
                                usedStrings.Add(str);
                            }
                            else
                            {
                                unknownStringOffsets.Add(offset);
                            }
                        }
                    }
                }
            }

            // every lofsa offset that points within a string that isn't used
            // is an instance where Script0 string detection was incorrect because
            // of junk bytes in between strings. adjust each of these strings.
            foreach (var offset in unknownStringOffsets)
            {
                var str = Strings.First(s => s.Position < offset && offset < s.Position + s.Length);
                if (!usedStrings.Contains(str))
                {
                    UInt16 junkByteCount = (UInt16)(offset - str.Position);
                    str.Position += junkByteCount;
                    str.Length -= junkByteCount;
                    str.Text = str.Text.Substring(junkByteCount);
                    usedStrings.Add(str);
                }
            }

            // now we handle properties with relocations that point within parsed strings.
            // these are the least trustworthy pieces so they go last.
            foreach (var property in Objects.SelectMany(o => o.Properties))
            {
                // skip properties that already point to something
                if (property.String != null || property.Said != null) continue;

                // is there a relocation?
                var sourceProperty = (Property16)property.Source;
                if (relocations.Contains(sourceProperty.Position))
                {
                    // does this point to a string or within one?
                    // remember that this still could be a garbage offset inherited from a superclass.
                    // it could have a relocation and still happen to point within a real string,
                    // which is why it's critical to make sure the string isn't used elsewhere.
                    int offset = sourceProperty.Value;
                    var str = Strings.FirstOrDefault(s => s.Position <= offset && offset < s.Position + s.Length);
                    if (str != null)
                    {
                        if (str.Position == offset)
                        {
                            // property points to the start of a known string; it was already fixed by this function.
                            sourceProperty.String = str;
                            property.UpdateString();
                            usedStrings.Add(str); // probably redundant
                        }
                        else if (!usedStrings.Contains(str))
                        {
                            // property points within a string that we haven't used yet.
                            // Script0 parsed the string wrong; fix it.
                            UInt16 junkByteCount = (UInt16)(offset - str.Position);
                            str.Position += junkByteCount;
                            str.Length -= junkByteCount;
                            str.Text = str.Text.Substring(junkByteCount);
                            sourceProperty.String = str;
                            property.UpdateString();
                            usedStrings.Add(str);
                        }
                    }
                }
            }

            // now do locals
            foreach (var local in Locals)
            {
                if (local.Type == LocalType.Number && relocations.Contains(local.Position))
                {
                    int offset = local.Value;
                    var str = Strings.FirstOrDefault(s => s.Position <= offset && offset < s.Position + s.Length);
                    if (str != null)
                    {
                        if (str.Position == offset)
                        {
                            // local points to the start of a known string; it was already fixed by this function.
                            local.Type = LocalType.String;
                            usedStrings.Add(str); // probably redundant
                        }
                        else if (!usedStrings.Contains(str))
                        {
                            // local points within a string that we haven't used yet.
                            // Script0 parsed the string wrong; fix it.
                            UInt16 junkByteCount = (UInt16)(offset - str.Position);
                            str.Position += junkByteCount;
                            str.Length -= junkByteCount;
                            str.Text = str.Text.Substring(junkByteCount);
                            local.Type = LocalType.String;
                            usedStrings.Add(str);
                        }
                    }
                }
            }
        }
    }

    public enum ExportType
    {
        Invalid,
        Object,
        Code
    }

    public class Export
    {
        public ExportType Type; // 0:  from block type where value lives
                                // 11: based on relocations and if non-zero and other validations
                                // 3:  based on relocations and other validations
                                // L:  from indexed block type
        public UInt16 Position; // location of the export table entry
        public UInt32 Value;    // value at the export table entry
                                // this is really a 16-bit value, but i've expanded it so that
                                // i can adjust SCI3 code offsets from bytecode-buffer based
                                // to script based like in all other versions.
                                // Lighthouse script 9 only has 18k of bytecode... but it doesn't
                                // even start until way beyond the 64k barrier. it's like at 100k.
                                // SCI3 object structures are so wasteful! I converted it from 133k
                                // to SCI2 19k heap and 21k hunk.

        public override string ToString()
        {
            return "[" + Type + "] " + Value.Print();
        }
    }

    public enum LocalType
    {
        Number,  // default
        String,  // all script versions
        Said,    // SCI0 and LSCI
    }

    public class Local
    {
        public LocalType Type;
        public UInt16 Position; // always below 64k barrier in SCI3
        public UInt16 Value;

        public override string ToString()
        {
            var s = Value.Print() + " at: " + Position.Print();
            if (Type != LocalType.Number)
            {
                s += " " + Type;
            }
            return s;
        }
    }

    public class Object
    {
        public object Source { get; private set; }
        public Script Script { get; private set; }
        public UInt32 Position { get; private set; }
        public string Name { get; set; }
        public bool IsClass { get; private set; }
        public UInt16 Species { get; private set; }    // class number. ignore this on instances, not all games set it
        public UInt16 SuperClass { get; private set; } // parent class number. always safe to use. 0xffff on Obj.

        public List<Property> Properties { get; private set; }
        public List<Method> Methods { get; private set; }

        public Object Class { get; set; }

        public override string ToString()
        {
            return Name + (Class != null ? (" of " + Class.Name) : "");
        }

        public Object(Script script, Object0 source, IReadOnlyDictionary<UInt16, List<UInt16>> classPropertySelectors)
        {
            Source = source;
            Script = script;
            Position = source.Offset; // Script0.Offset is the identity. see its comments.
            Name = source.Name;
            IsClass = source.IsClass;
            Species = source.Species;
            SuperClass = source.SuperClass;

            // many objects have incorrect property counts.
            // do what sci companion does and use the lowest number, ignoring extraneous ones.
            // sigh, handle the class not existing, as in KQ5 French, Amiga, others where
            // Species and SuperClass are both 0xffff in an unused object in script 202.
            var propertySelectors = source.IsClass ? source.ClassPropertySelectors :
                                    classPropertySelectors.ContainsKey(SuperClass) ?
                                    classPropertySelectors[SuperClass] :
                                    new List<UInt16>();
            var propertyCount = Math.Min(source.Properties.Count, propertySelectors.Count);
            Properties = new List<Property>(propertyCount);
            for (UInt16 i = 0; i < propertyCount; ++i)
            {
                Properties.Add(new Property(this, source.Properties[i], i, propertySelectors[i]));
            }

            Methods = new List<Method>(source.Methods.Count);
            for (UInt16 i = 0; i < source.Methods.Count; ++i)
            {
                Methods.Add(new Method(this, source.Methods[i].Selector, source.Methods[i].Code));
            }
        }

        public Object(Script script, ObjectL source, IReadOnlyDictionary<UInt16, List<UInt16>> classPropertySelectors, IReadOnlyList<BlockL> blocks)
        {
            Source = source;
            Script = script;
            Position = source.Block.Position;
            Name = source.Name;
            IsClass = source.IsClass;
            Species = source.Species;
            SuperClass = source.SuperClass;

            // same logic as SCI0, handles objects with incorrect property counts
            var propertySelectors = source.IsClass ? source.ClassPropertySelectors :
                                    classPropertySelectors.ContainsKey(SuperClass) ?
                                    classPropertySelectors[SuperClass] :
                                    new List<UInt16>();
            var propertyCount = Math.Min(source.Properties.Count, propertySelectors.Count);
            Properties = new List<Property>(propertyCount);
            for (UInt16 i = 0; i < source.Properties.Count; ++i)
            {
                Properties.Add(new Property(this, source.Properties[i], i, propertySelectors[i]));
            }

            Methods = new List<Method>(source.Methods.Count);
            for (UInt16 i = 0; i < source.Methods.Count; ++i)
            {
                // method pointers are block indexes. convert to block content position. (bytecode)
                var methodBlock = blocks[source.Methods[i].Code];
                Methods.Add(new Method(this, source.Methods[i].Selector, methodBlock.Position));
            }
        }

        public Object(Script script, Object11 source, IReadOnlyDictionary<UInt16, List<UInt16>> classPropertySelectors)
        {
            Source = source;
            Script = script;
            Position = source.Position;
            Name = source.Name;
            IsClass = source.IsClass;
            Species = source.Species;
            SuperClass = source.SuperClass;

            // many objects have incorrect property counts.
            // do what sci companion does and use the lowest number, ignoring extraneous ones.
            // sigh, handle the class not existing, as in hoyle5 bridge.
            var propertySelectors = source.IsClass ? source.ClassPropertySelectors :
                                    classPropertySelectors.ContainsKey(SuperClass) ?
                                    classPropertySelectors[SuperClass] :
                                    new List<UInt16>();
            var propertyCount = Math.Min(source.Properties.Count, propertySelectors.Count);
            Properties = new List<Property>(propertyCount);
            for (UInt16 i = 0; i < propertyCount; ++i)
            {
                Properties.Add(new Property(this, source.Properties[i], i, propertySelectors[i]));
            }

            Methods = new List<Method>(source.Methods.Count);
            for (UInt16 i = 0; i < source.Methods.Count; ++i)
            {
                Methods.Add(new Method(this, source.Methods[i].Selector, source.Methods[i].Code));
            }
        }

        public Object(Script script, Object3 source, UInt32 byteCodePosition)
        {
            Source = source;
            Script = script;
            Position = source.Position;
            Name = source.Name;
            IsClass = source.IsClass;
            Species = source.Species;
            SuperClass = source.Super;

            Properties = new List<Property>(source.Properties.Count);
            for (UInt16 i = 0; i < source.Properties.Count; ++i)
            {
                Properties.Add(new Property(this, source.Properties[i]));
            }

            Methods = new List<Method>(source.Methods.Count);
            for (UInt16 i = 0; i < source.Methods.Count; ++i)
            {
                // SCI3 code offsets are relative to bytecode buffer and must be adjusted
                Methods.Add(new Method(this, source.Methods[i].Selector, byteCodePosition + source.Methods[i].Code));
            }
        }
    }

    public class Property
    {
        public object Source { get; private set; }
        public Object Object { get; private set; }
        public UInt16 Index { get; private set; } // referenced by disasm?. selector on SCI3
        public UInt16 Selector { get; private set; }
        public string Name { get; private set; }
        public UInt32 Value { get; private set; }
        public string String { get; private set; }
        public Said Said { get; private set; }

        public override string ToString()
        {
            return Name + " " + Value + (String != null ? (" " + String) : "");
        }

        public Property(Object obj, Property16 source, UInt16 index, UInt16 selector)
        {
            Source = source;
            Object = obj;
            Index = index;
            Selector = selector;
            Name = obj.Script.Game.GetSelectorName(selector);
            Value = source.Value;
            String = source.String?.Text;
            Said = source.Said;
        }

        public Property(Object obj, PropertyL source, UInt16 index, UInt16 selector)
        {
            Source = source;
            Object = obj;
            Index = index;
            Selector = selector;
            Name = obj.Script.Game.GetSelectorName(selector);
            Value = source.Value;
            String = source.String?.Text;
            Said = source.Said;
        }

        public Property(Object obj, Property3 source)
        {
            Source = source;
            Object = obj;
            Index = source.Selector;
            Selector = source.Selector;
            Name = obj.Script.Game.GetSelectorName(source.Selector);
            Value = source.Value;
            String = source.String?.Text;
        }

        // used when fixing up strings
        public void UpdateString()
        {
            if (Source is Property16)
            {
                String = (Source as Property16).String?.Text;
            }
            else if (Source is Property3)
            {
                String = (Source as Property3).String?.Text;
            }
        }
    }

    public abstract class Function
    {
        public Script Script { get; protected set; }
        public Object Object { get; protected set; }
        public string Name { get; protected set; }
        public string FullName { get; protected set; }
        public UInt32 CodePosition { get; protected set; }
        public Span Code { get; set; }
        public FunctionError Errors { get; set; }

        public override string ToString()
        {
            return FullName;
        }
    }

    // similar to ByteCodeParserStatus, they may eventually become one
    [Flags]
    public enum FunctionError
    {
        None = 0,
        Truncated = 1,
        OutOfBoundsBranch = 2, // kind of the same thing as Truncated
        OutOfBoundsCall = 4,   // this implies that the whole script is truncated
        IllegalOpcode = 8,     // gibberish, like reading acc on first instruction. (gk1 sysLogger)
    }

    public class Procedure : Function
    {
        public bool IsPublic { get { return ExportNumber != 0xffff; } }
        public UInt16 ExportNumber { get; private set; } // 0xffff if local

        public Procedure(Script script, bool isPublic, UInt16 exportNumber, UInt32 codePosition)
        {
            Script = script;
            ExportNumber = isPublic ? exportNumber : UInt16.MaxValue;
            CodePosition = codePosition;
            UpdateName();
        }

        void UpdateName()
        {
            if (IsPublic)
            {
                Name = "proc" + Script.Number + "_" + ExportNumber;
            }
            else
            {
                Name = "localproc_" + CodePosition.ToString("x4");
            }
            FullName = Name;
        }

        public void SetExportNumber(UInt16 exportNumber)
        {
            ExportNumber = exportNumber;
            UpdateName();
        }

        public void SetObject(Object obj)
        {
            Object = obj;
        }
    }

    public class Method : Function
    {
        public UInt16 Selector { get; private set; }

        public Method(Object obj, UInt16 selector, UInt32 codePosition)
        {
            Script = obj.Script;
            Object = obj;
            Selector = selector;
            Name = obj.Script.Game.GetSelectorName(selector);
            FullName = obj.Name + ":" + Name;
            CodePosition = codePosition;
        }
    }

    public class String
    {
        public UInt32 Position;
        public UInt16 Length; // for when the text can't be rendered/retrieved from .NET String class in Text
        public string Text;

        public override string ToString()
        {
            return "\"" + Text + "\" at: " + Position.Print();
        }
    }

    public class Said
    {
        public UInt16 Position;
        public UInt16 Length; // in case i want to parse the bytes later?
        public string Text;

        public override string ToString()
        {
            return Text;
        }
    }

    public class Synonym
    {
        public UInt16 Group;
        public List<UInt16> Groups;
    }

    public enum LofsAddressType
    {
        Unknown,
        Relative,  // SCI0 - SCI1 early
        Absolute,  // SCI1 middle - SCI2
        Relocated, // SCI3
    }
}
