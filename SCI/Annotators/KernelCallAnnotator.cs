using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SCI.Language;

namespace SCI.Annotators
{
    // Rename or annotate kernel call enums.
    // Annotations are used when compatible with the SCI Fan Template.
    // Otherwise, annotate the integer.

    class KernelCallAnnotator
    {
        static Dictionary<int, string> ArrayOps = new Dictionary<int, string>
        {
            {0,  "ArrayNew"},
            {1,  "ArraySize"},
            {2,  "ArrayAt"},
            {3,  "ArrayAtPut"},
            {4,  "ArrayFree"},
            {5,  "ArrayFill"},
            {6,  "ArrayCpy"},
            {7,  "ArrayCmp"},
            {8,  "ArrayDup"},
            {9,  "ArrayGetData"},
            // saw this in SCI3 interpreter source
            {10, "ArrayByteCpy"},
        };

        static Dictionary<int, string> BitmapOps = new Dictionary<int, string>
        {
            {0, "Create"},
            {1, "Dispose"},
            {2, "AddLine"},
            {3, "AddCel"},
            {4, "AddText"},
            {5, "AddRect"},
            {6, "AddBitmap"},
            {7, "InvertRect"},
            {8, "SetOrigin"},
            {9, "CreateWithCel"},
            {10, "Remap"},
            {11, "Duplicate"},
            {12, "GetColor"},
            {13, "GetThumbnail"},
            {14, "LoadBMP"},
        };

        static Dictionary<int, string> CelInfoOps = new Dictionary<int, string>
        {
            {0, "GetOrigX"},
            {1, "GetOrigY"},
            {2, "GetLinkX"},
            {3, "GetLinkY"},
            {4, "GetPixel" },
        };

        static Dictionary<int, string> CelLinkOps = new Dictionary<int, string>
        {
            {0, "GetFirstLink"},
            {1, "GetNextLink"},
            {2, "GetXLinkPoint"},
            {3, "GetYLinkPoint"},
            {4, "AddBitmapLink" },
        };

        static Dictionary<int, string> CdOps = new Dictionary<int, string>
        {
            {0, "Check"},
            {1, "GetSaveCD"},
        };

        static Dictionary<int, string> DeviceInfoOps = new Dictionary<int, string>
        {
            {0, "diGET_DEVICE"},
            {1, "diGET_CURRENT_DEVICE"},
            {2, "diPATHS_EQUAL"},
            {3, "diIS_FLOPPY"},
            {4, "*CloseDevice" },
            {5, "*SaveDevice" },
            {6, "*SaveDirMounted" },
            {7, "*MakeSaveDirName" },
            {8, "*MakeSaveFileName" },
        };

        static Dictionary<int, string> DoAudioOps16 = new Dictionary<int, string>
        {
            {1, "audWPLAY"},
            {2, "audPLAY"},
            {3, "audSTOP"},
            {4, "audPAUSE"},
            {5, "audRESUME"},
            {6, "audPOSITION"},
            {7, "audRATE"},
            {8, "audVOLUME"},
            {9, "audLANGUAGE"},
            {10, "audCD"},
        };

        static Dictionary<int, string> DoAudioOps32 = new Dictionary<int, string>
        {
            // 1-8 are the same in SCI32
            {1, "audWPLAY"},
            {2, "audPLAY"},
            {3, "audSTOP"},
            {4, "audPAUSE"},
            {5, "audRESUME"},
            {6, "audPOSITION"},
            {7, "audRATE"},
            {8, "audVOLUME"},
            // 9-21 are different in SCI32
            {9, "*AudDACFound"},
            {10, "*AudBits"},
            {11, "*AudDistort"},
            {12, "*AudMixCheck"},
            {13, "*AudChannels"},
            {14, "*AudPreload"},
            {15, "*AudFade"},
            {16, "*AudFade36"},
            {17, "*AudCheckNoise"},
            {18, "*AudDACCritical"},
            {19, "*AudLoop"},
            {20, "*AudPan"},
            {21, "*AudPanOff"},
        };

        static Dictionary<int, string> DoSoundSci0Ops = new Dictionary<int, string>
        {
            {0,  "sndINIT"},
            {1,  "sndPLAY"},
            {2,  "sndNOP"},
            {3,  "sndDISPOSE"},
            {4,  "sndSET_SOUND"},
            {5,  "sndSTOP"},
            {6,  "sndPAUSE"},
            {7,  "sndRESUME"},
            {8,  "sndVOLUME"},
            {9,  "sndUPDATE"},
            {10, "sndFADE"},
            {11, "sndCHECK_DRIVER"},
            {12, "sndSTOP_ALL"},
        };

        // Companion's template isn't aware of these, it just has the Sci11Late values.
        // Symbolize the few (0-3) that are the same in both sets, annotate the rest.
        static Dictionary<int, string> DoSoundSci11EarlyOps = new Dictionary<int, string>
        {
            {0,  "sndMASTER_VOLUME"},
            {1,  "sndSET_SOUND"},
            {2,  "sndRESTORE"},
            {3,  "sndGET_POLYPHONY"},
            {4,  "*sndUPDATE"},
            {5,  "*sndINIT"},
            {6,  "*sndDISPOSE"},
            {7,  "*sndPLAY"},
            {8,  "*sndSTOP"},
            {9,  "*sndPAUSE"},
            {10, "*sndFADE"},
            {11, "*sndUPDATE_CUES"},
            {12, "*sndSEND_MIDI"},
            {13, "*sndGLOBAL_REVERB"},
            {14, "*sndSET_HOLD"},
        };

        // Companion's template matches these
        static Dictionary<int, string> DoSoundSci11LateOps = new Dictionary<int, string>
        {
            {0,  "sndMASTER_VOLUME"},
            {1,  "sndSET_SOUND"},
            {2,  "sndRESTORE"},
            {3,  "sndGET_POLYPHONY"},
            {4,  "sndGET_AUDIO_CAPABILITY"},
            {5,  "sndSUSPEND"},
            {6,  "sndINIT"},
            {7,  "sndDISPOSE"},
            {8,  "sndPLAY"},
            {9,  "sndSTOP"},
            {10, "sndPAUSE"},
            {11, "sndFADE"},
            {12, "sndSET_HOLD"},
            //{13, "sndDUMMY"},
            {13, "*Mute"}, // from 1991 system scripts
            {14, "sndSET_VOLUME"},
            {15, "sndSET_PRIORITY"},
            {16, "sndSET_LOOP"},
            {17, "sndUPDATE_CUES"},
            {18, "sndSEND_MIDI"},
            {19, "sndGLOBAL_REVERB"},
            {20, "sndUPDATE"},
        };

        // Some SCI32 Mac games have different enums because they deleted unsupported subops
        static Dictionary<int, string> DoSoundSci2MacOps = new Dictionary<int, string>
        {
            {0,  "sndMASTER_VOLUME"}, // only one that's the same
            {1,  "*sndGET_AUDIO_CAPABILITY"},
            {2,  "*sndINIT"},
            {3,  "*sndDISPOSE"},
            {4,  "*sndPLAY"},
            {5,  "*sndSTOP"},
            {6,  "*sndPAUSE"},
            {7,  "*sndFADE"},
            {8,  "*sndSET_VOLUME"},
            {9,  "*sndSET_LOOP"},
            {10, "*sndUPDATE_CUES"},
            // pqswat, hoyle5 solitaire
            {12, "*sndRESTORE"},
            {13, "*sndGET_POLYPHONY"},
            {14, "*sndSUSPEND"},
            {15, "*sndSET_HOLD"},
            {17, "*sndSET_PRIORITY"},
            {18, "*sndSEND_MIDI"},
        };

        static Dictionary<int, string> DoSyncOps = new Dictionary<int, string>
        {
            {0, "syncSTART"},
            {1, "syncNEXT"},
            {2, "syncSTOP"},
        };

        static Dictionary<int, string> FileIoOps = new Dictionary<int, string>
        {
            {0, "fiOPEN"},
            {1, "fiCLOSE"},
            {2, "fiREAD"},
            {3, "fiWRITE"},
            {4, "fiUNLINK"},
            {5, "fiREAD_STRING"},
            {6, "fiWRITE_STRING"},
            {7, "fiSEEK"},
            {8, "fiFIND_FIRST"},
            {9, "fiFIND_NEXT"},
            {10, "fiEXISTS"},
            {11, "fiRENAME"},
            {12, "*Copy"},
            {13, "*GetByte"},
            {14, "*PutByte"},
            {15, "*GetWord"},
            {16, "*PutWord"},
            {17, "*CheckFreeSpace"},
            {18, "*GetCWD"},
            {19, "*ValidPath"},
        };

        static Dictionary<int, string> FontOps = new Dictionary<int, string>
        {
            {0, "PointSize" },
            {1, "SetFontRes" },
        };

        static Dictionary<int, string> GetTimeOps = new Dictionary<int, string>
        {
            // i don't like anybody's names
            {0, "Ticks" },
            {1, "SysTime12" },
            {2, "SysTime24" },
            {3, "SysDate" },
        };

        static Dictionary<int, string> GraphOps = new Dictionary<int, string>
        {
            {2, "grGET_COLOURS"},
            {4, "grDRAW_LINE"},
            {7, "grSAVE_BOX"},
            {8, "grRESTORE_BOX"},
            {9, "grFILL_BOX_BACKGROUND"},
            {10, "grFILL_BOX_FOREGROUND"},
            {11, "grFILL_BOX"},
            {12, "grUPDATE_BOX"},
            {13, "grREDRAW_BOX"},
            {14, "grADJUST_PRIORITY"},
            {15, "*grSAVE_BITS_HIGH_RES_KQ6" }, // not in template
        };

        static Dictionary<int, string> ListOps = new Dictionary<int, string>
        {
            {0, "New"},
            {1, "Dispose"},
            {2, "NewNode"},
            {3, "FirstNode"},
            {4, "LastNode"},
            {5, "Empty"},
            {6, "NextNode"},
            {7, "PrevNode"},
            {8, "NodeValue"},
            {9, "AddAfter"},
            {10, "AddToFront"},
            {11, "AddToEnd"},
            {12, "AddBefore"},
            {13, "MoveToFront"},
            {14, "MoveToEnd"},
            {15, "FindKey"},
            {16, "DeleteKey"},
            {17, "At"},
            {18, "IndexOf"},
            {19, "EachElementDo"},
            {20, "FirstTrue"},
            {21, "AllTrue"},
            {22, "Sort"},
        };

        static Dictionary<int, string> MemoryInfoOps16 = new Dictionary<int, string>
        {
            {0, "*LargestPtr"}, // template has these reversed
            {1, "*FreeHeap"},   // template has these reversed
            {2, "miLARGESTHUNK"},
            {3, "miFREEHUNK"},
            {4, "*TotalHunk" }, // not in template
        };

        // deliberately omitting kMemoryInfo sci32 because it's confusing and barely used.
        // only subop 0 seems to have been allowed, and it looks like it changed meanings
        // between sci2.1 and sci3.

        static Dictionary<int, string> MemoryOps = new Dictionary<int, string>
        {
            {1, "memALLOC_CRIT"},
            {2, "memALLOC_NONCRIT"},
            {3, "memFREE"},
            {4, "memCOPY"},
            {5, "memPEEK"},
            {6, "memPOKE"},
        };

        static Dictionary<int, string> MemorySegmentOps = new Dictionary<int, string>
        {
            {0, "MS_SAVE_FROM"},
            {1, "MS_RESTORE_TO"},
        };

        static Dictionary<int, string> MessageOps16 = new Dictionary<int, string>
        {
            {0, "msgGET"},
            {1, "msgNEXT"},
            {2, "msgSIZE"},
            {3, "msgREF_NOUN"},
            {4, "msgREF_VERB"},
            {5, "msgREF_COND"},
            {6, "msgPUSH"},
            {7, "msgPOP"},
            {8, "msgLAST_MESSAGE"},
        };

        static Dictionary<int, string> MessageOps32 = new Dictionary<int, string>
        {
            {0, "msgGET"},
            {1, "msgNEXT"},
            {2, "msgSIZE"},
            {4, "*msgREF_NOUN"},
            {5, "*msgREF_VERB"},
            {6, "*msgREF_COND"},
            {7, "*msgPUSH"},
            {8, "*msgPOP"},
            {9, "*msgLAST_MESSAGE"},
        };

        static Dictionary<int, string> OnControlFlags = new Dictionary<int, string>
        {
            // template has ocVISUAL, ocPRIORITY, ocSPECIAL, and i don't like the name ocSPECIAL.
            // but... template also has VISIAUL, PRIORITY, CONTROL with same values. use those!
            {1, "VISUAL"},
            {2, "PRIORITY"},
            {4, "CONTROL"},
        };

        static Dictionary<int, string> PalCycleOps = new Dictionary<int, string>
        {
            {0, "Start"},
            {1, "DoCycle"},
            {2, "Pause"},
            {3, "Go"},
            {4, "Off"},
        };

        static Dictionary<int, string> PaletteOps16 = new Dictionary<int, string>
        {
            {1, "palSET_FROM_RESOURCE"},
            {2, "palSET_FLAG"},
            {3, "palUNSET_FLAG"},
            {4, "palSET_INTENSITY"},
            {5, "palFIND_COLOR"},
            {6, "palANIMATE"},
            {7, "palSAVE"},
            {8, "palRESTORE"},
        };

        static Dictionary<int, string> PaletteOps32 = new Dictionary<int, string>
        {
            {1, "PalLoad"},
            {2, "PalIntensity"},
            {3, "PalMatch"},
            {4, "PalSetGamma"},
        };

        static Dictionary<int, string> PalVaryOps16 = new Dictionary<int, string>
        {
            {0, "pvINIT"},
            {1, "pvREVERSE"},
            {2, "pvGET_CURRENT_STEP"},
            {3, "pvUNINIT"},
            {4, "pvCHANGE_TARGET"},
            {5, "pvCHANGE_TICKS"},
            {6, "pvPAUSE_RESUME"},
        };

        static Dictionary<int, string> PalVaryOps32 = new Dictionary<int, string>
        {
            {0, "PalVaryStart"},
            {1, "PalVaryReverse"},
            {2, "PalVaryInfo"},
            {3, "PalVaryKill"},
            {4, "PalVaryTarget"},
            {5, "PalVaryNewTime"},
            {6, "PalVaryPause"},
            {7, "PalVaryNewTarget"},
            {8, "PalVaryNewSource"},
            {9, "PalVaryMergeSource"},
        };

        static Dictionary<int, string> PlayDuckOps = new Dictionary<int, string>
        {
            {0, "SetPaletteRange"},
            {1, "Play"},
            {2, "DoFrameOut"},
            {3, "Open"},
            {4, "PlayFor"},
            {5, "Close"},
            {6, "Volume"},
            {7, "PlayTo"},
        };

        static Dictionary<int, string> PlayVMDOps = new Dictionary<int, string>
        {
            {0, "Open"},
            {1, "Put"},
            {2, "Play"},
            {3, "Stop"},
            {4, "Pause"},
            {5, "Resume"},
            {6, "Close"},
            {7, "SetPalette"},
            {8, "GetLength"},
            {9, "GetPosition"},
            {10, "GetStatus"},
            {11, "Cue"},
            {12, "Seek"},
            {13, "GetSkippedFrames"},
            {14, "WaitEvent"},
            {15, "PutDoubled"},
            {16, "ShowCursor"},
            {17, "StartBlob"},
            {18, "StopBlobs"},
            {19, "AddBlob"},
            {20, "DeleteBlob"},
            {21, "Black"},
            {22, "Skip"},
            {23, "RestrictPalette"},
            {27, "SetPlane"},
            {28, "SetPreload"},
            {31, "SetFrameRate"},
        };

        static Dictionary<int, string> RemapColorsOps = new Dictionary<int, string>
        {
            {0, "Off"},
            {1, "ByRange"},
            {2, "ByPercent"},
            {3, "ToGray"},
            {4, "ToPercentGray"},
            {5, "SetGlobalNoMatchRange"},
        };

        static Dictionary<int, string> RobotOps = new Dictionary<int, string>
        {
            {0, "Open"},
            {1, "DisplayFrame"},
            {2, "FrameInfo"},
            {3, "SaveOffset"},
            {4, "Play"},
            {5, "HasEnded"},
            {6, "Exists"},
            {7, "Terminate"},
            {8, "GetCue"},
            {9, "IsPaused"},
            {10, "Pause"},
            {11, "FrameNum"},
            {12, "SetPriority"},
        };

        static Dictionary<int, string> SaveOps = new Dictionary<int, string>
        {
            {0, "SaveGame"},
            {1, "RestoreGame"},
            {2, "GetSaveDir"},
            {3, "CheckSaveGame"},
            {4, "GetSaveCDisc"},
            {5, "GetSaveFiles"},
            {6, "MakeSaveCatName"},
            {7, "MakeSaveFileName"},
            {8, "GameIsRestarting"},
            {9, "Restart"},
        };

        static Dictionary<int, string> ScrollWindowOps = new Dictionary<int, string>
        {
            {0, "ScrollCreate"},
            {1, "ScrollAdd"},
            {2, "ScrollClear"},
            {3, "ScrollPageUp"},
            {4, "ScrollPageDown"},
            {5, "ScrollUpArrow"},
            {6, "ScrollDownArrow"},
            {7, "ScrollHome"},
            {8, "ScrollEnd"},
            {9, "ScrollResize"},
            {10, "ScrollWhere"},
            {11, "ScrollGo"},
            {12, "ScrollInsert"},
            {13, "ScrollDelete"},
            {14, "ScrollModify"},
            {15, "ScrollHide"},
            {16, "ScrollShow"},
            {17, "ScrollDestroy"},
            {18, "ScrollText"},
            {19, "ScrollReconstruct"},
        };

        static Dictionary<int, string> ShakeScreenFlags = new Dictionary<int, string>
        {
            {1, "ssUPDOWN"},
            {2, "ssLEFTRIGHT"},
            {3, "ssFULL_SHAKE"},
        };

        static Dictionary<int, string> ShowMovie32Ops = new Dictionary<int, string>
        {
            {0, "Open"},
            {1, "Put"},
            {2, "Play"},
            {3, "Stop"},
            {4, "Pause"},
            {5, "Resume"},
            {6, "Close"},
            {7, "SetPalette"},
            {8, "GetLength"},
            {9, "GetPosition"},
            {10, "GetStatus"},
            {11, "Cue"},
            {12, "Seek"},
            {13, "GetSkippedFrames"},
            {14, "WaitEvent"},
        };

        static Dictionary<int, string> StringEarlyOps = new Dictionary<int, string>
        {
            {0,  "StrNew"},
            {1,  "StrSize"},
            {2,  "StrAt"},
            {3,  "StrAtPut"},
            {4,  "StrFree"},
            {5,  "StrFill"},
            {6,  "StrCpy"},
            {7,  "StrCmp"},
            {8,  "StrDup"},
            {9,  "StrGetData"},
            {10, "StrLen"},
            {11, "StrFormat"},
            {12, "StrFormatAt"},
            {13, "StrToInt"},
            {14, "StrTrim"},
            {15, "StrUpr"},
            {16, "StrLwr"},
            {17, "StrTrn"},
            {18, "StrTrnExclude"},
            {19, "StrPrint"},
        };

        static Dictionary<int, string> StringLateOps = new Dictionary<int, string>
        {
            {7,  "StrCmp"},
            {8,  "StrLen" },
            {9,  "StrFormat"},
            {10, "StrFormatAt"},
            {11, "StrToInt"},
            {12, "StrTrim"},
            {13, "StrUpr"},
            {14, "StrLwr"},
            {15, "StrTrn"},
            {16, "StrTrnExclude"},
        };

        static Dictionary<int, string> TextOps = new Dictionary<int, string>
        {
            {0, "TextSize"},
            {1, "TextWidth" },
        };

        public static void Run(Game game, bool sci32)
        {
            var doSoundVersion = DetectDoSoundVersion(game, sci32);
            var stringVersion = DetectStringVersion(game);

            foreach (var function in game.GetFunctions())
            {
                foreach (var node in function.Node)
                {
                    switch (node.At(0).Text)
                    {
                        case "KArray":
                            if (sci32)
                            {
                                Annotate(node.At(1), ArrayOps);
                            }
                            break;
                        case "Bitmap":
                            if (sci32)
                            {
                                Annotate(node.At(1), BitmapOps);
                            }
                            break;
                        case "CelInfo":
                            if (sci32)
                            {
                                Annotate(node.At(1), CelInfoOps);
                            }
                            break;
                        case "CelLink":
                            if (sci32)
                            {
                                Annotate(node.At(1), CelLinkOps);
                            }
                            break;
                        case "CD":
                            if (sci32)
                            {
                                Annotate(node.At(1), CdOps);
                            }
                            break;
                        case "DeviceInfo":
                            MakeSymbol(node.At(1), DeviceInfoOps);
                            break;
                        case "DoAudio":
                            if (!sci32)
                            {
                                MakeSymbol(node.At(1), DoAudioOps16);
                            }
                            else
                            {
                                MakeSymbol(node.At(1), DoAudioOps32);
                            }
                            break;
                        case "DoSound":
                            {
                                switch (doSoundVersion)
                                {
                                    case DoSoundVersion.Sci0:
                                        MakeSymbol(node.At(1), DoSoundSci0Ops);
                                        break;
                                    case DoSoundVersion.Sci11Early:
                                        MakeSymbol(node.At(1), DoSoundSci11EarlyOps);
                                        break;
                                    case DoSoundVersion.Sci11Late:
                                    case DoSoundVersion.Sci2:
                                        MakeSymbol(node.At(1), DoSoundSci11LateOps);
                                        break;
                                    case DoSoundVersion.Sci2Mac:
                                        MakeSymbol(node.At(1), DoSoundSci2MacOps);
                                        break;
                                }
                            }
                            break;
                        case "DoSync":
                            MakeSymbol(node.At(1), DoSyncOps);
                            break;
                        case "FileIO":
                            MakeSymbol(node.At(1), FileIoOps);
                            break;
                        case "Font":
                            if (sci32)
                            {
                                Annotate(node.At(1), FontOps);
                            }
                            break;
                        case "GetTime":
                            Annotate(node.At(1), GetTimeOps);
                            break;
                        case "Graph":
                            MakeSymbol(node.At(1), GraphOps);
                            break;
                        case "KList":
                            if (sci32)
                            {
                                Annotate(node.At(1), ListOps);
                            }
                            break;
                        case "RemapColors":
                            if (sci32)
                            {
                                Annotate(node.At(1), RemapColorsOps);
                            }
                            break;
                        case "Robot":
                            if (sci32)
                            {
                                Annotate(node.At(1), RobotOps);
                            }
                            break;
                       case "MemoryInfo":
                            if (!sci32)
                            {
                                // looks to me like they changed in sci32
                                MakeSymbol(node.At(1), MemoryInfoOps16);
                            }
                            break;
                        case "Memory":
                            MakeSymbol(node.At(1), MemoryOps);
                            break;
                        case "MemorySegment":
                            Annotate(node.At(1), MemorySegmentOps);
                            break;
                        case "Message":
                            if (!sci32)
                            {
                                MakeSymbol(node.At(1), MessageOps16);
                            }
                            else
                            {
                                MakeSymbol(node.At(1), MessageOps32);
                            }
                            break;
                        case "OnControl":
                            if (node.Children.Count != 3 &&
                                node.Children.Count != 5)
                            {
                                MakeSymbolOrFlags(node.At(1), OnControlFlags);
                            }
                            break;
                        case "PalCycle":
                            if (sci32)
                            {
                                Annotate(node.At(1), PalCycleOps);
                            }
                            break;
                        case "Palette":
                            if (!sci32)
                            {
                                MakeSymbol(node.At(1), PaletteOps16);
                            }
                            else
                            {
                                Annotate(node.At(1), PaletteOps32);
                            }
                            break;
                        case "PalVary":
                            if (!sci32)
                            {
                                MakeSymbol(node.At(1), PalVaryOps16);
                            }
                            else
                            {
                                Annotate(node.At(1), PalVaryOps32);
                            }
                            break;
                        case "PlayDuck":
                            if (sci32)
                            {
                                Annotate(node.At(1), PlayDuckOps);
                            }
                            break;
                        case "PlayVMD":
                            if (sci32)
                            {
                                Annotate(node.At(1), PlayVMDOps);
                            }
                            break;
                        case "Save":
                            if (sci32)
                            {
                                Annotate(node.At(1), SaveOps);
                            }
                            break;
                        case "ScrollWindow":
                            if (sci32)
                            {
                                Annotate(node.At(1), ScrollWindowOps);
                            }
                            break;
                        case "ShakeScreen":
                            MakeSymbolOrFlags(node.At(2), ShakeScreenFlags);
                            break;
                        case "ShowMovie":
                            if (sci32)
                            {
                                Annotate(node.At(1), ShowMovie32Ops);
                            }
                            break;
                        case "KString":
                            if (sci32)
                            {
                                if (stringVersion == StringVersion.Early)
                                {
                                    Annotate(node.At(1), StringEarlyOps);
                                }
                                else if (stringVersion == StringVersion.Late)
                                {
                                    Annotate(node.At(1), StringLateOps);
                                }
                            }
                            break;
                        case "Text":
                            if (sci32)
                            {
                                Annotate(node.At(1), TextOps);
                            }
                            break;
                    }
                }
            }
        }

        public static void MakeSymbol(Node node, IReadOnlyDictionary<int, string> map)
        {
            if (!(node is Integer)) return;

            string name;
            if (!map.TryGetValue(node.Number, out name)) return;

            if (!name.StartsWith("*"))
            {
                (node as Integer).SetDefineText(name);
            }
            else
            {
                node.Annotate(name.Substring(1));
            }
        }

        static void MakeSymbolOrFlags(Node node, IReadOnlyDictionary<int, string> map)
        {
            if (!(node is Integer)) return;

            string name;
            if (map.TryGetValue(node.Number, out name))
            {
                (node as Integer).SetDefineText(name);
            }
            else
            {
                string flagString = MakeFlagString(node.Number, map);
                if (!string.IsNullOrWhiteSpace(flagString))
                {
                    node.Annotate(flagString);
                }
            }
        }

        public static string MakeFlagString(int value, IReadOnlyDictionary<int, string> map)
        {
            List<int> flags = map.Keys.OrderByDescending(k => k).ToList();
            var output = new StringBuilder();
            foreach (int flag in flags)
            {
                // don't include the flag for zero if value isn't zero
                if (flag == 0 && value != 0) continue;

                if ((value & flag) == flag)
                {
                    if (output.Length > 0) output.Append(" | ");
                    output.Append(map[flag]);
                    value -= flag;
                }
                if (value == 0) break;
            }
            if (output.Length > 0 && value != 0)
            {
                output.Append(" | ");
                output.Append("$" + ((UInt16)value).ToString("x4"));
            }
            return output.ToString();
        }

        public static void Annotate(Node node, IReadOnlyDictionary<int, string> map)
        {
            string name;
            if (node is Integer &&
                map.TryGetValue(node.Number, out name))
            {
                node.Annotate(name);
            }
        }

        //
        // KDoSound Version Detection
        //

        enum DoSoundVersion
        {
            Unknown,
            Sci0,
            Sci11Early,
            Sci11Late,
            Sci2,
            Sci2Mac
        };

        static DoSoundVersion DetectDoSoundVersion(Game game, bool sci32)
        {
            int initSubop = GetDoSoundInit(game, sci32);
            if (!sci32)
            {
                // There are three possibilities for SCI16. For detection to fail,
                // there has to not be a Sound class, so it's a weirdo I don't
                // care too much about, so just skip this annotation.
                if (initSubop == 0)
                {
                    return DoSoundVersion.Sci0;
                }
                if (initSubop == 5)
                {
                    return DoSoundVersion.Sci11Early;
                }
                if (initSubop == 6)
                {
                    return DoSoundVersion.Sci11Late;
                }
                return DoSoundVersion.Unknown;
            }
            else
            {
                // SCI32 subops are all the same except for a few mac weirdos
                // where they deleted some of the enums, changing all the numbers.
                // There are actually two variants on this, because some of these
                // mac games have more subops implemented than others, but there
                // are no conflicts between the variants so I can use one mac set.
                // Simple test: find Sound:init's DoSound subop. 6 = normal, 2 = mac.
                if (initSubop == 2)
                {
                    return DoSoundVersion.Sci2Mac;
                }
                return DoSoundVersion.Sci2;
            }
        }

        static int GetDoSoundInit(Game game, bool sci32)
        {
            // Get Sound object, using numerous fallbacks.
            // 1. Get by name
            // 2. Get by name within expected script
            // 3. Use the first class within expected script
            Language.Object sound = null;
            var sounds = game.GetClasses().Where(c => c.Name == "Sound").ToList();
            if (sounds.Count == 1)
            {
                sound = sounds[0];
            }
            else
            {
                var soundScript = game.GetScript(sci32 ? 64989 : 989);
                if (soundScript != null)
                {
                    sound = soundScript.Classes.FirstOrDefault(c => c.Name == "Sound");
                    if (sound == null)
                    {
                        sound = soundScript.Classes.FirstOrDefault();
                    }
                }
            }
            if (sound == null) return -1;

            // If Sound:init can be found by name, get its subop
            var init = sound.Methods.FirstOrDefault(m => m.Name == "init");
            if (init != null)
            {
                return GetDoSoundSubop(init);
            }

            // If Sound:init does not exist then we are missing the init selector,
            // so gather the subops from every nameless method.
            var candidates = new HashSet<int>();
            foreach (var method in sound.Methods.Where(m => m.Name.StartsWith("sel_")))
            {
                int subop = GetDoSoundSubop(method);
                if (subop != -1)
                {
                    candidates.Add(subop);
                }
            }

            // 0 can only mean SCI0
            if (candidates.Contains(0)) return 0;

            // 2 can only mean Mac SCI32
            if (sci32 && candidates.Contains(2)) return 2;

            // it's either SCI11 Early or SCI11 Late - SCI3
            //            5        6
            // Early:     Init     Dispose
            // Late:      Suspend  Init
            //
            // Dispose and Init get called the same way,
            // so 6 is ambiguous, but 5 isn't.
            if (candidates.Contains(5)) return 5;
            if (candidates.Contains(6)) return 6;

            return -1;
        }

        static int GetDoSoundSubop(Function function)
        {
            foreach (var node in function.Node)
            {
                if (node.Children.Count == 3 &&
                    node.At(0).Text == "DoSound" &&
                    node.At(1) is Integer &&
                    node.At(2).Text == "self")
                {
                    return node.At(1).Number;
                }
            }
            return -1;
        }

        //
        // KString Version Detection
        //

        enum StringVersion { Unknown, Early, Late };

        static StringVersion DetectStringVersion(Game game)
        {
            var sizeOfStrName = game.GetExport(64918, 0);
            if (string.IsNullOrWhiteSpace(sizeOfStrName)) return StringVersion.Unknown;

            var sizeOfStr = game.GetScript(64918).Procedures.FirstOrDefault(p => p.Name == sizeOfStrName);
            if (sizeOfStr == null) return StringVersion.Unknown;

            // The first KString call passes StrLen.
            // Early: StrLen == 10
            // Late:  StrLen == 8
            foreach (var node in sizeOfStr.Node)
            {
                if (node.At(0).Text == "KString" &&
                    node.At(1) is Integer)
                {
                    if (node.At(1).Number == 10)
                    {
                        return StringVersion.Early;
                    }
                    if (node.At(1).Number == 8)
                    {
                        return StringVersion.Late;
                    }
                }
            }

            return StringVersion.Unknown;
        }
    }
}
