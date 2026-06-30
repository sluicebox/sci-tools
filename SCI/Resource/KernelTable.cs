using System;
using System.Collections.Generic;
using System.Linq;

// KernelTableBuilder produces an array of kernel function names for a Game.
//
// This is adapted from ScummVM's heuristics plus some of my own.
// The big difference is that ScummVM uses a high-level engine version to make
// decisions, and those versions are influenced by the versions of the resource
// map and volume files (resource.map, resource.001, etc). I don't use those
// because I don't have them, I don't require resource volumes to even exist.
// A directory of patch files is supported too. Every parser component tries
// to be responsible for its own version detection.
//
// Kernel tables are often wrong in script tools; it's just too messy a problem.
// Even while making this I found and fixed an inaccuracy that crashed ScummVM.
//
// - SinMult and CosMult *may* have been called TimesSin and TimesCos originally.
//   Times* is how they appear in vocab.999 in SCI0 games, but that's just debugger
//   metadata. I have system scripts from 1989, and another that appears to be
//   from 1988, and those have *Mult. And I have the kernel functions doc from
//   early 1989, and it also uses *Mult names, even though vocab.999 kept using
//   Times*. As the ScummVM source says, vocab.999 is untrustworthy.
//   Companion uses the Times* names on early games, probably due to vocab.999,
//   so that's an incompatibility. But I think Times* is historically wrong and it
//   should just support both.
//
// - KString, KArray, and KList all included the "K" in their script symbols because
//   otherwise they conflict with the existing String, Array, and List class names.
//   That's not my convention, that's how it worked in real scripts.
//   Companion doesn't do this, so it won't understand these K* symbols, but
//   it also fails to compile its own decompiled scripts due to the ambiguity.
//   "(String)" is legal script for "Call a kernel function named String" or
//   "Load the String class with a `class` instruction. This is SCI32-only though,
//   where Companion has lesser support.

namespace SCI.Resource
{
    static class KernelTableBuilder
    {
        public static string[] Build(Game game)
        {
            // if there are no scripts then don't do anything; heuristics will crash
            if (!game.Scripts.Any()) return new string[0];

            if (game.ByteCodeVersion == ByteCodeVersion.SCI0_11)
            {
                return BuildSci0_11(game);
            }
            if (game.ByteCodeVersion == ByteCodeVersion.LSCI)
            {
                return BuildLsci();
            }

            var sci32Version = DetectSci32Version(game);
            switch (sci32Version)
            {
                case Sci32Version.Sci2:
                case Sci32Version.Sci21_Early:
                    return Build2_21(game, sci32Version);

                case Sci32Version.Sci21_Middle:
                case Sci32Version.Sci21_Late:
                case Sci32Version.Sci3:
                case Sci32Version.Realm2:
                case Sci32Version.Realm3:
                default:
                    return BuildSci21_3(sci32Version);
            }
        }

        static string[] BuildSci0_11(Game game)
        {
            var table = new List<string>(0x8a);
            /*0x00*/ table.Add("Load");
            /*0x01*/ table.Add("UnLoad");
            /*0x02*/ table.Add("ScriptID");
            /*0x03*/ table.Add("DisposeScript");
            /*0x04*/ table.Add("Clone");
            /*0x05*/ table.Add("DisposeClone");
            /*0x06*/ table.Add("IsObject");
            /*0x07*/ table.Add("RespondsTo");
            /*0x08*/ table.Add("DrawPic");
            /*0x09*/ table.Add("Show");
            /*0x0a*/ table.Add("PicNotValid");
            /*0x0b*/ table.Add("Animate");
            /*0x0c*/ table.Add("SetNowSeen");
            /*0x0d*/ table.Add("NumLoops");
            /*0x0e*/ table.Add("NumCels");
            /*0x0f*/ table.Add("CelWide");
            /*0x10*/ table.Add("CelHigh");
            /*0x11*/ table.Add("DrawCel");
            /*0x12*/ table.Add("AddToPic");
            /*0x13*/ table.Add("NewWindow");
            /*0x14*/ table.Add("GetPort");
            /*0x15*/ table.Add("SetPort");
            /*0x16*/ table.Add("DisposeWindow");
            /*0x17*/ table.Add("DrawControl");
            /*0x18*/ table.Add("HiliteControl");
            /*0x19*/ table.Add("EditControl");
            /*0x1a*/ table.Add("TextSize");
            /*0x1b*/ table.Add("Display");
            /*0x1c*/ table.Add("GetEvent");
            /*0x1d*/ table.Add("GlobalToLocal");
            /*0x1e*/ table.Add("LocalToGlobal");
            /*0x1f*/ table.Add("MapKeyToDir");
            /*0x20*/ table.Add("DrawMenuBar");
            /*0x21*/ table.Add("MenuSelect");
            /*0x22*/ table.Add("AddMenu");
            /*0x23*/ table.Add("DrawStatus");
            /*0x24*/ table.Add("Parse");
            /*0x25*/ table.Add("Said");

            // kSetSynonyms was removed in SCI11 and set to kEmpty,
            // but then it became kPortrait for KQ6 CD and the Italian
            // and Spanish localizations.
            // ScummVM uses game id and platform, Companion does nothing.
            // I'm using game id and the presence of the calling script.
            if (game.ScriptFormat == ScriptFormat.SCI0)
            {
                /*0x26*/ table.Add("SetSynonyms");
            }
            else // SCI11
            {
                if (game.Id == "Kq6" && game.Scripts.Any(s => s.Number == 109))
                {
                    /*0x26*/ table.Add("Portrait");
                }
                else
                {
                    /*0x26*/ table.Add("Empty");
                }
            }

            /*0x27*/ table.Add("HaveMouse");
            /*0x28*/ table.Add("SetCursor");

            // SCI0 started with four file i/o functions that were later
            // removed and replaced with kFileIO on 06/20/90.
            // ScummVM adds these on SCI_VERSION_0_EARLY and SCI_VERSION_0_LATE.
            // That version is detected by compression format used in volumes.
            // KQ1, localized games (SCI_VERSION_01) and QFG2 (SCI_VERSION_1_EGA_ONLY)
            // don't have these functions.
            bool hasSci0FileFunctions;
            if (game.ScriptFormat == ScriptFormat.SCI0)
            {
                if ((game.Scripts[0].Source as Script0).HasOldHeader)
                {
                    // not necessary, just hedging by skipping the heuristic
                    hasSci0FileFunctions = true;
                }
                else
                {
                    // Heuristic: What is kDeleteKey's function number in script 999?
                    //
                    // This does not depend on object names or selectors, although the
                    // function that calls kDeleteKey is Collection:delete or Col:delete.
                    // Just scan script 999 for a call for 0x3f. kDeleteKey is the last
                    // function with the highest value, so it will be 0x3f when SCI0
                    // file functions exist. Otherwise 0x3d is kGetAngle and that has
                    // never appeared in script 999.
                    hasSci0FileFunctions = IsKernelFunctionCalled(game, 999, 0x3f);
                    if (!hasSci0FileFunctions && !IsKernelFunctionCalled(game, 999, 0x3f - 4))
                    {
                        // sanity check failed, the heuristic is ambiguous
                        throw new Exception("Unable to detect SCI0 file functions");
                    }
                }
            }
            else // SCI11
            {
                // not necessary, just hedging by skipping the heuristic
                hasSci0FileFunctions = false;
            }
            if (hasSci0FileFunctions)
            {
                /*0x29*/ table.Add("FOpen");
                /*0x2a*/ table.Add("FPuts");
                /*0x2b*/ table.Add("FGets");
                /*0x2c*/ table.Add("FClose");
            }

            table.Add("SaveGame");
            table.Add("RestoreGame");
            table.Add("RestartGame");
            table.Add("GameIsRestarting");
            table.Add("DoSound");
            table.Add("NewList");
            table.Add("DisposeList");
            table.Add("NewNode");
            table.Add("FirstNode");
            table.Add("LastNode");
            table.Add("EmptyList");
            table.Add("NextNode");
            table.Add("PrevNode");
            table.Add("NodeValue");
            table.Add("AddAfter");
            table.Add("AddToFront");
            table.Add("AddToEnd");
            table.Add("FindKey");
            table.Add("DeleteKey");
            table.Add("Random");
            table.Add("Abs");
            table.Add("Sqrt");
            table.Add("GetAngle");
            table.Add("GetDistance");
            table.Add("Wait");
            table.Add("GetTime");
            table.Add("StrEnd");
            table.Add("StrCat");
            table.Add("StrCmp");
            table.Add("StrLen");
            table.Add("StrCpy");
            table.Add("Format");
            table.Add("GetFarText");
            table.Add("ReadNumber");
            table.Add("BaseSetter");
            table.Add("DirLoop");

            // CanBeHere => CantBeHere.
            //
            // Used in Actor:canBeHere, later renamed Actor:cantBeHere (script 998).
            // No mention in the changelog. Cant appears in LSL5.
            // If selectors were a given, easiest thing to do is to test for name.
            // ScummVM thinks cantBeHere appears in SCI_VERSION_1_LATE and uses
            // that via its static selector table, and it derives that from the map.
            bool canBeHere;
            if (game.ScriptFormat == ScriptFormat.SCI0)
            {
                if ((game.Scripts[0].Source as Script0).HasOldHeader)
                {
                    canBeHere = true;
                }
                else if (HasMethod(game, 998, "canBeHere") || HasMethod(game, 998, "sel_57"))
                {
                    // LSL3 Demo and LSL5 Demo don't have selectors
                    canBeHere = true;
                }
                else if (HasMethod(game, 998, "cantBeHere"))
                {
                    canBeHere = false;
                }
                else
                {
                    throw new Exception("Unable to detect kCanBeHere vs kCantBeHere");
                }
            }
            else // SCI11
            {
                canBeHere = false;
            }

            if (canBeHere)
            {
                table.Add("CanBeHere");
            }
            else
            {
                table.Add("CantBeHere");
            }

            table.Add("OnControl");
            table.Add("InitBresen");
            table.Add("DoBresen");

            // DoAvoider (SCI0) => Platform
            // ScummVM sets kDoAvoider on SCI_VERSION_0_EARLY and SCI_VERSION_0_LATE.
            // For now, I'm just going to do the same and tie it to the file operations.
            // But kDoAvoider is in the LSL5 kernel function list.
            // Companion gets this wrong and sets Platform on KQ5CD, which seems to be
            // the first game (SCI_VERSION_1_LATE) that uses it for a graphics check.
            if (hasSci0FileFunctions)
            {
                table.Add("DoAvoider");
            }
            else
            {
                // i'm applying this too early, but it doesn't matter because
                // sierra quickly stopped using kDoAvoider.
                table.Add("Platform");
            }

            table.Add("SetJump");
            table.Add("SetDebug");
            table.Add("InspectObj");
            table.Add("ShowSends");
            table.Add("ShowObjs");
            table.Add("ShowFree");
            table.Add("MemoryInfo");
            table.Add("StackUsage");
            table.Add("Profiler");
            table.Add("GetMenu");
            table.Add("SetMenu");
            table.Add("GetSaveFiles");
            table.Add("GetCWD");
            table.Add("CheckFreeSpace");
            table.Add("ValidPath");
            table.Add("CoordPri");
            table.Add("StrAt");
            table.Add("DeviceInfo");
            table.Add("GetSaveDir");
            table.Add("CheckSaveGame");
            table.Add("ShakeScreen");
            table.Add("FlushResources");
            table.Add("SinMult");
            table.Add("CosMult");
            table.Add("SinDiv");
            table.Add("CosDiv");
            table.Add("Graph");
            table.Add("Joystick");

            // ScummVM says this is the end of the SCI0 function table,
            // and cuts it off here. For now, I'm continuing on unless
            // there were SCI0 file functions. I don't care about providing
            // a kernel table with too many entries.
            if (hasSci0FileFunctions)
            {
                return table.ToArray();
            }

            /*0x6e*/ table.Add("ShiftScreen");
            /*0x6f*/ table.Add("Palette");
            /*0x70*/ table.Add("MemorySegment");

            // Intersections => MoveCursor => PalVary
            // I've scanned all pre-SCI11 games for 0x71 usage.
            // - Intersections is only used by Mixed-Up Mother Goose CD
            // - MoveCursor is only used by SCI_VERSION_1_LATE KQ5's and ECO1 Floppy
            // - kPalVary is used by SCI11; that's all ScummVM does to detect.
            // Companion gets this wrong and uses kIntersections instead of kMoveCursor.
            // I'm using wide exports as the heuristic. It could be a good one for
            // other tie breakers between SCI1 versions.
            if (game.ScriptFormat == ScriptFormat.SCI0)
            {
                bool hasWideExports = (game.Scripts[0].Source as Script0).HasWideExports;
                if (!hasWideExports)
                {
                    // SCI_VERSION_1_MIDDLE or earlier
                    /*0x71*/ table.Add("Intersections");
                }
                else
                {
                    // SCI_VERSION_1_LATE
                    /*0x71*/ table.Add("MoveCursor");
                }
            }
            else
            {
                // SCI_VERSION_1_1
                // PalVary in everything except ECO1 Demo.
                // I discovered this and updated ScummVM; seems related
                // to the GetMessage exception ECO1 Demo has below.
                // ScummVM tests message resources for version, I hard code.
                // Although the scripts included with ECO1 Demo expect this
                // to be MoveCursor, it's really Dummy in the interpreter.
                if (game.Id == "eco" && HasObject(game, 221, "demo"))
                {
                    /*0x71*/ table.Add("MoveCursor");
                }
                else
                {
                    /*0x71*/ table.Add("PalVary");
                }
            }

            /*0x72*/ table.Add("Memory");
            /*0x73*/ table.Add("ListOps");
            /*0x74*/ table.Add("FileIO");
            /*0x75*/ table.Add("DoAudio");
            /*0x76*/ table.Add("DoSync");
            /*0x77*/ table.Add("AvoidPath");

            // Sort or StrSplit. I think it starts out StrSplit and then became Sort.
            // ScummVM sets StrSplit if SCI_VERSION_01. QFG2 has it as Sort.
            // Companion doesn't handle StrSplit, shows up as kernel_120.
            // Scanning shows only script 255 and 944 used StrSplit like this,
            // so that's my dumb heuristic for now.
            if (game.ScriptFormat == ScriptFormat.SCI0)
            {
                if (IsKernelFunctionCalled(game, 255, 0x78))
                {
                    /*0x78*/ table.Add("StrSplit");
                }
                else
                {
                    // even if this is applied to too early a version
                    // it doesn't matter because we just proved that
                    // 0x78 StrSplit isn't being used.
                    /*0x78*/ table.Add("Sort");
                }
            }
            else // SCI11
            {
                /*0x78*/ table.Add("Sorts");
            }

            /*0x79*/ table.Add("ATan");
            /*0x7a*/ table.Add("Lock");

            // StrSplit => RemapColors in the QFG4 Demo
            if (!(game.Id == "Glory" && HasObject(game, 4, "rm4")))
            {
                /*0x7b*/ table.Add("StrSplit");
            }
            else
            {
                // QFG4 Demo. Same id as QFG1EGA late, QFG1VGA, QFG3.
                /*0x7b*/ table.Add("RemapColors");
            }

            if (game.ScriptFormat == ScriptFormat.SCI0)
            {
                /*0x7c*/ table.Add("GetMessage");
            }
            else // SCI11
            {
                // ScummVM detects this by testing message resources.
                // Comments say that ECO1 Demo is SCI1.1 but uses kGetMessage
                // because it uses an older version of message resources.
                // I'm just going to detect the demo.
                if (game.Id == "eco" && HasObject(game, 221, "demo"))
                {
                    /*0x7c*/ table.Add("GetMessage");
                }
                else
                {
                    /*0x7c*/ table.Add("Message");
                }
            }

            /*0x7d*/ table.Add("IsItSkip");
            /*0x7e*/ table.Add("MergePoly");
            /*0x7f*/ table.Add("ResCheck");
            /*0x80*/ table.Add("AssertPalette");
            /*0x81*/ table.Add("TextColors");
            /*0x82*/ table.Add("TextFonts");
            /*0x83*/ table.Add("Record");

            // KQ6 Mac
            if (game.Id == "Kq6" && game.Scripts[0].Span.Endian == Endian.Big)
            {
                /*0x84*/ table.Add("ShowMovie");
            }
            else
            {
                /*0x84*/ table.Add("PlayBack");
            }

            /*0x85*/ table.Add("ShowMovie");
            /*0x86*/ table.Add("SetVideoMode");
            /*0x87*/ table.Add("SetQuitStr");
            /*0x88*/ table.Add("DbugStr");

            // ScummVM puts two more Empty's here

            return table.ToArray();
        }

        static string[] Build2_21(Game game, Sci32Version version)
        {
            bool sci2 = (version == Sci32Version.Sci2);
            var table = new List<string>(0x9f);
            /*0x00*/ table.Add("Load");
            /*0x01*/ table.Add("UnLoad");
            /*0x02*/ table.Add("ScriptID");
            /*0x03*/ table.Add("DisposeScript");
            /*0x04*/ table.Add("Lock");
            /*0x05*/ table.Add("ResCheck");
            /*0x06*/ table.Add("Purge");
            /*0x07*/ table.Add("Clone");
            /*0x08*/ table.Add("DisposeClone");
            /*0x09*/ table.Add("RespondsTo");
            /*0x0a*/ table.Add("SetNowSeen");
            /*0x0b*/ table.Add("NumLoops");
            /*0x0c*/ table.Add("NumCels");
            /*0x0d*/ table.Add("CelWide");
            /*0x0e*/ table.Add("CelHigh");
            /*0x0f*/ table.Add("GetHighPlanePri");
            /*0x10*/ table.Add("GetHighItemPri");
            /*0x11*/ table.Add("ShakeScreen");
            /*0x12*/ table.Add("OnMe");
            /*0x13*/ table.Add("ShowMovie");
            /*0x14*/ table.Add("SetVideoMode");
            /*0x15*/ table.Add("AddScreenItem");
            /*0x16*/ table.Add("DeleteScreenItem");
            /*0x17*/ table.Add("UpdateScreenItem");
            /*0x18*/ table.Add("FrameOut");
            /*0x19*/ table.Add("AddPlane");
            /*0x1a*/ table.Add("DeletePlane");
            /*0x1b*/ table.Add("UpdatePlane");
            /*0x1c*/ table.Add("RepaintPlane");
            /*0x1d*/ table.Add("SetShowStyle");
            /*0x1e*/ table.Add("ShowStylePercent");
            /*0x1f*/ table.Add("SetScroll");
            /*0x20*/ table.Add("AddMagnify");
            /*0x21*/ table.Add("DeleteMagnify");
            /*0x22*/ table.Add("IsHiRes");
            if (sci2)
            {
                /*0x23*/ table.Add("Graph");
            }
            else
            {
                if (game.Id == "LSL6")
                {
                    // LSL6-hires kept two calls to kGraph from SCI16 version in its init code,
                    // but it doesn't use the results. This interpreter is really kEmpty, and
                    // that's what ScummVM maps it too, but I want the original code to appear.
                    /*0x23*/ table.Add("Graph");
                }
                else
                {
                    // ScummVM: "Robot in early SCI2.1 games with a SCI2 kernel table"
                    /*0x23*/ table.Add("Robot");
                }
            }
            /*0x24*/ table.Add("InvertRect");
            /*0x25*/ table.Add("TextSize");
            /*0x26*/ table.Add("Message");
            /*0x27*/ table.Add("TextColors");
            /*0x28*/ table.Add("TextFonts");
            /*0x29*/ table.Add("SaveScreen"); // Dummy
            /*0x2a*/ table.Add("SetQuitStr");
            /*0x2b*/ table.Add("EditText");
            /*0x2c*/ table.Add("InputText");
            /*0x2d*/ table.Add("CreateTextBitmap");
            if (sci2)
            {
                /*0x2e*/ table.Add("DisposeTextBitmap");
            }
            else
            {
                // ScummVM: "Priority in early SCI2.1 games with a SCI2 kernel table"
                /*0x2e*/ table.Add("Priority");
            }
            /*0x2f*/ table.Add("GetEvent");
            /*0x30*/ table.Add("GlobalToLocal");
            /*0x31*/ table.Add("LocalToGlobal");
            /*0x32*/ table.Add("MapKeyToDir");
            /*0x33*/ table.Add("HaveMouse");
            /*0x34*/ table.Add("SetCursor");
            /*0x35*/ table.Add("VibrateMouse");
            /*0x36*/ table.Add("SaveGame");
            /*0x37*/ table.Add("RestoreGame");
            /*0x38*/ table.Add("RestartGame");
            /*0x39*/ table.Add("GameIsRestarting");
            /*0x3a*/ table.Add("MakeSaveCatName");
            /*0x3b*/ table.Add("MakeSaveFileName");
            /*0x3c*/ table.Add("GetSaveFiles");
            /*0x3d*/ table.Add("GetSaveDir");
            /*0x3e*/ table.Add("CheckSaveGame");
            /*0x3f*/ table.Add("CheckFreeSpace");
            /*0x40*/ table.Add("DoSound");
            /*0x41*/ table.Add("DoAudio");
            /*0x42*/ table.Add("DoSync");
            /*0x43*/ table.Add("NewList");
            /*0x44*/ table.Add("DisposeList");
            /*0x45*/ table.Add("NewNode");
            /*0x46*/ table.Add("FirstNode");
            /*0x47*/ table.Add("LastNode");
            /*0x48*/ table.Add("EmptyList");
            /*0x49*/ table.Add("NextNode");
            /*0x4a*/ table.Add("PrevNode");
            /*0x4b*/ table.Add("NodeValue");
            /*0x4c*/ table.Add("AddAfter");
            /*0x4d*/ table.Add("AddToFront");
            /*0x4e*/ table.Add("AddToEnd");
            /*0x4f*/ table.Add("Dummy");
            /*0x50*/ table.Add("Dummy");
            /*0x51*/ table.Add("FindKey");
            /*0x52*/ table.Add("Dummy");
            /*0x53*/ table.Add("Dummy");
            /*0x54*/ table.Add("Dummy");
            /*0x55*/ table.Add("DeleteKey");
            /*0x56*/ table.Add("Dummy");
            /*0x57*/ table.Add("Dummy");
            /*0x58*/ table.Add("ListAt");
            /*0x59*/ table.Add("ListIndexOf");
            /*0x5a*/ table.Add("ListEachElementDo");
            /*0x5b*/ table.Add("ListFirstTrue");
            /*0x5c*/ table.Add("ListAllTrue");
            /*0x5d*/ table.Add("Random");
            /*0x5e*/ table.Add("Abs");
            /*0x5f*/ table.Add("Sqrt");
            /*0x60*/ table.Add("GetAngle");
            /*0x61*/ table.Add("GetDistance");
            /*0x62*/ table.Add("ATan");
            /*0x63*/ table.Add("SinMult");
            /*0x64*/ table.Add("CosMult");
            /*0x65*/ table.Add("SinDiv");
            /*0x66*/ table.Add("CosDiv");
            /*0x67*/ table.Add("GetTime");
            /*0x68*/ table.Add("Platform");
            /*0x69*/ table.Add("BaseSetter");
            /*0x6a*/ table.Add("DirLoop");
            /*0x6b*/ table.Add("CantBeHere");
            /*0x6c*/ table.Add("InitBresen");
            /*0x6d*/ table.Add("DoBresen");
            /*0x6e*/ table.Add("SetJump");
            /*0x6f*/ table.Add("AvoidPath");
            /*0x70*/ table.Add("InPolygon");
            /*0x71*/ table.Add("MergePoly");
            /*0x72*/ table.Add("SetDebug");
            /*0x73*/ table.Add("InspectObject");
            /*0x74*/ table.Add("MemoryInfo");
            /*0x75*/ table.Add("Profiler");
            /*0x76*/ table.Add("Record");
            /*0x77*/ table.Add("PlayBack");
            /*0x78*/ table.Add("MonoOut");
            /*0x79*/ table.Add("SetFatalStr");
            /*0x7a*/ table.Add("GetCWD");
            /*0x7b*/ table.Add("ValidPath");
            /*0x7c*/ table.Add("FileIO");
            /*0x7d*/ table.Add("Dummy");
            /*0x7e*/ table.Add("DeviceInfo");
            /*0x7f*/ table.Add("Palette");
            /*0x80*/ table.Add("PalVary");
            /*0x81*/ table.Add("PalCycle");
            /*0x82*/ table.Add("KArray");  // otherwise ambiguous with "Array" class
            /*0x83*/ table.Add("KString"); // otherwise ambiguous with "String" class
            /*0x84*/ table.Add("RemapColors");
            /*0x85*/ table.Add("IntegrityChecking");
            /*0x86*/ table.Add("CheckIntegrity");
            /*0x87*/ table.Add("ObjectIntersect");
            /*0x88*/ table.Add("MarkMemory");
            /*0x89*/ table.Add("TextWidth");
            /*0x8a*/ table.Add("PointSize");
            if (sci2)
            {
                return table.ToArray();
            }
            /*0x8b*/ table.Add("AddLine");
            /*0x8c*/ table.Add("DeleteLine");
            /*0x8d*/ table.Add("UpdateLine");
            /*0x8e*/ table.Add("AddPolygon");
            /*0x8f*/ table.Add("DeletePolygon");
            /*0x90*/ table.Add("UpdatePolygon");
            /*0x91*/ table.Add("Bitmap");
            /*0x92*/ table.Add("ScrollWindow");
            /*0x93*/ table.Add("SetFontRes");
            /*0x94*/ table.Add("MovePlaneItems");
            /*0x95*/ table.Add("PreloadResource");
            /*0x96*/ table.Add("Dummy");
            /*0x97*/ table.Add("ResourceTrack");
            /*0x98*/ table.Add("CheckCDisc");
            /*0x99*/ table.Add("GetSaveCDisc");
            /*0x9a*/ table.Add("TestPoly");
            /*0x9b*/ table.Add("WinHelp");
            /*0x9c*/ table.Add("LoadChunk");
            /*0x9d*/ table.Add("SetPalStyleRange");
            /*0x9e*/ table.Add("AddPicAt");
            /*0x9f*/ table.Add("MessageBox");
            return table.ToArray();
        }

        static string[] BuildSci21_3(Sci32Version version)
        {
            bool sci3 = (version == Sci32Version.Sci3);
            var table = new List<string>(0xa3);
            /*0x00*/ table.Add("Load");
            /*0x01*/ table.Add("UnLoad");
            /*0x02*/ table.Add("ScriptID");
            /*0x03*/ table.Add("DisposeScript");
            /*0x04*/ table.Add("Lock");
            /*0x05*/ table.Add("ResCheck");
            /*0x06*/ table.Add("Purge");
            /*0x07*/ table.Add("SetLanguage");
            /*0x08*/ table.Add("Dummy");
            /*0x09*/ table.Add("Dummy");
            /*0x0a*/ table.Add("Clone");
            /*0x0b*/ table.Add("DisposeClone");
            /*0x0c*/ table.Add("RespondsTo");
            /*0x0d*/ table.Add("FindSelector");
            /*0x0e*/ table.Add("FindClass");
            /*0x0f*/ table.Add("DumpClones"); // DEBUG ? DumpClones : Dummy
            /*0x10*/ table.Add("Dummy");
            /*0x11*/ table.Add("Dummy");
            /*0x12*/ table.Add("Dummy");
            /*0x13*/ table.Add("Dummy");
            /*0x14*/ table.Add("SetNowSeen");
            /*0x15*/ table.Add("NumLoops");
            /*0x16*/ table.Add("NumCels");
            /*0x17*/ table.Add("IsOnMe");
            /*0x18*/ table.Add("AddMagnify");    // Dummy in sci3
            /*0x19*/ table.Add("DeleteMagnify"); // Dummy in sci3
            /*0x1a*/ table.Add("CelRect");
            /*0x1b*/ table.Add("BaseLineSpan");
            /*0x1c*/ table.Add("CelWide");
            /*0x1d*/ table.Add("CelHigh");
            /*0x1e*/ table.Add("AddScreenItem");
            /*0x1f*/ table.Add("DeleteScreenItem");
            /*0x20*/ table.Add("UpdateScreenItem");
            /*0x21*/ table.Add("FrameOut");
            /*0x22*/ table.Add("CelInfo");
            /*0x23*/ table.Add("Bitmap");
            /*0x24*/ table.Add("CelLink");
            /*0x25*/ table.Add("Dummy");
            /*0x26*/ table.Add("Dummy");
            /*0x27*/ table.Add("Dummy");
            /*0x28*/ table.Add("AddPlane");
            /*0x29*/ table.Add("DeletePlane");
            /*0x2a*/ table.Add("UpdatePlane");
            /*0x2b*/ table.Add("RepaintPlane");
            /*0x2c*/ table.Add("GetHighPlanePri");
            /*0x2d*/ table.Add("GetHighItemPri");
            /*0x2e*/ table.Add("SetShowStyle");
            /*0x2f*/ table.Add("ShowStylePercent");
            /*0x30*/ table.Add("SetScroll"); // Dummy in sci3
            /*0x31*/ table.Add("MovePlaneItems");
            /*0x32*/ table.Add("ShakeScreen");
            /*0x33*/ table.Add("Dummy");
            /*0x34*/ table.Add("Dummy");
            /*0x35*/ table.Add("Dummy");
            /*0x36*/ table.Add("Dummy");
            /*0x37*/ table.Add("IsHiRes");
            /*0x38*/ table.Add("SetVideoMode");
            /*0x39*/ table.Add("ShowMovie");  // Dummy in sci3
            /*0x3a*/ table.Add("Robot");
            /*0x3b*/ table.Add("CreateTextBitmap");
            /*0x3c*/ table.Add("Random");
            /*0x3d*/ table.Add("Abs");
            /*0x3e*/ table.Add("Sqrt");
            /*0x3f*/ table.Add("GetAngle");
            /*0x40*/ table.Add("GetDistance");
            /*0x41*/ table.Add("ATan");
            /*0x42*/ table.Add("SinMult");
            /*0x43*/ table.Add("CosMult");
            /*0x44*/ table.Add("SinDiv");
            /*0x45*/ table.Add("CosDiv");
            /*0x46*/ table.Add("Text");
            /*0x47*/ table.Add("RandomA"); // Dummy, but RandomA in realm 3
            /*0x48*/ table.Add("Message");
            /*0x49*/ table.Add("Font");
            /*0x4a*/ table.Add("EditText"); // or Edit or maybe KEdit
            /*0x4b*/ table.Add("InputText");
            /*0x4c*/ table.Add("ScrollWindow"); // Dummy in sci3
            /*0x4d*/ table.Add("Dummy");
            /*0x4e*/ table.Add("Dummy");
            /*0x4f*/ table.Add("Dummy");
            /*0x50*/ table.Add("GetEvent");
            /*0x51*/ table.Add("GlobalToLocal");
            /*0x52*/ table.Add("LocalToGlobal");
            /*0x53*/ table.Add("MapKeyToDir");
            /*0x54*/ table.Add("HaveMouse");
            /*0x55*/ table.Add("SetCursor");
            /*0x56*/ table.Add("VibrateMouse"); // Dummy in sci3
            /*0x57*/ table.Add("Dummy");
            /*0x58*/ table.Add("Dummy");
            /*0x59*/ table.Add("Dummy");
            /*0x5a*/ table.Add("KList");   // otherwise ambiguous with "List" class
            /*0x5b*/ table.Add("KArray");  // otherwise ambiguous with "Array" class
            /*0x5c*/ table.Add("KString"); // otherwise ambiguous with "String" class
            /*0x5d*/ table.Add("FileIO");
            /*0x5e*/ table.Add("BaseSetter");
            /*0x5f*/ table.Add("DirLoop");
            /*0x60*/ table.Add("CantBeHere");
            /*0x61*/ table.Add("InitBresen");
            /*0x62*/ table.Add("DoBresen");
            /*0x63*/ table.Add("SetJump");
            /*0x64*/ table.Add("AvoidPath"); // Dummy in sci3
            /*0x65*/ table.Add("InPolygon");
            /*0x66*/ table.Add("MergePoly"); // Dummy in sci3
            /*0x67*/ table.Add("ObjectIntersect");
            /*0x68*/ table.Add("Dummy");
            /*0x69*/ table.Add("MemoryInfo");
            /*0x6a*/ table.Add("DeviceInfo");
            /*0x6b*/ table.Add("Palette");
            /*0x6c*/ table.Add("PalVary");
            /*0x6d*/ table.Add("PalCycle");
            /*0x6e*/ table.Add("RemapColors");
            /*0x6f*/ table.Add("AddLine");
            /*0x70*/ table.Add("DeleteLine");
            /*0x71*/ table.Add("UpdateLine");
            /*0x72*/ table.Add("AddPolygon");
            /*0x73*/ table.Add("DeletePolygon");
            /*0x74*/ table.Add("UpdatePolygon");
            if (version == Sci32Version.Realm2)
            {
                /*0x75*/ table.Add("PlayMidi");
                /*0x76*/ table.Add("PlayWave");
            }
            else if (version == Sci32Version.Realm3)
            {
                /*0x75*/ table.Add("KSound");
                /*0x76*/ table.Add("KDialog");
            }
            else
            {
                /*0x75*/ table.Add("DoSound");
                /*0x76*/ table.Add("DoAudio");
            }
            /*0x77*/ table.Add("DoSync");  // Dummy in realm
            /*0x78*/ table.Add("Save");    // Dummy in realm
            /*0x79*/ table.Add("GetTime");
            /*0x7a*/ table.Add("Platform");
            /*0x7b*/ table.Add("CD");
            /*0x7c*/ table.Add("SetQuitStr");
            /*0x7d*/ table.Add("GetConfig");
            /*0x7e*/ table.Add("Table");
            /*0x7f*/ table.Add("WinHelp");
            /*0x80*/ table.Add("Network");    // NETWORK ? Network : Dummy (sci3)
            /*0x81*/ table.Add("SetDebug");   // DEBUG ? SetDebug : Dummy (sci3)
            /*0x82*/ table.Add("Dummy");      // DEBUG ? InspectObject : Dummy (sci3)
            /*0x83*/ table.Add("PrintDebug"); // DEBUG ? MonoOut : Dummy (sci3)
            /*0x84*/ table.Add("SetFatalStr");// DEBUG ? SetFatalStr : Dummy (sci3) [ SetFatalStr in realm ]
            /*0x85*/ table.Add("Dummy");      // DEBUG ? IntegrityChecking : Dummy (sci3)
            /*0x86*/ table.Add("Dummy");      // DEBUG ? CheckIntegrity : Dummy (sci3)
            /*0x87*/ table.Add("Dummy");      // DEBUG ? MarkMemory : Dummy (sci3)
            /*0x88*/ table.Add("SaveScreen"); // Dummy (debug)
            /*0x89*/ table.Add("TestPoly");   // Dummy (debug)
            /*0x8a*/ table.Add("LoadChunk");
            /*0x8b*/ table.Add("SetPalStyleRange");
            /*0x8c*/ table.Add("AddPicAt");
            /*0x8d*/ table.Add("MessageBox"); // Dummy (used in kq7 though)
            /*0x8e*/ table.Add("NewRoom");
            /*0x8f*/ table.Add("Dummy");
            /*0x90*/ table.Add("Priority");
            /*0x91*/ table.Add("MorphOn");
            /*0x92*/ table.Add("PlayVMD");
            /*0x93*/ table.Add("SetHotRectangles");
            /*0x94*/ table.Add("MulDiv");
            /*0x95*/ table.Add("GetSierraProfileInt");
            /*0x96*/ table.Add("GetSierraProfileString");
            /*0x97*/ table.Add("SetWindowsOption");
            /*0x98*/ table.Add("GetWindowsOption");
            /*0x99*/ table.Add("WinDLL"); // realm: KCallDLL
            if (version != Sci32Version.Realm2 && version != Sci32Version.Realm3)
            {
                /*0x9a*/ table.Add("Dummy");
                /*0x9b*/ table.Add("Minimize"); // Dummy in sci3
                /*0x9c*/ table.Add("DeletePic");
                if (sci3)
                {
                    /*0x9d*/ table.Add("Dummy");
                    /*0x9e*/ table.Add("WebConnect");
                    /*0x9f*/ table.Add("Dummy");
                    /*0xa0*/ table.Add("PlayDuck");
                    /*0xa1*/ table.Add("WinExec");
                    /*0xa2*/ table.Add("ThreadLocal");
                }
            }
            else
            {
                // Realm
                /*0x9a*/ table.Add("KLong");
                /*0x9b*/ table.Add("KIsKindOf");
                /*0x9c*/ table.Add("KPackData");
                /*0x9d*/ table.Add("KArgList");
                /*0x9e*/ table.Add("KVerbQueue");
            }
            return table.ToArray();
        }

        static bool HasObject(Game game, int scriptNumber, string objectName)
        {
            return game.Scripts.Where(s => s.Number == scriptNumber)
                               .SelectMany(s => s.Objects)
                               .Any(o => o.Name == objectName);
        }

        static bool HasMethod(Game game, int scriptNumber, string methodName)
        {
            // trying this out as an alternative to a FirstOrDefault on Scripts
            return game.Scripts.Where(s => s.Number == scriptNumber)
                               .SelectMany(s => s.Objects)
                               .Any(o => o.Methods.Any(m => m.Name == methodName));
        }

        // scans all functions in a script for a callk with the function number.
        // this is so i can do heuristics that only depend on script number and function number,
        // and not object names or selector names.
        static bool IsKernelFunctionCalled(Game game, int scriptNumber, int kernelFunction)
        {
            // trying this out as an alternative to a FirstOrDefault on Scripts
            return game.Scripts.Where(s => s.Number == scriptNumber)
                                        .SelectMany(s => s.Functions)
                                        .Any(f => IsKernelFunctionCalled(f, kernelFunction));

        }

        static bool IsKernelFunctionCalled(Function function, int kernelFunction)
        {
            var parser = new ByteCodeParser(function);
            while (parser.Next())
            {
                if (parser.Operation == Operation.callk &&
                    parser.GetOperand(0) == kernelFunction)
                {
                    return true;
                }
            }
            return false;
        }

        enum Sci32Version
        {
            Unknown,
            Sci2,
            Sci21_Early,
            Sci21_Middle,
            Sci21_Late,
            Sci3,
            Realm2,
            Realm3,
        }

        // okay i fucked up here because while some of this is how ScummVM determines game-level SCI version,
        // it lumps 2.1 early/mid/late together and runs ANOTHER HEURISTIC against Sound:play to see what
        // function number kDoSound is.
        static Sci32Version DetectSci32Version(Game game)
        {
            if (game.ByteCodeVersion == ByteCodeVersion.SCI2)
            {
                // if Obj is in script 60000 then this is Realm
                if (game.Scripts.Any(s => s.Number == 60000 && s.Objects.Any(o => o.Name == "Obj")))
                {
                    // Sound script calls kDoSound [ kSound? ] in realm3, not in realm2
                    if (!IsKernelFunctionCalled(game, 60012, 0x75))
                    {
                        return Sci32Version.Realm2;
                    }
                    else
                    {
                        return Sci32Version.Realm3;
                    }
                }

                if (game.Selectors.Contains("wordFail")) // ScummVM heuristic
                {
                    // ScummVM uses resource.map format to distinguish 2 from 2.1 early.
                    // I'm think Obj:scratch could be a good one!
                    if (!game.Selectors.Contains("scratch"))
                    {
                        return Sci32Version.Sci2;
                    }
                    else
                    {
                        return Sci32Version.Sci21_Early;
                    }
                }
                else
                {
                    // UPDATE: i don't think this matters for the kernel table,
                    //         but i think it does for subops: kString_subops
                    //
                    // ScummVM heuristic (but with a better explanation):
                    // Str:format calls kString (0x5c) in Late, not in middle.
                    // Str script does not exist in lighthouse non-interactive demo.
                    var strFormat = game.Scripts.Where(s => s.Number == 64918)
                                                .SelectMany(s => s.Functions)
                                                .FirstOrDefault(f => f.FullName == "Str:format");
                    if (strFormat == null || !IsKernelFunctionCalled(strFormat, 0x5c))
                    {
                        return Sci32Version.Sci21_Middle;
                    }
                    else
                    {
                        return Sci32Version.Sci21_Late;
                    }
                }
            }
            else if (game.ByteCodeVersion == ByteCodeVersion.SCI3)
            {
                return Sci32Version.Sci3;
            }
            else
            {
                throw new Exception("not Sci32");
            }
        }

        // LSCI should hopefully be easy since it was only used for INN
        static string[] BuildLsci()
        {
            var table = new List<string>(0x5c);
            /* INTERP.KRN */		
            /*0x00*/ table.Add("IsObject");
            /*0x01*/ table.Add("DisposeClone");
            /*0x02*/ table.Add("Clone");
			/*0x03*/ table.Add("RespondsTo");
			/*0x04*/ table.Add("KArray");
			/*0x05*/ table.Add("KBlock");
			/*0x06*/ table.Add("KList");
			/*0x07*/ table.Add("ModuleDispose");
			/*0x08*/ table.Add("ModuleID");
			/*0x09*/ table.Add("Resource");
			/*0x0a*/ table.Add("KSeq");
			/*0x0b*/ table.Add("KString");
			/*0x0c*/ table.Add("GetFarText");
			/*0x0d*/ table.Add("CoordPri");
			/*0x0e*/ table.Add("DrawPic");
			/*0x0f*/ table.Add("PicState");
			/*0x10*/ table.Add("ShakeScreen");
			/*0x11*/ table.Add("Show");
			/*0x12*/ table.Add("AddToPic");
			/*0x13*/ table.Add("Animate");
			/*0x14*/ table.Add("CelHigh");
			/*0x15*/ table.Add("CelRect");
			/*0x16*/ table.Add("CelWide");
			/*0x17*/ table.Add("DrawCel");
			/*0x18*/ table.Add("DrawScaledCel");
			/*0x19*/ table.Add("NumCels");
			/*0x1a*/ table.Add("NumLoops");
			/*0x1b*/ table.Add("ScaledCelRect");
			/*0x1c*/ table.Add("DrawControl");
			/*0x1d*/ table.Add("EditControl");
			/*0x1e*/ table.Add("HiliteControl");
			/*0x1f*/ table.Add("GetPort");
			/*0x20*/ table.Add("SetPort");
			/*0x21*/ table.Add("DisposeWindow");
			/*0x22*/ table.Add("NewWindow");
			/*0x23*/ table.Add("Display");
			/*0x24*/ table.Add("TextSize");
			/*0x25*/ table.Add("GetEvent");
            /*0x26*/ table.Add("GlobalToLocal");
            /*0x27*/ table.Add("LocalToGlobal");
            /*0x28*/ table.Add("MapKeyToDir");
            /*0x29*/ table.Add("KMenu");
			/*0x2a*/ table.Add("HaveMouse");
            /*0x2b*/ table.Add("SetCursor");
            /*0x2c*/ table.Add("Memory");
            /*0x2d*/ table.Add("CheckSaveGame");
            /*0x2e*/ table.Add("GameIsRestarting");
            /*0x2f*/ table.Add("GetSaveDir");
            /*0x30*/ table.Add("GetSaveFiles");
            /*0x31*/ table.Add("RestartGame");
            /*0x32*/ table.Add("RestoreGame");
            /*0x33*/ table.Add("SaveGame");
            /*0x34*/ table.Add("KSound");
            /*0x35*/ table.Add("GetTime");
            /*0x36*/ table.Add("SetTimerFreq");
            /*0x37*/ table.Add("Wait");
            /*0x38*/ table.Add("Alert");
            /*0x39*/ table.Add("CanBeHere");
            /*0x3a*/ table.Add("OnControl");
            /*0x3b*/ table.Add("FileSystem");
            /*0x3c*/ table.Add("DeviceInfo");
            /*0x3d*/ table.Add("Abs");
            /*0x3e*/ table.Add("CosDiv");
            /*0x3f*/ table.Add("CosMult");
            /*0x40*/ table.Add("GetAngle");
            /*0x41*/ table.Add("GetDistance");
            /*0x42*/ table.Add("Random");
            /*0x43*/ table.Add("SinDiv");
            /*0x44*/ table.Add("SinMult");
            /*0x45*/ table.Add("Sqrt");
            /*0x46*/ table.Add("KLong");
            /*0x47*/ table.Add("Graph");
            /*0x48*/ table.Add("Debug");
            /*0x49*/ table.Add("Profiler");
            /*0x4a*/ table.Add("SetDebug");
            /*0x4b*/ table.Add("ShowFree");
            /*0x4c*/ table.Add("StackUsage");
            /*0x4d*/ table.Add("Joystick");
            /*0x4e*/ table.Add("BaseSetter");
            /*0x4f*/ table.Add("ConfigStr");
            /*0x50*/ table.Add("RedHerring1");
            /*0x51*/ table.Add("RedHerring2");
            /*0x52*/ table.Add("Encrypt");
            /*0x53*/ table.Add("Decrypt");
            /* TSN.KRN */
            /*0x54*/ table.Add("TSN");
            /*0x55*/ table.Add("ObjPropOffset");
            /*0x56*/ table.Add("ObjOffsetProp");
            /*0x57*/ table.Add("SID");
            /*0x58*/ table.Add("InvokeMethod");
            /* GRAPH.KRN */
            /*0x59*/ table.Add("Palette");
            /* NL.KRN */
            /*0x5a*/ table.Add("Said");
            /*0x5b*/ table.Add("Parse");
            /*0x5c*/ table.Add("SetSynonyms");
            return table.ToArray();
        }
    }
}
