using System;
using System.Collections.Generic;
using System.Linq;
using SCI.Resource;

// Third Party Compiler Compatibility Hacks
//
// My decompiler does not support third party compilers. And yet, here we are!
//
// It's been a year since I finished the decompiler, and I've mostly recovered.
// For fun, I took a look to see how close it was to handling Companion's code.
// Pretty close! In some ways, Companion's code is easier because it doesn't do
// Sierra's optimizations. This exposed an off-by-one bug in my loop deduction
// that would never have occurred with Sierra-optimized code. Then I took a look
// at SCI Studio and it has similar behavior as Companion, with just a few more
// bugs and quirks. So sure, fine, whatever, let's do them all!
//
// Every fan game I've found now decompiles 100%, except for hand-written assembly
// that can't be represented in script. (zork-demo has a few of these.)
//
// I can now round trip entire Sierra games through my decompiler, recompile them
// with Companion, decompile them 100%, and diff the results. This process has
// ruthlessly exposed bugs in my decompiler and Companion's compiler; I've
// since fixed two compiler bugs in The Cat's fork.
//
// Here are the third party compiler issues, some of which are handled elsewhere:
//
// 1. Branch targets differ from Sierra's
//
//    - BT => BNT will instead target the instruction after the BNT
//    - BNT => BT will instead target the instruction after the BT
//
//    Most third party functions that didn't decompile were due to this.
//
//    The overall control flow is the same, but Companion's branch code is
//    significantly different than Sierra's. It's not a compiler bug, but
//    it creates a different graph structure than what I was expecting.
//    This breaks my control flow analysis algorithm in several ways.
//    I also find these graphs harder to understand, because the branches
//    appear to escape their scope.
//
//    Both instruction patterns can be detected during preprocessing and
//    patched to target the previous instruction, as Sierra's compiler
//    would have done before applying its own optimizations.
//    That's all it takes to satisfy my CFA algorithm.
//
// 2. DoWhile Loops exist in SCI Studio Syntax. See below for details.
//    Short version: detect them during Loop Deduction, flag, patch,
//    then AstBuilder generates the equivalent repeat loop.
//    I also had to allow for bt as a latch instruction.
//
// 3. LINK instruction sometimes don't allocate enough space.
//    I now allow out-of-bounds temps and annotate the function
//    as having a compiler bug. Not sure which compiler did this,
//    if any; it may have been a mistake in a template's asm block.
//
// 4. Unused instructions ssgi/ssli/sspi/ssti are now used.
//    These are "unused" in that Sierra's compiler never emitted them,
//    so I threw an error on them. Now AstBuilder consumes them.
//    I fixed Companion's compiler to stop emitting these instructions,
//    as they break the language when consuming their expressions.
//
// 5. Arithmetic Assignment with Complex Variables.
//    A messy history here. Companion uses creative instruction
//    sequences for arithmetic assignment when the target is a complex
//    variable and the indexer isn't a constant. It emits pprev, and
//    used to emit toss too, both of which break decompiler assumptions.
//    The first sequence was completely broken and the second was
//    partially broken. I've fixed this in Companion, so now there are
//    three possible sequences. I detect them all in preprocessing,
//    delete them, and flag the arithmetic instruction so that AstBuilder
//    creates an arithmetic assignment node. SCI Tetris is the only
//    fan game I've seen with any of these sequences.
//
// 6. Companion optimizes away array indexes when the index is constant.
//    That doesn't break decompiling, it's just annoying because
//    it breaks array detection, so the results are less clear and
//    the variable types are wrong, so it also prevents matching
//    original source by function signature. This was removed in
//    The Cat's fork for better decompiles. Just noting it here.
//
// 7. SCI Studio creates weird switches when default/else isn't
//    the last case in the code. The default case always executes
//    when reached because the compiler doesn't place it as the
//    last case. This produces unexpected control flow. Companion
//    can't decompile these but its compiler fixes the bug by
//    always placing default/else last. My switch detection just
//    happened to already handle this: it produces a default/else case
//    containing the actual control flow.
//
// 8. SCI Studio breaks switches after 16 cases. Classic C program!
//    Betrayed Alliance has three of these, I've detected it in five
//    other games. Companion asm's these. SwitchDetection now handles
//    this; the rest of the switch is treated as the default/else case,
//    which is accurate control flow for this mess.
//
// 9. Undecompilable functions are allowed and exist in fan games.
//    zork-demo has functions with SCI Studio Syntax asm blocks
//    in the middle of the function to jmp out of a loop to an
//    arbitrary label.
//    Ash's QFG4 patch has undecompilable functions because Companion
//    asm'd those functions and he modified them by hand, but that
//    resulted in an invalid function call in one script, and a
//    switch with unrepresentable control flow in another.
//
// A. Sierra's compiler expands the text "\r" in a string literal as
//    a carriage return followed by a newline. Companion, and probably
//    Studio, just treat it as a carriage return.
//    I don't like the idea of emitting strings that different compilers
//    will interpret differently, so I write carriage returns as "\0d".

namespace SCI.Decompile
{
    static class ThirdPartyHacks
    {
        // Sierra:    BT => BNT
        // Companion: BT => Instruction After BNT
        //
        // Detect any BT that forward-targets an instruction that follows
        // a BNT, and patch the BT to instead target the BNT.
        // This is a sequence that Sierra's compiler could never emit.

        public static void BtFixups(InstructionList instructions)
        {
            foreach (var i in instructions)
            {
                if (i.Operation == Operation.bt &&
                    i.IsBranch &&                // probably unnecessary
                    i.BranchTarget > i.Position) // forward-target only
                {
                    var target = instructions[i.BranchTarget];
                    if (target.Operation != Operation.bnt &&
                        target.Prev.Operation == Operation.bnt)
                    {
                        Log.Debug(instructions.Function, "third party branch: " + i + " => " + target.Prev + ", " + target);

                        i.BranchTarget = target.Prev.Position;
                        i.Flags |= InstructionFlag.ThirdPartyBranch;
                    }
                }
            }
        }

        // Sierra:    BNT => BT
        // Companion: BNT => Instruction After BT
        //
        // This is the opposite of BtFixups above, but with one wrinkle:
        // The Companion instruction sequence I want to patch could also appear
        // in Sierra's compiler output. If we mistake the two and patch Sierra's,
        // we'll alter an unrelated structure and break decompiling.
        //
        // As far as I can tell, the only time this sequence could legitimately
        // appear is when a BNT targets the instruction after a breakif or contif.
        // Preprocessing detects loops, so this fixup requires that list so that
        // it can ignore BNTs that target loop follows.
        // This does mean that a for-contif could fool this, but I've run this on
        // my corpus and that hasn't happened, plus it's not like this decompiler
        // can handle for-continue in the first place, so I'm leaving this in.
        // But if it breaks something, it's getting ripped out.
        // I am certain that there are other failure modes for this, but it's a
        // rare instruction sequence to begin with. (told you these were hacks!)

        public static void BntFixups(InstructionList instructions, IReadOnlyList<LoopSummary> loops)
        {
            foreach (var i in instructions)
            {
                if (i.Operation == Operation.bnt &&
                    i.IsBranch &&                // probably unnecessary
                    i.BranchTarget > i.Position) // forward-target only
                {
                    var target = instructions[i.BranchTarget];
                    if (target.Operation != Operation.bt &&
                        target.Prev.Operation == Operation.bt &&
                        target.Prev.BranchTarget > target.Position &&
                        loops.All(l => l.Latch.Position != instructions[target.Prev.BranchTarget].Prev.Position))
                    {
                        Log.Debug(instructions.Function, "third party branch: " + i + " => " + target.Prev + ", " + target);

                        i.BranchTarget = target.Prev.Position;
                        i.Flags |= InstructionFlag.ThirdPartyBranch;
                    }
                }
            }
        }

        // DoWhile loops do not exist in Sierra's Script language but they do
        // exist in SCI Studio syntax. Studio and Companion both produce them.
        // Studio and early Companion use BT for the latch instead of JMP,
        // so LoopDetection scans for both.
        //
        // There are three different DoWhile signatures at the end of the loop,
        // depending on the compiler and version. LoopDeduction calls this
        // function to easily detect DoWhiles by the last two instructions.
        // If it's a DoWhile, then any BNT instructions that target the follow
        // are part of the while test, so they are defanged or retargeted.
        //
        // Once patched, a DoWhile loop is just a Repeat loop that ends in a test.
        // By patching the BNT instructions in the test, this all looks like
        // normal control flow unrelated to loops, so the loop appears to be a
        // boring Repeat loop to the loop solvers and control flow analysis.
        // AstBuilder just needs to check the loop type when consuming the body.
        // If it's DoWhile then it consumes the last expression in the body and
        // wraps it in the appropriate break/not statements.
        public static void DetectDoWhileLoop(LoopSummary loop, BranchTargets branchTargets)
        {
            // SCI Studio
            // Loop ends in while condition, bt to head. simple!
            if (loop.Latch.Operation == Operation.bt &&
                loop.Latch.Prev.Operation != Operation.bnt)
            {
                loop.Type = LoopType.DoWhile;
                loop.Latch.Operation = Operation.jmp;
                loop.Latch.Flags |= InstructionFlag.ThirdPartyBranch;
                return;
            }

            // SCI Companion
            // Early: Loop ends in while condition, bnt to follow, bt to head.  okay?
            // Now:   Loop ends in while condition, bnt to follow, jmp to head. sure!
            //        Unfortunately this last sequence can also occur in a While loop,
            //        when there is just a test but no body. AstBuilder will detect this.
            if (loop.Latch.Prev.Operation == Operation.bnt &&
                loop.Latch.Prev.BranchTarget == loop.Follow.Position)
            {
                loop.Type = LoopType.DoWhile;
                if (loop.Latch.Operation == Operation.bt)
                {
                    loop.Latch.Operation = Operation.jmp;
                    loop.Latch.Flags |= InstructionFlag.ThirdPartyBranch;
                }

                // defang the last bnt so that it is a no-op and will be ignored
                // by control flow analysis and instruction consumption.
                // AstBuilder is just going to consume acc at the end of the loop
                // body, and that will be the test regardless of whether it's a
                // studio loop or a companion loop. but...
                Instruction lastBnt = loop.Latch.Prev;
                branchTargets.RemoveBranch(lastBnt); // have to do this before defanging
                lastBnt.Flags |= InstructionFlag.DefangedBranch;

                // ...if the while condition is an AND then each of its bnt's targets
                // the follow. just retarget all of them to the defanged bnt, that
                // will make a normal graph for CFA, and instruction consumption
                // can still just consume acc at the end to get the correct test.
                for (var i = loop.Start; i != lastBnt; i = i.Next)
                {
                    if (i.Operation == Operation.bnt &&
                        i.IsBranch && // could have been defanged
                        i.BranchTarget == loop.Follow.Position)
                    {
                        i.BranchTarget = lastBnt.Position;
                        i.Flags |= InstructionFlag.ThirdPartyBranch;
                        branchTargets.UpdateBranch(i, loop.Follow.Position);
                    }
                }
            }
        }

        //
        // Arithmetic Assignment
        //

        static Operation[] GoodMathAssignmentSequence =
        {
            // add/sub/etc    // acc = arithmetic result
            Operation.push0,  // dummy push
            Operation.eq,     // pprev = arithmetic result
            Operation.ldi,    // acc = 0
            Operation.or,     // acc = array index
            // pprev          // push arithmetic result
            // ssgi/etc       // array[index] = arithmetic result
            //                // alternatively, my fixed version:
            // sagi/etc       // acc = array[index] = arithmetic result
        };

        static Operation[] BadMathAssignmentSequence =
        {
            // add/sub/etc    // acc = arithmetic result
            Operation.eq,     // uh oh
            Operation.toss,   // oh god no
            // pprev          // stack is fucked
            // ssgi/etc       // array[0 or 1] = arithmetic result
        };

        // Returns true if the instruction list still contains a pprev instruction.
        // This is an optimization that lets me skip PprevFixups if this scan determined
        // that there's no pprev, which there usually isn't. This makes me feel better
        // about scanning all of SCI for something that only appears in one fan game.
        public static bool FixupMathAssignments(InstructionList instructions)
        {
            // scan for pprev because it appears in all instruction sequences
            bool hasPprev = false;
            for (var i = instructions.First; i != null; i = i?.Next)
            {
                if (i.Operation != Operation.pprev) continue;

                // does the next instruction store to an array?
                // if so then we've found an arithmetic assignment sequence.
                Operation load;
                switch (i.Next?.Operation)
                {
                    case Operation.ssgi: // broken
                    case Operation.sagi: // my fix
                        load = Operation.lsgi;
                        break;
                    case Operation.ssli: // broken
                    case Operation.sali: // my fix
                        load = Operation.lsli;
                        break;
                    case Operation.ssti: // broken
                    case Operation.sati: // my fix
                        load = Operation.lsti;
                        break;
                    case Operation.sspi: // broken
                    case Operation.sapi: // my fix
                        load = Operation.lspi;
                        break;
                    default:
                        hasPprev = true;
                        continue;
                }

                Operation[] sequence;
                if (DoesSequenceMatch(i.Prev, GoodMathAssignmentSequence))
                {
                    sequence = GoodMathAssignmentSequence;
                }
                else if (DoesSequenceMatch(i.Prev, BadMathAssignmentSequence))
                {
                    sequence = BadMathAssignmentSequence;
                }
                else
                {
                    throw new Exception("pprev followed by " + i.Next.Operation.GetName() + " in unknown sequence");
                }

                // scan up for the push/ls*i sequence; delete the push.
                // necessary so that stack balances after ripping out the rest.
                Instruction loadInstruction = i;
                while (loadInstruction != null)
                {
                    loadInstruction = loadInstruction.GetPrev(load);
                    if (loadInstruction?.Prev.Operation == Operation.push)
                    {
                        instructions.Remove(loadInstruction.Prev);
                        break;
                    }
                }
                if (loadInstruction == null) throw new Exception("no push/" + load.GetName() + " before sequence");

                // locate first instruction in the sequence to delete
                Instruction toDelete = i;
                for (int j = 0; j < sequence.Length; j++)
                {
                    toDelete = toDelete.Prev;
                }

                // flag the math instruction so that AstBuilder will
                // build a math assignment node
                Instruction mathInstruction = toDelete.Prev;
                mathInstruction.Flags |= InstructionFlag.MathAssignment;
                if (sequence == BadMathAssignmentSequence)
                {
                    mathInstruction.Flags |= InstructionFlag.CompilerBug;
                }

                // if one of the ss*i instructions was used then the
                // assignment can't be consumed. flag it so that
                // AstBuilder knows whether to set acc or not.
                switch (i.Next.Operation)
                {
                    case Operation.ssgi:
                    case Operation.ssli:
                    case Operation.ssti:
                    case Operation.sspi:
                        mathInstruction.Flags |= InstructionFlag.Inconsumable;
                        break;
                }

                // skip the current instruction (pprev) and the next
                // because we're about to delete them
                i = i.Next.Next;

                // delete the whole sequence
                for (int j = 0; j < sequence.Length + 2; j++)
                {
                    Instruction next = toDelete.Next;
                    instructions.Remove(toDelete);
                    toDelete = next;
                }
            }
            return hasPprev;
        }

        static bool DoesSequenceMatch(Instruction last, Operation[] sequence)
        {
            for (int j = sequence.Length - 1; j >= 0; j--)
            {
                if (last == null) return false;
                if (sequence[j] != last.Operation) return false;
                last = last.Prev;
            }
            return true;
        }
    }
}
