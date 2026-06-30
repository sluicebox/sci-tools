using System.Collections.Generic;
using System.Linq;
using SCI.Language;

namespace SCI.Annotators
{
    // Renames globals in the 0-99 range.

    static class SystemGlobalRenamer
    {
        public static IReadOnlyDictionary<int, string> Run(Game game, bool sci32)
        {
            IReadOnlyDictionary<int, string> globals;
            if (!sci32)
            {
                // SCI16 globals bounce around
                globals = BuildSystemGlobals16(game);
            }
            else
            {
                if (game.GetExport(0, 0) != "RoomZero")
                {
                    // SCI32 globals are constant as far as I can tell
                    globals = systemGlobals32;
                }
                else
                {
                    // the realm
                    globals = systemGlobalsLsci32;
                }
            }

            // rename
            GlobalRenamer.Run(game, globals);
            return globals;
        }

        static IReadOnlyDictionary<int, string> BuildSystemGlobals16(Game game)
        {
            var globals = new Dictionary<int, string>(baseGlobals16);

            // The number of system variables grows and moves around, but the first
            // important global that changes is #27. I'm using that as the landmark,
            // so if I can figure it out then I know which set of globals to use.
            // The reality is messier than that, but only for globals I don't care
            // about, so I've excluded the messy ones from the globals.
            // Fan games have a different layout, so global 27 is unknown for them.

            Result is27Volume = IsGlobal27Volume(game);
            if (is27Volume == Result.Yes)
            {
                // Early SCI16 game. global #3 is speed.
                globals.Add(3, "gSpeed");
                foreach (var kv in nextEarly16)
                {
                    globals.Add(kv.Key, kv.Value);
                }
            }
            else if (is27Volume == Result.No)
            {
                // Mid-Late SCI16 game. global #3 is still speed in a few
                // of these, then becomes unused. If it's initialized then
                // rename it.
                if (GetGlobalType(game, 3) == GlobalType.Number)
                {
                    globals.Add(3, "gSpeed");
                }
                foreach (var kv in nextMid16)
                {
                    globals.Add(kv.Key, kv.Value);
                }
            }

            return globals;
        }

        static Dictionary<int, string> baseGlobals16 = new Dictionary<int, string>
        {
            { 0,  "gEgo" },
            { 1,  "gGame" },
            { 2,  "gCurRoom" },
            // 3 gSpeed, then unused
            { 4,  "gQuit" },
            { 5,  "gCast" },
            { 6,  "gRegions" },
            { 7,  "gTimers" },
            { 8,  "gSounds" },
            { 9,  "gInventory" },
            { 10, "gAddToPics" },
            { 11, "gCurRoomNum" },
            { 12, "gPrevRoomNum" },
            { 13, "gNewRoomNum" },
            { 14, "gDebugOn" },
            { 15, "gScore" },
            { 16, "gPossibleScore" },
            // 17-18 changes
            //{ 17, "gTextCode" },
            //{ 18, "gCuees" },
            { 19, "gTheCursor" },
            { 20, "gNormalCursor" },
            { 21, "gWaitCursor" },
            { 22, "gUserFont" },
            { 23, "gSmallFont" },
            { 24, "gLastEvent" },
            { 25, "gModelessDialog" },
            { 26, "gBigFont" },
        };

        static IReadOnlyDictionary<int, string> nextEarly16 = new Dictionary<int, string>
        {
            { 17, "gShowStyle" }, // continues on, but safe to do here
            { 18, "gAniInterval" }, // continues on, but safe to do here

            { 27, "gVolume" },
            { 28, "gVersion" },
            { 29, "gLocales" },
            { 30, "gCurSaveDir" },

            { 50, "gAniThreshold" },
            { 51, "gPerspective" },
            { 52, "gFeatures" },
            { 53, "gSortedFeatures" },
            // different values
            //{ 54, "gSortedCast" },
            //{ 55, "gDeleteCastMember" },
            //{ 56, "gSkipFactor" },
        };

        static IReadOnlyDictionary<int, string> nextMid16 = new Dictionary<int, string>
        {
            { 27, "gVersion" },
            { 28, "gLocales" },
            { 29, "gCurSaveDir" },
            //{ 30, "aniThreshold" }, // later unused
            { 31, "gPerspective" },
            { 32, "gFeatures" },
            //{ 33, "gSortedFeatures" }, // later unused
            { 34, "gUseSortedFeatures" },
            //{ 35, "gEgoBlindSpot" }, // later unused
            { 36, "gOverlays" },
            { 37, "gDoMotionCue" },
            { 38, "gSystemWindow" },
            //{ 39, "gDemoDialogTime" }, /. later unused
            //{ 40, "gCurrentPalette" }, // later unused
            { 41, "gModelessPort" },
            { 42, "gSysLogPath" },

            //{ 62, "gEndSysLogPath" },
            { 63, "gGameControls" },
            { 64, "gFtrInitializer" },
            { 65, "gDoVerbCode" },
            //{ 66, "gFirstSaidHandler" }, later gApproachCode
            { 67, "gUseObstacles" },
            //{ 68, "gTheMenuBar" }, // later unused
            { 69, "gTheIconBar" },
            { 70, "gMouseX" },
            { 71, "gMouseY" },
            { 72, "gKeyDownHandler" },
            { 73, "gMouseDownHandler" },
            { 74, "gDirectionHandler" },
            //{ 75, "gGameCursor" }, // later gSpeechHandler
            { 76, "gLastVolume" },
            { 77, "gPMouse" },
            { 78, "gTheDoits" },
            { 79, "gEatMice" },
            { 80, "gUser" },
            { 81, "gSyncBias" },
            { 82, "gTheSync" },
            //{ 83, "gCDAudio" }, // later unused
            { 84, "gFastCast" },
            { 85, "gInputFont" },
            { 86, "gTickOffset" },
            { 87, "gHowFast" },
            { 88, "gGameTime" },

            { 89, "gNarrator" },
            { 90, "gMsgType" },
            { 91, "gMessager" },
            { 92, "gPrints" },
            { 93, "gWalkHandler" },
            { 94, "gTextSpeed" },
            //{ 95, "gAltPolyList" },
        };

        static IReadOnlyDictionary<int, string> systemGlobals32 = new Dictionary<int, string>
        {
            { 0, "gEgo" },
            { 1, "gGame" },
            { 2, "gCurRoom" },
            { 3, "gThePlane" },
            { 4, "gQuit" },
            { 5, "gCast" },
            { 6, "gRegions" },
            { 7, "gTimers" },
            { 8, "gSounds" },
            { 9, "gInventory" },
            { 10, "gPlanes" },
            { 11, "gCurRoomNum" },
            { 12, "gPrevRoomNum" },
            { 13, "gNewRoomNum" },
            { 14, "gDebugOn" },
            { 15, "gScore" },
            { 16, "gPossibleScore" },
            { 17, "gTextCode" },
            { 18, "gCuees" },
            { 19, "gTheCursor" },
            { 20, "gNormalCursor" },
            { 21, "gWaitCursor" },
            { 22, "gUserFont" },
            { 23, "gSmallFont" },
            { 24, "gLastEvent" },
            { 25, "gEventMask" },
            { 26, "gBigFont" },
            { 27, "gVersion" },
            { 28, "gAutoRobot" },
            { 29, "gCurSaveDir" },
            { 30, "gNumCD" },
            { 31, "gPerspective" },
            { 32, "gFeatures" },
            { 33, "gPanels" },
            { 34, "gUseSortedFeatures" },
            // 35 unused
            { 36, "gOverlays" },
            { 37, "gDoMotionCue" },
            { 38, "gSystemPlane" },
            { 39, "gSaveFileSelText" },
            // 40 unused
            // 41 unused
            { 42, "gSysLogPath" },
            { 62, "gEndSysLogPath" },
            { 63, "gGameControls" },
            { 64, "gFtrInitializer" },
            { 65, "gDoVerbCode" },
            { 66, "gApproachCode" },
            { 67, "gUseObstacles" },
            // 68 unused
            { 69, "gTheIconBar" },
            { 70, "gMouseX" },
            { 71, "gMouseY" },
            { 72, "gKeyDownHandler" },
            { 73, "gMouseDownHandler" },
            { 74, "gDirectionHandler" },
            { 75, "gSpeechHandler" },
            { 76, "gLastVolume" },
            { 77, "gPMouse" },
            { 78, "gTheDoits" },
            { 79, "gEatMice" },
            { 80, "gUser" },
            { 81, "gSyncBias" },
            { 82, "gTheSync" },
            { 83, "gExtMouseHandler" },
            { 84, "gTalkers" },
            { 85, "gInputFont" },
            { 86, "gTickOffset" },
            { 87, "gHowFast" },
            { 88, "gGameTime" },
            { 89, "gNarrator" },
            { 90, "gMsgType" },
            { 91, "gMessager" },
            { 92, "gPrints" },
            { 93, "gWalkHandler" },
            { 94, "gTextSpeed" },
            { 95, "gAltPolyList" },
            { 96, "gScreenWidth" },
            { 97, "gScreenHeight" },
            { 98, "gLastScreenX" },
            { 99, "gLastScreenY" },
        };

        // the realm
        static IReadOnlyDictionary<int, string> systemGlobalsLsci32 = new Dictionary<int, string>
        {
            { 1, "gEgo" },
            { 2, "gGame" },
            { 3, "gCurRoom" },
            { 6, "gTheIconBar" },
            { 7, "gSystemDialog" },
            { 8, "gSystemWindow" },
            { 9, "gSystemButton" },
            { 10, "gSystemPlane" },
            { 11, "gTheCursor" },
            { 12, "gNormalCursor" },
            { 13, "gWaitCursor" },
            { 14, "gUser" },
            { 15, "gTheMenuBar" },
            { 16, "gStatusLine" },
            { 17, "gAltKeyTable" },
            { 18, "gNetMsgProcessor" },
            { 19, "gDialogHandler" },
            { 20, "gMessager" },
            { 21, "gNarrator" },
            { 22, "gTheSync" },
            { 26, "gOptionsMenu" },
            { 31, "gRegions" },
            { 36, "gCheckPlaneEdge" },
            { 37, "gPlanes" },
            { 39, "gFirstEventHandler" },
            { 40, "gInDialogHandler" },
            { 41, "gWalkHandler" },
            { 43, "gApproachCode" },
            { 44, "gDoVerbCode" },
            { 45, "gFtrInitializer" },
            { 46, "gAltPolyList" },
            { 47, "gNetMsgHandler" },
            { 48, "gRoomMgr" },
            { 50, "gQuit" },
            { 52, "gCurRoomNum" },
            { 53, "gPrevRoomNum" },
            { 54, "gNewRoomNum" },
            { 57, "gShowStyle" },
            { 58, "gUserFont" },
            { 59, "gSmallFont" },
            { 60, "gBigFont" },
            { 61, "gVolume" },
            { 62, "gVersion" },
            { 63, "gPerspective" },
            { 64, "gGameTime" },
            { 65, "gTickOffset" },
            { 66, "gScrnShowCount" },
            { 68, "gCurVerbItem" },
            { 69, "gMessageType" },
            { 70, "gUseKeyHighlight" },
            { 72, "gCurrentPalette" },
            { 73, "gUseSortedFeatures" },
            { 74, "gDebugging" },
            { 75, "gUseObstacles" },
            { 76, "gSyncBias" },
            { 77, "gNullEvtHandler" },
            { 78, "gFancyWindow" },
            { 79, "gProcessMessages" },
            { 84, "gBlack" },
            { 85, "gWhite" },
            { 86, "gBlue" },
            { 87, "gGreen" },
            { 88, "gCyan" },
            { 89, "gRed" },
            { 90, "gMagenta" },
            { 91, "gBrown" },
            { 92, "gLtGrey" },
            { 93, "gGrey" },
            { 94, "gLtBlue" },
            { 95, "gLtGreen" },
            { 96, "gLtCyan" },
            { 97, "gLtRed" },
            { 98, "gLtMagenta" },
            { 99, "gYellow" },
            { 100, "gSkipFrame" },
            { 101, "gCuees" },
            { 102, "gDeadBitmaps" },
            { 103, "gMaxTextWidth" },
            { 104, "gDialogListID" },
            { 105, "gDeadModuleList" },
        };

        enum Result { Unknown, Yes, No }

        static Result IsGlobal27Volume(Game game)
        {
            // 27 starts out as volume, then it gets removed and others shift down.
            // First: 27 volume,  28 version, 29 ......., 30 save-dir
            // Then:  27 version, 28 ......., 29 save-dir
            GlobalType type27 = GetGlobalType(game, 27);
            if (type27 == GlobalType.Number)
            {
                // 27 is a number, it's volume
                return Result.Yes;
            }
            if (type27 == GlobalType.String)
            {
                // 27 is a string, it's version
                return Result.No;
            }
            GlobalType type28 = GetGlobalType(game, 28);
            if (type28 == GlobalType.String)
            {
                // 28 is a string, 27 must be volume
                return Result.Yes;
            }
            int curSaveDir = GetCurSaveDirGlobal(game);
            if (curSaveDir == 30)
            {
                // 30 is curSaveDir, 27 must be volume
                return Result.Yes;
            }
            if (curSaveDir == 29)
            {
                // 29 is curSaveDir, 27 must be version
                return Result.No;
            }
            int version = GetVersionGlobal(game);
            if (version == 28)
            {
                // 28 is version, 27 must be volume
                return Result.Yes;
            }
            if (version == 27)
            {
                // 27 is version
                return Result.No;
            }

            Log.Warn(game, "Can't figure out global 27, probably a fan game");
            return Result.Unknown;
        }

        enum GlobalType { Unknown, Number, String }

        // attempt to get a global's type by looking at its heap value
        // and any assignments made to it in script 0.
        static GlobalType GetGlobalType(Game game, int globalNumber)
        {
            var global = game.GetGlobal(globalNumber);
            if (global == null) return GlobalType.Unknown;

            if (global.Value is int)
            {
                if (((int)global.Value) != 0)
                {
                    return GlobalType.Number;
                }
            }
            else if (global.Value is string)
            {
                return GlobalType.String;
            }

            string globalName = global.Name;
            foreach (var function in game.GetScript(0).GetFunctions())
            {
                foreach (var node in function.Node)
                {
                    if (node.Children.Count == 3 &&
                        node.At(1).Text == globalName &&
                        node.At(0).Text == "=")
                    {
                        if (node.At(2) is Integer)
                        {
                            return GlobalType.Number;
                        }
                        else if (node.At(2) is String)
                        {
                            return GlobalType.String;
                        }
                        else if (node.At(2) is AddressOf)
                        {
                            // only way to detect Hoyle4
                            return GlobalType.String;
                        }
                    }
                }
            }

            return GlobalType.Unknown;
        }

        // Early Game script 994 has a GetCWD call and GetDirectory call that passes gCurSaveDir.
        // Later Game script 994 has a ValidPath call instead.
        static int GetCurSaveDirGlobal(Game game)
        {
            var script = game.GetScript(994); // Game script
            if (script == null) return -1;

            foreach (var function in script.GetFunctions())
            {
                foreach (var node in function.Node)
                {
                    if ((node.At(0).Text == "GetCWD" ||
                         node.At(0).Text == "GetDirectory" ||
                         node.At(0).Text == "ValidPath") &&
                        node.At(1) is Atom)
                    {
                        string name = node.At(1).Text;
                        var global = game.Globals.Values.FirstOrDefault(g => g.Name == name);
                        if (global != null)
                        {
                            if (global.Number == 29 || global.Number == 30)
                            {
                                return global.Number;
                            }
                        }
                    }
                }
            }
            return -1;
        }

        // Game script 994 has SaveGame/RestoreGame/CheckSaveGame that pass gVersion
        static int GetVersionGlobal(Game game)
        {
            var script = game.GetScript(994); // Game script
            if (script == null) return -1;

            foreach (var function in script.GetFunctions())
            {
                foreach (var node in function.Node)
                {
                    if (node.At(0).Text == "SaveGame" &&
                        node.At(4) is Atom)
                    {
                        int globalNumber = ReadGlobal(game, node.At(4));
                        if (globalNumber != -1)
                        {
                            return globalNumber;
                        }
                    }
                    if ((node.At(0).Text == "CheckSaveGame" ||
                         node.At(0).Text == "RestoreGame") &&
                        node.At(3) is Atom)
                    {
                        int globalNumber = ReadGlobal(game, node.At(3));
                        if (globalNumber != -1)
                        {
                            return globalNumber;
                        }
                    }
                }
            }
            return -1;
        }

        static int ReadGlobal(Game game, Node node)
        {
            var global = game.Globals.Values.FirstOrDefault(g => g.Name == node.Text);
            if (global != null)
            {
                return global.Number;
            }
            return -1;
        }
    }
}
