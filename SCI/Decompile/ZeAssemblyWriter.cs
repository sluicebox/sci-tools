using System.Linq;
using System.Text;
using SCI.Resource;

// ZeAssemblyWriter writes ze asm block when a function fails to decompile.
//
// One of the perks of writing a 100% decompiler is that you don't need to
// write a fallback disassembler. But now that I'm releasing the decompiler
// to the wild, I can't not.
//
// "asm" blocks are not part of Sierra's language, they are an invention of
// fan compilers, so this is all about matching SCI Companion's format.
//
// This assembly writer is fallback code for when the decompiler fails, but
// since I'm writing it a year and a half later, and since the decompiler
// hardly fails in practice, that means this code is the least tested in the
// codebase and the most likely to fail. Ha!
//
// I thought this was going to be a Wet Hot American Summer cafeteria chore,
// but coming back for this one last little heist was kinda fun. That said,
// I'll be damned if I'm going to dethrone Ast.cs and make this the first
// source file that everyone sees. Zo you zee, eet ez now ZeAssemblyWriter.

namespace SCI.Decompile
{
    static class ZeAssemblyWriter
    {
        public static void Write(StringBuilder output, int indent, Symbols symbols, Function function)
        {
            var script = function.Script;

            // build a new instruction list.
            // don't use the one that the decompiler built, because it was
            // altered during preprocessing. we want the original instructions.
            var instructions = InstructionList.Parse(function);
            var branchTargets = new BranchTargets(instructions);

            output.AppendLine("(asm");

            foreach (Instruction instruction in instructions)
            {
                // print label if instruction is a branch target
                if (branchTargets.Any(instruction.Position))
                {
                    output.AppendFormat("code_{0:x4}:", instruction.AbsolutePosition);
                    output.AppendLine();
                }

                // print op name
                Indent(indent + 1, output);
                string opName;
                switch (instruction.Operation)
                {
                    case Operation.push0:
                    case Operation.push1:
                    case Operation.push2:
                        // deoptimize to pushi 0/1/2
                        instruction.Parameters = new int[] { instruction.Operation - Operation.push0 };
                        instruction.Operation = Operation.pushi;
                        opName = instruction.Operation.GetName();
                        break;
                    case Operation.file:
                        opName = "_file_"; // companion name
                        break;
                    case Operation.line:
                        opName = "_line_"; // companion name
                        break;
                    case Operation.lea:
                        if (instruction.IsComplexVariable())
                        {
                            opName = "leai"; // companion name when variable is complex
                        }
                        else
                        {
                            opName = instruction.Operation.GetName(); // lea
                        }
                        break;
                    default:
                        opName = instruction.Operation.GetName();
                        break;
                }
                output.Append(opName);

                // pad op name if there are parameters
                if (instruction.Parameters.Length > 0)
                {
                    for (int i = opName.Length; i < 9; i++)
                    {
                        output.Append(' ');
                    }
                }

                // print parameters based on operation
                switch (instruction.Operation)
                {
                    case Operation.bt:
                    case Operation.bnt:
                    case Operation.jmp:
                        if (instructions.Has(instruction.BranchTarget))
                        {
                            output.AppendFormat("code_{0:x4}", instructions[instruction.BranchTarget].AbsolutePosition);
                        }
                        else
                        {
                            output.AppendFormat("{0} ; INVALID BRANCH TARGET", instruction.Parameters[0]);
                        }
                        break;

                    case Operation.call:
                        // print local procedure name and stack size
                        try
                        {
                            output.AppendFormat("{0}, {1,2}",
                                symbols.LocalProcedure(script, instruction.Parameters[0]),
                                instruction.Parameters[1]);
                        }
                        catch // localproc may not exist, maybe that's why we're asm'ing
                        {
                            output.AppendFormat("{0}, {1,2} ; INVALID PROCEDURE OFFSET",
                               instruction.Parameters[0],
                               instruction.Parameters[1]);
                        }
                        break;

                    case Operation.callk:
                        // print kernel function name and stack size
                        output.AppendFormat("{0}, {1,2}",
                            symbols.KernelFunction(instruction.Parameters[0]),
                            instruction.Parameters[1]);
                        break;

                    case Operation.callb:
                        // print public procedure name and stack size
                        output.AppendFormat("{0}, {1,2}",
                            symbols.PublicProcedure(0, instruction.Parameters[0]),
                            instruction.Parameters[1]);
                        break;

                    case Operation.calle:
                        // print public procedure name and stack size
                        output.AppendFormat("{0}, {1,2}",
                           symbols.PublicProcedure(instruction.Parameters[0], instruction.Parameters[1]),
                           instruction.Parameters[2]);
                        break;

                    case Operation.class_:
                        // print class name
                        PrintClass(instruction.Parameters[0], script, symbols, output);
                        break;

                    case Operation.super:
                        // print class name and stack size
                        PrintClass(instruction.Parameters[0], script, symbols, output);
                        output.AppendFormat(", {0,2}", instruction.Parameters[1]);
                        break;

                    case Operation.rest:
                        // print the parameter as an integer.
                        // this is not what companion does, it uses the parameter name,
                        // but that is wrong because when &rest is used as a standalone
                        // keyword, which it is 99.999% of the time, the instruction's
                        // parameter is the index *after* the last known parameter.
                        // when companion asm's a function with &rest, it hallucinates
                        // an extra parameter. thankfully, the compiler accepts an integer.
                        output.Append(instruction.Parameters[0]);
                        break;

                    case Operation.lea:
                        // print address-of and the variable name.
                        // note that we are not printing the use-index flag,
                        // companion handles that with pseudo-opcode name
                        // "leai", which we handled when printing the name.
                        output.Append('@');
                        output.Append(symbols.Variable(script, function, instruction.GetVariableType(), instruction.GetVariableIndex()));
                        break;

                    case Operation.pToa:
                    case Operation.aTop:
                    case Operation.pTos:
                    case Operation.sTop:
                    case Operation.ipToa:
                    case Operation.dpToa:
                    case Operation.ipTos:
                    case Operation.dpTos:
                        // print the property name
                        output.Append(symbols.Property(function.Object, instruction.Parameters[0]));
                        break;

                    case Operation.lofsa:
                    case Operation.lofss:
                    case Operation.loadID:
                    case Operation.pushID:
                        // print the object (depends on the object type)
                        PrintObject(instruction.Parameters[0], script, symbols, output);
                        break;

                    case Operation.file:
                        // print the file name recorded when building the instruction list
                        ScriptWriter.FormatStringLiteral(instructions.DebugFileName, output);
                        break;

                    case Operation.lag:
                    case Operation.lal:
                    case Operation.lat:
                    case Operation.lap:
                    case Operation.lsg:
                    case Operation.lsl:
                    case Operation.lst:
                    case Operation.lsp:
                    case Operation.lagi:
                    case Operation.lali:
                    case Operation.lati:
                    case Operation.lapi:
                    case Operation.lsgi:
                    case Operation.lsli:
                    case Operation.lsti:
                    case Operation.lspi:
                    case Operation.sag:
                    case Operation.sal:
                    case Operation.sat:
                    case Operation.sap:
                    case Operation.ssg:
                    case Operation.ssl:
                    case Operation.sst:
                    case Operation.ssp:
                    case Operation.sagi:
                    case Operation.sali:
                    case Operation.sati:
                    case Operation.sapi:
                    case Operation.ssgi:
                    case Operation.ssli:
                    case Operation.ssti:
                    case Operation.sspi:
                    case Operation.plusag:
                    case Operation.plusal:
                    case Operation.plusat:
                    case Operation.plusap:
                    case Operation.plussg:
                    case Operation.plussl:
                    case Operation.plusst:
                    case Operation.plussp:
                    case Operation.plusagi:
                    case Operation.plusali:
                    case Operation.plusati:
                    case Operation.plusapi:
                    case Operation.plussgi:
                    case Operation.plussli:
                    case Operation.plussti:
                    case Operation.plusspi:
                    case Operation.minusag:
                    case Operation.minusal:
                    case Operation.minusat:
                    case Operation.minusap:
                    case Operation.minussg:
                    case Operation.minussl:
                    case Operation.minusst:
                    case Operation.minussp:
                    case Operation.minusagi:
                    case Operation.minusali:
                    case Operation.minusati:
                    case Operation.minusapi:
                    case Operation.minussgi:
                    case Operation.minussli:
                    case Operation.minussti:
                    case Operation.minusspi:
                        // print the variable name
                        output.Append(symbols.Variable(script, function, instruction.GetVariableType(), instruction.Parameters[0]));
                        break;

                    default:
                        // everybody else has one integer parameter or no parameters
                        if (instruction.Parameters.Length > 0)
                        {
                            // just print the number however InstructionList interpreted it (signed or unsigned)
                            output.Append(instruction.Parameters[0]);
                        }
                        break;
                }

                output.AppendLine();
            }

            Indent(indent, output);
            output.Append(")");
        }

        static void Indent(int indent, StringBuilder output)
        {
            for (int i = 0; i < indent; i++)
            {
                output.Append('\t');
            }
        }

        static void PrintClass(int classNumber, Script script, Symbols symbols, StringBuilder output)
        {
            // print the class name if the class exists, otherwise the number
            var class_ = script.Game.GetClass(script, classNumber);
            if (class_ != null)
            {
                output.Append(symbols.Class(class_));
            }
            else
            {
                output.Append(classNumber);
            }
        }

        // logic from AstBuilder.GetByOffset()
        static void PrintObject(int offset, Script script, Symbols symbols, StringBuilder output)
        {
            var obj = script.Objects.FirstOrDefault(o => o.Position == offset);
            if (obj != null)
            {
                output.Append(symbols.Object(obj));
                return;
            }
            var str = script.Strings.FirstOrDefault(s => s.Position == offset);
            if (str != null)
            {
                ScriptWriter.FormatStringLiteral(str.Text, output);
                return;
            }
            var said = script.Saids.FirstOrDefault(s => s.Position == offset);
            if (said != null)
            {
                output.Append("'");
                output.Append(said.Text);
                output.Append("'");
                return;
            }

            // SCI Companion string
            output.Append("LOOKUP_ERROR");
        }
    }
}
