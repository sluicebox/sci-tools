using SCI.Resource;

// Workarounds: Skandal im Decompilirk!
//
// Prerequisite: Remove all debug instructions (file,line)
//
// There are a few specific functions in the wild that the decompiler is not
// capable of handling. They are detected here and their instructions are
// patched or flagged so that the rest of the process can happily proceed.
//
// Yes, this decompiler is hard-coded to certain functions. Skandal!
//
// There are so few workarounds that this summary is longer than the code,
// but I want to remind myself of the remaining categories:
//
// 1. Broken instructions due to compiler bugs or mistakes in hand-written
//    assembly. The accurate interpretation is to fail on these, just like they
//    fail at runtime, but I'd rather decompile them and annotate the code with
//    a comment explaining that the specific line will crash.
//
//    That said, Companion successfully decompiles the kq5-pc98 functions!
//    An impressive win for reverse instruction consumption. But I think it's
//    inaccurate to produce code that implies it works when it really doesn't.
//
// 2. One garbage instruction (possibly a compiler bug) breaks my switch
//    detection, and it's too late to rewrite it. It's one function in one
//    obscure PQ3 localization, and I didn't discover it until the very end of
//    the project. Too late, switch detection is hard and I'm not inventing a
//    new one. Sounds like a great way to fail to finish! Companion decompiles
//    this function and accurately emits the nonsense in the script.
//
// 3. For loops with continue statements are not handled by this decompiler!
//    Oof, that hurts. There are a few specific patterns that are detected and
//    handled, and some for/continues happen to decompile as other equivalent
//    control flow structures, but the decompiler does not generically detect
//    continue statements in for loops so it's likely to fail on most of them.
//
//    This was supposed to be my final boss, but when I got to the end, nobody
//    showed up. There were only two of these functions in all of SCI. TWO?!?!
//    "I think we're done here." A few minutes later I had my 100% decompiler.
//    (Long after the release, I added LSCI support and found a third function
//    in an INN script.)
//
//    I was looking forward to solving this! I'd been thinking about it and knew
//    it would be hard, but I figured once I had the graphs and could study all
//    their loops I'd eventually come up with something.
//
//    But I can't develop a difficult control flow algorithm, possibly the most
//    difficult, against a sample size of three. It would be a waste of time,
//    because I already know the answers. It also wouldn't work, because
//    anything can handle three cases, so it would effectively be untested and
//    likely incorrect. And it would probably be a ton of code! Maybe someday
//    I'll crack and generate a bunch of for/continue scripts, run them through
//    SC.EXE, and cosplay as a decompiler developer again. Until then, 100%.

namespace SCI.Decompile
{
    static class Workarounds
    {
        public static void Patch(InstructionList il)
        {
            var f = il.Function;

            // pq3 german amiga script bug
            if (f.Script.Number == 53 &&
                f.Script.Game.Id == "pq3" &&
                f.FullName == "floor:doVerb" &&
                il.Has(9) &&
                il[9].Operation == Operation.pushi &&
                il[9].Parameters[0] == 0x0035)
            {
                // garbage instruction leaks a stack item in the
                // first case of a switch, screws up my switch detection.
                // this game version was discovered at the very end of
                // the decompiler project, and switch detection was written
                // early on in Asheville when i thought i could count on
                // the stack balancing.
                il[9].Operation = Operation.ldi;
            }

            // kq5 japanese compiler bug, crashes game
            if (f.Script.Number == 216 &&
                f.Script.Game.Id == "KQ5" &&
                (f.FullName == "fireRing:handleEvent" ||
                 f.FullName == "fire:handleEvent") &&
                il.Has(0x73) && il.Has(0x7b) &&
                il[0x73].Operation == Operation.pushi &&
                il[0x7b].Parameters[0] == 0x00d8)
            {
                il[0x73].Operation = Operation.ldi;
                il[0x7b].Operation = Operation.ldi;
                il[0x43].Operation = Operation.ldi;
                il[0x4b].Operation = Operation.ldi;

                il[0x51].Flags |= InstructionFlag.CompilerBug;
                il[0x81].Flags |= InstructionFlag.CompilerBug;
            }

            // "Temporary" Hack (maybe): For loop with a continue; breaks the graph.
            // Only this function and NewHandlerList:handleEvent are problematic for-continues,
            // making them The Final Two functions I can't decompile. Screw that, just tag
            // the continue and tell the later phases to make this a For loop.
            if (f.Script.Number == 293 &&
                f.Script.Game.Id == "Brain" &&
                f.FullName == "weightsPuzzle:buyClue")
            {
                il[0x53].Flags |= InstructionFlag.Continue | InstructionFlag.TrustMeItsAFor;
            }

            // Same problem as above; For-Continue in unfortunate place. (LSL7, Torin)
            // Find three consecutive jmps; the middle one is the Continue.
            // Also, the bnt that pointed to this was optimized, so deoptimize it.
            // Weirdly, the bnt is only optimized in SCI2 (Torin, LSL7-Mac).
            // The SCI3 compiler didn't optimize it. Huh.
            if (f.Script.Number == 64892 &&
                f.FullName == "NewHandlerList:handleEvent")
            {
                for (var i = il.First; i != null; i = i.Next)
                {
                    if (i.Operation == Operation.jmp &&
                        i.Prev.Operation == Operation.jmp &&
                        i.Next.Operation == Operation.jmp)
                    {
                        i.Flags |= InstructionFlag.Continue | InstructionFlag.TrustMeItsAFor;

                        // SCI2 compiler did a branch to branch optimization that SCI3 didn't.
                        // Find the previous bnt and retarget it to me, if not already.
                        var bnt = i;
                        while (bnt.Operation != Operation.bnt)
                            bnt = bnt.Prev;
                        if (bnt.BranchTarget != i.Position)
                        {
                            bnt.BranchTarget = i.Position;
                            bnt.Flags |= InstructionFlag.Deoptimized;
                        }

                        break;
                    }
                }
            }

            // Third instance of For-Continue, this time in INN checkers.
            // It's a localproc, and different INN games/sub-games/whatever have
            // different script 0 object names, and there are different versions,
            // so identify it by number and exported object name and temp variables.
            if (f.Script.Number == 400 &&
                f.Object == null && // localproc
                il.First.Operation == Operation.link &&
                il.First.Parameters[0] == 8 && // only localproc with 8 temps
                f.Script.GetExportedObject(0)?.Name == "CheckersGame")
            {
                if (il[0x6b].Operation == Operation.jmp)
                {
                    il[0x6b].Flags |= InstructionFlag.Continue | InstructionFlag.TrustMeItsAFor;
                }
                if (il[0x86].Operation == Operation.jmp)
                {
                    il[0x86].Flags |= InstructionFlag.Continue | InstructionFlag.TrustMeItsAFor;
                }
            }

            // QFG4CD Fan Patch has an invalid function call.
            // Normally there are 43 parameters passed to OneOf.
            // A room number (360) was added, but the parameter count
            // and the calle stack size weren't updated.
            // (Companion asm'd the original function, so the author
            // had to edit that asm output to make their mod, but
            // they didn't update all the necessary instructions.)
            if (f.Script.Number == 13 &&
                f.Script.Game.Id == "Glory" &&
                f.FullName == "castOpenScript:changeState" &&
                il.Has(0x27e) && il.Has(0x28e) && il.Has(0x303) &&
                // param count
                il[0x27e].Operation == Operation.pushi &&
                il[0x27e].Parameters[0] == 0x2b &&
                // new parameter
                il[0x28e].Operation == Operation.pushi &&
                il[0x28e].Parameters[0] == 360 &&
                // stack size
                il[0x303].Operation == Operation.calle &&
                il[0x303].Parameters[2] == 0x56)
            {
                il[0x27e].Parameters[0] += 1; // increase parameter count
                il[0x303].Parameters[2] += 2; // increase stack size
                il[0x303].Flags |= InstructionFlag.CompilerBug; // really a asm mistake
            }
        }
    }
}
