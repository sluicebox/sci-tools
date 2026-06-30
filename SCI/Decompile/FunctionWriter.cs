using System.Collections.Generic;
using System.Linq;
using System.Text;
using SCI.Decompile.Ast;

// FunctionWriter writes a function's AST to a StringBuilder.
// ScriptWriter calls this.

namespace SCI.Decompile
{
    class FunctionWriter
    {
        enum Mode { Inline, Multi };

        StringBuilder output;
        int indent;
        Symbols symbols;
        Resource.Function function;
        bool forceInline;

        const int MaxLineLength = 80;

        public FunctionWriter(StringBuilder output, int indent, Symbols symbols, Resource.Function function)
        {
            this.output = output;
            this.indent = indent;
            this.symbols = symbols;
            this.function = function;
        }

        public void WriteTree(Node node)
        {
            node.Accept(new NumberFormatVisitor());
            node.Accept(new NumberSignVisitor());
            node.Accept(new SelectorLiteralVisitor(symbols));
            node.Accept(new TextLengthVisitor(symbols, function));

            forceInline = false;
            Write(node);
        }

        void Write(Node node)
        {
            // how are we going to render this one?
            // leaves don't use this, they just write their word.
            bool prevForceInline = forceInline;
            Mode mode;
            if (forceInline)
            {
                if (node.TextLength == -1) throw new System.Exception("oops");
                mode = Mode.Inline;
            }
            else if (node.TextLength == -1)
            {
                mode = Mode.Multi;
            }
            else
            {
                if (LineLength() + node.TextLength > MaxLineLength)
                {
                    mode = Mode.Multi;
                }
                else
                {
                    mode = Mode.Inline;
                    forceInline = true;
                }
            }

            switch (node.Type)
            {
                case NodeType.List:
                    if (node.Children.Count == 1)
                    {
                        // treat it as a single expression
                        Write(node.Children[0]);
                    }
                    else
                    {
                        // sanity check
                        if (node.Children.Count != 0)
                        {
                            if (mode == Mode.Inline) throw new System.Exception("oops");
                        }

                        // HACK: if our parent is an and/or then this is a weird situation
                        // where multiple expressions were passed as a single operand;
                        // probably because they were unintentionally wrapped in parenthesis.
                        // alternatively, it's something crazy like a for loop as an operand and
                        // i didn't recognize it as a for loops so we've got the for initializer
                        // and the separate while loop. (qfg1vga proc0_3, although that is now
                        // recognize as a for loop by AstBuilder.) point is, weird stuff happens,
                        // so wrap it in parenthesis. SCI Companion won't compile it (good!!)
                        // but the output is accurate and reveals problems.
                        bool wrapInParens = node.Parent?.Type == NodeType.And ||
                                            node.Parent?.Type == NodeType.Or;
                        if (!wrapInParens)
                        {
                            for (int i = 0; i < node.Children.Count; i++)
                            {
                                if (i > 0) Newline();
                                Write(node.Children[i]);
                            }
                        }
                        else
                        {
                            Write("(");
                            indent++;
                            foreach (Node child in node.Children)
                            {
                                Newline();
                                Write(child);
                            }
                            indent--;
                            Newline();
                            Write(")");
                        }
                    }
                    break;
                case NodeType.Number:
                case NodeType.Selector:
                case NodeType.Said:
                    // ToString() methods contain the formatting logic
                    output.Append(node);
                    break;
                case NodeType.String:
                    ScriptWriter.FormatStringLiteral((node as String).Value, output);
                    break;
                case NodeType.Class:
                    output.Append((node as Class).Name);
                    break;
                case NodeType.Object:
                    output.Append((node as Obj).Name);
                    break;
                case NodeType.Self:
                case NodeType.Super:
                case NodeType.Info:
                    output.Append(Keywords[node.Type]);
                    break;
                case NodeType.Variable:
                    {
                        var variable = (Variable)node;
                        output.Append(symbols.Variable(function.Script, function, variable.VariableType, variable.Index));
                    }
                    break;
                case NodeType.ComplexVariable:
                    if (mode == Mode.Inline)
                    {
                        // [local (* 2 temp0)]
                        output.Append("[");
                        Write(node.Children[0]);
                        output.Append(" ");
                        Write(node.Children[1]);
                        output.Append("]");
                    }
                    else
                    {
                        // [local
                        //     (* 2 temp0)
                        // ]
                        output.Append("[");
                        Write(node.Children[0]); // mode doesn't matter, this is a leaf
                        indent++;
                        Newline();
                        Write(node.Children[1]);
                        indent--;
                        Newline();
                        output.Append("]");
                    }
                    break;
                case NodeType.Property:
                    output.Append((node as Property).Name);
                    break;
                case NodeType.Assignment:
                case NodeType.Increment:
                case NodeType.Decrement:
                case NodeType.AssignmentAdd:
                case NodeType.AssignmentSub:
                case NodeType.AssignmentMul:
                case NodeType.AssignmentDiv:
                case NodeType.AssignmentShl:
                case NodeType.AssignmentShr:
                case NodeType.AssignmentXor:
                case NodeType.AssignmentBinAnd:
                case NodeType.AssignmentBinOr:
                case NodeType.Not:
                case NodeType.BinNot:
                case NodeType.Neg:
                case NodeType.Add:
                case NodeType.Sub:
                case NodeType.Mul:
                case NodeType.Div:
                case NodeType.Mod:
                case NodeType.Shl:
                case NodeType.Shr:
                case NodeType.Xor:
                case NodeType.BinAnd:
                case NodeType.BinOr:
                case NodeType.And:
                case NodeType.Or:
                case NodeType.Eq:
                case NodeType.Ne:
                case NodeType.Gt:
                case NodeType.Ge:
                case NodeType.Ugt:
                case NodeType.Uge:
                case NodeType.Lt:
                case NodeType.Le:
                case NodeType.Ult:
                case NodeType.Ule:
                case NodeType.Return:
                case NodeType.Break:
                case NodeType.Continue:
                case NodeType.BreakIf:
                case NodeType.ContinueIf:
                    Write(mode, Keywords[node.Type], node.Children);
                    break;
                case NodeType.Rest:
                    if (node.Children.Count == 0)
                    {
                        // &rest
                        output.Append(Keywords[node.Type]);
                    }
                    else
                    {
                        // (&rest ...)
                        Write(mode, Keywords[node.Type], node.Children);
                    }
                    break;
                case NodeType.AddressOf:
                    // address-of is weird because it's just an @ sign in front of a variable
                    // or complex variable. the @ sign can't be multi-lined, but the complex
                    // variable might. so just write the @ sign at the current position and
                    // then write the child immediately after, regardless of mode.
                    output.Append("@");
                    Write(node.Children[0]);
                    break;
                case NodeType.If:
                    // TextLengthVisitor figured out if this is a ternary-if that could be on one line.
                    // otherwise it's multiline, with the tweak about keeping the test on the "if" line.
                    if (mode == Mode.Inline)
                    {
                        var ifNode = (If)node;
                        Write("(if ");
                        Write(ifNode.Test);
                        Write(" ");
                        Write(ifNode.Then);
                        if (ifNode.Else != null)
                        {
                            Write(" else ");
                            Write(ifNode.Else);
                        }
                        Write(")");
                    }
                    else
                    {
                        output.Append("(if");
                        var ifNode = (If)node;
                        // test
                        if (IsRoomFor(ifNode.Test, 1))
                        {
                            // (if (test)
                            output.Append(' ');
                            bool tempPrevForceInline = forceInline;
                            forceInline = true;
                            Write(ifNode.Test);
                            forceInline = tempPrevForceInline;
                        }
                        else
                        {
                            // (if
                            //   (test)
                            indent++;
                            Newline();
                            Write(ifNode.Test);
                            indent--;
                        }
                        // then
                        if (ifNode.Then.Children.Any())
                        {
                            indent++;
                            Newline();
                            Write(ifNode.Then);
                            indent--;
                        }
                        // else
                        if (ifNode.Else != null)
                        {
                            Newline();
                            Write("else");
                            if (ifNode.Else.Children.Any())
                            {
                                indent++;
                                Newline();
                                Write(ifNode.Else);
                                indent--;
                            }
                        }
                        Newline();
                        Write(")");
                    }
                    break;

                case NodeType.Cond:
                    {
                        // forcing these to be multiline for now
                        if (mode == Mode.Inline) throw new System.Exception("oops");

                        var cond = (Cond)node;
                        Write("(cond");

                        indent++;
                        foreach (var caseOrElse in cond.Children)
                        {
                            Newline();
                            Write(caseOrElse);
                        }
                        indent--;

                        Newline();
                        Write(")");
                    }
                    break;

                case NodeType.Switch:
                    {
                        // forcing these to be multiline for now
                        if (mode == Mode.Inline) throw new System.Exception("oops");

                        var switchNode = (Switch)node;
                        Write("(switch");
                        if (IsRoomFor(switchNode.Head, 1) || IsValue(switchNode.Head))
                        {
                            // (switch head
                            output.Append(' ');
                            bool tempPrevForceInline = forceInline;
                            forceInline = true;
                            Write(switchNode.Head);
                            forceInline = tempPrevForceInline;
                        }
                        else
                        {
                            // (switch
                            //    head
                            indent++;
                            Newline();
                            Write(switchNode.Head);
                            indent--;
                        }

                        indent++;
                        foreach (var caseOrElse in switchNode.Children.Skip(1))
                        {
                            Newline();
                            Write(caseOrElse);
                        }
                        indent--;

                        Newline();
                        Write(")");
                    }
                    break;

                case NodeType.Case:
                    {
                        if (mode == Mode.Inline) throw new System.Exception("oops: switch cases are always multiline (for now)");
                        var case_ = (Case)node;

                        // i would really prefer it if the cond test was on one line,
                        // but not always possible. least worst thing to do is newline
                        // and indent when that happens.
                        Write("(");
                        bool oneLine = false;
                        if (IsRoomFor(case_.Test) || IsValue(case_.Test))
                        {
                            // (cond ...
                            //   (test
                            bool tempPrevForceInline = forceInline;
                            forceInline = true;
                            Write(case_.Test);
                            forceInline = tempPrevForceInline;
                            oneLine = true;
                        }
                        else
                        {
                            // (cond ...
                            //   (
                            //     test
                            indent++;
                            Newline();
                            Write(case_.Test);
                            indent--;
                        }

                        if (IsEmpty(case_.Body))
                        {
                            if (case_.Flags.HasFlag(NodeFlags.CompilerBug))
                            {
                                indent++;
                                Newline();
                                Write("; COMPILER BUG: GAME WILL CRASH");
                                indent--;
                                Newline();
                            }
                        }
                        else if (oneLine && IsValue(case_.Body))
                        {
                            Write(" ");
                            bool tempPrevForceInline = forceInline;
                            forceInline = true;
                            Write(case_.Body);
                            forceInline = tempPrevForceInline;
                        }
                        else
                        {
                            indent++;
                            Newline();
                            Write(case_.Body);
                            indent--;
                            Newline();
                        }
                        Write(")");
                    }
                    break;

                // switch AND cond else, if i want different behavior then i'll make two types
                case NodeType.Else:
                    if (mode == Mode.Inline) throw new System.Exception("oops: switch/cond elses are always multiline (for now)");

                    // else
                    //   expression
                    //   expression
                    Write("(else");
                    var elseBody = ((Else)node).Body;
                    if (elseBody.Children.Count == 1 && IsValue(elseBody.Children[0]))
                    {
                        Write(" ");
                        bool tempPrevForceInline = forceInline;
                        forceInline = true;
                        Write(elseBody.Children[0]);
                        forceInline = tempPrevForceInline;
                    }
                    else if (elseBody.Children.Any())
                    {
                        indent++;
                        foreach (var child in elseBody.Children)
                        {
                            Newline();
                            Write(child);
                        }
                        indent--;
                        Newline();
                    }
                    Write(")");
                    break;

                case NodeType.Loop:
                    {
                        var loop = (Loop)node;
                        if (loop.Test == null)
                        {
                            Write("(repeat");
                        }
                        else if (loop.ForReinit == null)
                        {
                            Write("(while");
                            if (IsRoomFor(loop.Test, 1))
                            {
                                // (while condition
                                Write(' ');
                                bool tempPrevForceInline = forceInline;
                                forceInline = true;
                                Write(loop.Test);
                                forceInline = tempPrevForceInline;
                            }
                            else
                            {
                                // (while
                                //    condition
                                indent++;
                                Newline();
                                Write(loop.Test);
                                indent--;
                            }
                        }
                        else
                        {
                            Write("(for");
                            if (loop.ForInit.TextLength != -1 &&
                                loop.Test.TextLength != -1 &&
                                IsRoomFor(loop.ForReinit, loop.ForInit.TextLength + loop.Test.TextLength + 7))
                            {
                                // (for ((= i 0)) (i < 10) ((++ i))
                                bool tempPrevForceInline = forceInline;
                                forceInline = true;
                                Write(" (");
                                Write(loop.ForInit);
                                Write(") ");
                                Write(loop.Test);
                                Write(" (");
                                Write(loop.ForReinit);
                                Write(")");
                                forceInline = tempPrevForceInline;
                            }
                            else
                            {
                                // (for
                                //      ((= i 0))
                                //      (< i 10)
                                //      ((++ i))
                                //
                                // )
                                indent++;
                                Newline();
                                // for-init is a list in sci, but it's just one expression when decompiling.
                                // fake it by slapping some parens around it.
                                Write("(");
                                Write(loop.ForInit);
                                Write(")");
                                Newline();
                                // for-test is one expression
                                Write(loop.Test);
                                Newline();
                                // for-reinit is a list in sci, and also here too, for rare functions where
                                // i proved the start of the reinit.
                                Write("(");
                                if (IsRoomFor(loop.ForReinit))
                                {
                                    // force reinit to be on one line
                                    bool tempPrevForceInline = forceInline;
                                    forceInline = true;
                                    Write(loop.ForReinit);
                                    forceInline = tempPrevForceInline;
                                }
                                else
                                {
                                    indent++;
                                    Newline();
                                    Write(loop.ForReinit);
                                    indent--;
                                    Newline();
                                }
                                Write(")");
                                // add a blank line after multi-line for header, as long as the body isn't empty
                                if (loop.Body.Children.Any())
                                {
                                    Newline();
                                }
                                indent--;
                            }
                        }

                        if (loop.Body.Children.Any())
                        {
                            indent++;
                            Newline();
                            Write(loop.Body);
                            indent--;
                        }
                        Newline();
                        Write(')');
                    }
                    break;

                case NodeType.Send:
                    // sends are complicated because the receiver could be
                    // a symbol or a multi-line expression, plus the special
                    // indentation formatting for send messages.
                    if (mode == Mode.Inline)
                    {
                        // easy
                        output.Append("(");
                        Write(node.Children[0]);
                        foreach (var sendMessage in node.Children.Skip(1))
                        {
                            output.Append(" ");
                            Write(sendMessage);
                        }
                        output.Append(")");
                    }
                    else
                    {
                        // for a multi-line send, i always want the receiver as part of
                        // the first line.
                        output.Append("(");
                        indent++;
                        Write(node.Children[0]);
                        foreach (var sendMessage in node.Children.Skip(1))
                        {
                            Newline();
                            Write(sendMessage);
                        }
                        indent--;
                        Newline();
                        output.Append(")");
                    }
                    break;
                case NodeType.SendMessage:
                    if (mode == Mode.Inline)
                    {
                        // selector: param1 param2
                        Write(node.Children[0]); // usually a selector, could be a variable, etc
                        output.Append(":");
                        foreach (var param in node.Children.Skip(1))
                        {
                            output.Append(" ");
                            Write(param);
                        }
                    }
                    else
                    {
                        // selector:
                        //   param1
                        //   param2
                        Write(node.Children[0]);
                        output.Append(":");
                        indent++;
                        foreach (var param in node.Children.Skip(1))
                        {
                            Newline();
                            Write(param);
                        }
                        indent--;
                    }
                    break;
                case NodeType.KernelCall:
                    Write(mode, (node as KernelCall).Name, node.Children);
                    break;
                case NodeType.PublicCall:
                    if (node.Flags.HasFlag(NodeFlags.CompilerBug))
                    {
                        // sloppy, but this only occurs in kq5 pc98 japanese
                        Write("; COMPILER BUG: GAME WILL CRASH");
                        Newline();
                    }
                    Write(mode, (node as PublicCall).Name, node.Children);
                    break;
                case NodeType.LocalCall:
                    Write(mode, (node as LocalCall).Name, node.Children);
                    break;
                default:
                    throw new System.Exception("Don't know how to write this node yet: " + node);
            }

            forceInline = prevForceInline;
        }

        void Write(Mode mode, string name, IReadOnlyList<Node> children)
        {
            if (mode == Mode.Inline)
            {
                WriteInline(name, children);
            }
            else
            {
                WriteMultiline(name, children);
            }
        }

        void WriteInline(string name, IReadOnlyList<Node> children)
        {
            // (name child1 child2)
            // (name)
            output.Append('(');
            output.Append(name);
            foreach (var child in children)
            {
                output.Append(' ');
                Write(child);
            }
            output.Append(')');
        }

        void WriteMultiline(string name, IReadOnlyList<Node> children)
        {
            // (name
            //     child1
            //     child2
            // )
            // (name)
            output.Append('(');
            output.Append(name);
            if (children.Any())
            {
                // HACK: special case for assignment nodes;
                // write the target inline unless we can't.
                // This is a hack because I can't think of a name for this,
                // so I can't think of a parameter to pass, so I'm just testing
                // the name to see if it's an assignment.
                // I do this for sends too, but sends already do custom formatting.
                // (= destination
                //     source
                // )
                int skip = 0;
                if (name == "=")
                {
                    Node dest = children.First();
                    if (IsRoomFor(dest, 1))
                    {
                        output.Append(' ');
                        bool prevForceInline = forceInline;
                        forceInline = true;
                        Write(dest);
                        forceInline = prevForceInline;
                        skip = 1;
                    }
                }

                indent++;
                foreach (var child in children.Skip(skip))
                {
                    Newline();
                    Write(child);
                }
                indent--;
                Newline();
            }
            output.Append(')');
        }

        void Write(string s) { output.Append(s); }
        void Write(char c) { output.Append(c); }

        void Newline()
        {
            output.AppendLine();
            for (int i = 0; i < indent; i++)
            {
                output.Append('\t');
            }
        }

        bool IsRoomFor(Node node, int extraLength = 0)
        {
            return (node.TextLength != -1) &&
                   (LineLength() + node.TextLength + extraLength <= MaxLineLength);
        }

        int LineLength()
        {
            int lineLength = 0;
            int position = output.Length - 1;
            while (position >= 0)
            {
                char c = output[position];
                if (c == '\r' || c == '\n') break;
                if (c == '\t')
                {
                    lineLength += 4;
                }
                else
                {
                    lineLength++;
                }
                position--;
            }
            return lineLength;
        }

        static bool IsEmpty(Node node)
        {
            return node.Type == NodeType.List && node.Children.Count == 0;
        }

        // used for figuring out if a case body (including test/"else") goes on one line
        static bool IsValue(Node node)
        {
            switch (node.Type)
            {
                case NodeType.List:
                    return (node.Children.Count == 1) && IsValue(node.Children[0]);
                case NodeType.Number:
                case NodeType.String:
                case NodeType.Class:
                case NodeType.Object:
                case NodeType.Self:
                case NodeType.Variable:
                case NodeType.Property:
                    return true;
                case NodeType.ComplexVariable:
                    return node.Children[0].Type != NodeType.ComplexVariable &&
                           IsValue(node.Children[0]);
                case NodeType.AddressOf:
                    return IsValue(node.Children[0]);
                default:
                    return false;
            }
        }

        public static IReadOnlyDictionary<NodeType, string> Keywords = new Dictionary<NodeType, string>
        {
            { NodeType.Self, "self" },
            { NodeType.Super, "super" },
            { NodeType.Rest, "&rest" },
            { NodeType.Info, "-info-" },
            { NodeType.Return, "return" },
            { NodeType.Assignment, "=" },
            { NodeType.Increment, "++" },
            { NodeType.Decrement, "--" },
            { NodeType.AssignmentAdd, "+=" },
            { NodeType.AssignmentSub, "-=" },
            { NodeType.AssignmentMul, "*=" },
            { NodeType.AssignmentDiv, "/=" },
            { NodeType.AssignmentShl, "<<=" },
            { NodeType.AssignmentShr, ">>=" },
            { NodeType.AssignmentXor, "^=" },
            { NodeType.AssignmentBinAnd, "&=" },
            { NodeType.AssignmentBinOr, "|=" },
            { NodeType.AddressOf, "@" },
            { NodeType.Not, "not" },
            { NodeType.BinNot, "~" },
            { NodeType.Neg, "-" },
            { NodeType.Add, "+" },
            { NodeType.Sub, "-" },
            { NodeType.Mul, "*" },
            { NodeType.Div, "/" },
            { NodeType.Mod, "mod" },
            { NodeType.Shl, "<<" },
            { NodeType.Shr, ">>" },
            { NodeType.Xor, "^" },
            { NodeType.BinAnd, "&" },
            { NodeType.BinOr, "|" },
            { NodeType.And, "and" },
            { NodeType.Or, "or" },
            { NodeType.Eq, "==" },
            { NodeType.Ne, "!=" },
            { NodeType.Gt, ">" },
            { NodeType.Ge, ">=" },
            { NodeType.Ugt, "u>" },
            { NodeType.Uge, "u>=" },
            { NodeType.Lt, "<" },
            { NodeType.Le, "<=" },
            { NodeType.Ult, "u<" },
            { NodeType.Ule, "u<=" },
            { NodeType.If ,"if" },
            { NodeType.Cond ,"cond" },
            { NodeType.Switch ,"switch" },
            { NodeType.Case, "case" },
            { NodeType.Else, "else" },
            { NodeType.Break, "break" },
            { NodeType.Continue, "continue" },
            { NodeType.BreakIf, "breakif" },
            { NodeType.ContinueIf, "contif" },
        };
    }

    class TextLengthVisitor : Visitor
    {
        Symbols symbols;
        Resource.Function function;

        public TextLengthVisitor(Symbols symbols, Resource.Function function)
        {
            this.symbols = symbols;
            this.function = function;
        }

        public override void Visit(Node node)
        {
            switch (node.Type)
            {
                case NodeType.List:
                    if (node.Children.Count == 0)
                    {
                        node.TextLength = 0; // does this happen?
                    }
                    else if (node.Children.Count == 1)
                    {
                        // if there's just one then use it
                        node.TextLength = node.Children[0].TextLength;
                    }
                    else
                    {
                        // multiple expressions means multiple lines
                        node.TextLength = -1;
                    }
                    break;
                case NodeType.Self:
                case NodeType.Super:
                case NodeType.Info:
                    // these are keywords with no children so just use keyword length
                    node.TextLength = FunctionWriter.Keywords[node.Type].Length;
                    break;
                case NodeType.Return:
                case NodeType.Not:
                case NodeType.BinNot:
                case NodeType.Neg:
                case NodeType.Add:
                case NodeType.Sub:
                case NodeType.Mul:
                case NodeType.Div:
                case NodeType.Mod:
                case NodeType.Shl:
                case NodeType.Shr:
                case NodeType.Xor:
                case NodeType.BinAnd:
                case NodeType.BinOr:
                case NodeType.And:
                case NodeType.Or:
                case NodeType.AssignmentAdd:
                case NodeType.AssignmentSub:
                case NodeType.AssignmentMul:
                case NodeType.AssignmentDiv:
                case NodeType.AssignmentShl:
                case NodeType.AssignmentShr:
                case NodeType.AssignmentXor:
                case NodeType.AssignmentBinAnd:
                case NodeType.AssignmentBinOr:
                case NodeType.Break:
                case NodeType.Continue:
                case NodeType.BreakIf:
                case NodeType.ContinueIf:
                    // (keyword ...), maybe no children
                    node.TextLength = NodeLength(FunctionWriter.Keywords[node.Type], node);
                    break;
                case NodeType.Rest:
                    if (node.Children.Count == 0)
                    {
                        // &rest
                        node.TextLength = FunctionWriter.Keywords[node.Type].Length;
                    }
                    else
                    {
                        // (&rest ...)
                        node.TextLength = NodeLength(FunctionWriter.Keywords[node.Type], node);
                    }
                    break;
                default:
                    throw new System.Exception("Unsupported node: " + node);
            }
        }

        public override void Visit(Number node)
        {
            node.TextLength = node.IsHex ? 5 : node.Value.ToString().Length;
        }
        public override void Visit(String node)
        {
            var stringLiteral = new StringBuilder(node.Value.Length * 2);
            ScriptWriter.FormatStringLiteral(node.Value, stringLiteral);
            node.TextLength = stringLiteral.Length;
        }
        public override void Visit(Said node) { node.TextLength = node.Value.Length + 2; }
        public override void Visit(Selector node)
        {
            node.TextLength = node.Name.Length;
            if (node.IsLiteral) node.TextLength++;
        }
        public override void Visit(Class node) { node.TextLength = node.Name.Length; }
        public override void Visit(Obj node) { node.TextLength = node.Name.Length; }
        public override void Visit(Variable node) { node.TextLength = symbols.Variable(function.Script, function, node.VariableType, node.Index).Length; }
        public override void Visit(ComplexVariable node)
        {
            int indexLength = node.Index.TextLength;
            if (indexLength == -1)
            {
                node.TextLength = -1;
            }
            else
            {
                // [variable index]
                node.TextLength = 3 + node.Variable.TextLength + indexLength;
            }
        }
        public override void Visit(Property node) { node.TextLength = node.Name.Length; }
        public override void Visit(Assignment node) { node.TextLength = NodeLength(node); }
        public override void Visit(Increment node) { node.TextLength = NodeLength(node); }
        public override void Visit(Decrement node) { node.TextLength = NodeLength(node); }
        public override void Visit(AddressOf node)
        {
            if (node.Operand.TextLength == -1)
            {
                node.TextLength = -1;
            }
            else
            {
                node.TextLength = node.Operand.TextLength;
            }
        }
        public override void Visit(Compare node) { node.TextLength = NodeLength(node); }

        public override void Visit(If node)
        {
            // one-liner is only allowed if this appears to be a ternary operation.
            // maybe i'll loosen that up for short if thens, but for now...
            if (node.Children.Any(c => c.TextLength == -1))
            {
                // Test, Then, or Else are multi-line, so the entire If must be
                node.TextLength = -1;
            }
            else if (!IsIfTernaryResult(node.Then))
            {
                // Then is definitely not a ternary result, so multi-line
                node.TextLength = -1;
            }
            else if (node.Else != null && !IsIfTernaryResult(node.Else))
            {
                // Else is definitely not a ternary result, so multi-line
                node.TextLength = -1;
            }
            else
            {
                // Okay fine, it's a ternary
                node.TextLength = 4 + node.Test.TextLength + 1 + node.Then.TextLength + 1;
                if (node.Else != null)
                {
                    node.TextLength += 6 + node.Else.TextLength;
                }
            }

            // exception: if it looked like a ternary, but Then is just an increment/decrement
            // in a list of expressions, treat it like a regular if statement.
            // (if temp (++ temp)) shouldn't be a one-liner in a list of expressions.
            if (node.TextLength != -1 &&
                node.Parent.Type == NodeType.List &&
                node.Then.Children.Count == 1 &&
                (node.Then.Children[0].Type == NodeType.Increment ||
                 node.Then.Children[0].Type == NodeType.Decrement))
            {
                // overruled, render this as a normal if statement (multi-line)
                node.TextLength = -1;
            }
        }

        static bool IsIfTernaryResult(Node node)
        {
            switch (node.Type)
            {
                case NodeType.Number:
                case NodeType.String:
                case NodeType.Said:
                case NodeType.Property:
                case NodeType.Class:
                case NodeType.Object:
                case NodeType.Variable:
                case NodeType.ComplexVariable:
                case NodeType.Self:
                case NodeType.Info:
                case NodeType.Increment:
                case NodeType.Decrement:
                    return true;
                case NodeType.AddressOf:
                    return IsIfTernaryResult(node.Children[0]);
                case NodeType.List:
                    return (node.Children.Count == 1) && IsIfTernaryResult(node.Children[0]);
                default:
                    return false;
            }
        }

        // switches and cases are all multiline for now
        public override void Visit(Switch node) { node.TextLength = -1; }
        public override void Visit(Cond node) { node.TextLength = -1; }
        public override void Visit(Case node) { node.TextLength = -1; }
        public override void Visit(Else node) { node.TextLength = -1; }
        public override void Visit(Send node)
        {
            int childrenLength = ChildrenLength(node);
            if (childrenLength == -1)
            {
                node.TextLength = -1;
            }
            else
            {
                // 2 parenthesis + children length, which includes extra space
                node.TextLength = 1 + childrenLength;
            }
        }

        // loops are always multiline
        public override void Visit(Loop node) { node.TextLength = -1; }

        public override void Visit(SendMessage node)
        {
            // this math works out with the colon
            node.TextLength = ChildrenLength(node);
        }
        public override void Visit(KernelCall node)
        {
            node.TextLength = NodeLength(node.Name, node);
        }
        public override void Visit(PublicCall node)
        {
            node.TextLength = NodeLength(node.Name, node);
        }
        public override void Visit(LocalCall node)
        {
            node.TextLength = NodeLength(node.Name, node);
        }

        int NodeLength(Node node)
        {
            return NodeLength(FunctionWriter.Keywords[node.Type], node);
        }

        int NodeLength(string name, Node node)
        {
            // parenthesis, name, children
            int childrenLength = ChildrenLength(node);
            if (childrenLength == -1) return -1;
            return 2 + name.Length + childrenLength;
        }

        int ChildrenLength(Node node)
        {
            return ChildrenLength(node.Children);
        }

        int ChildrenLength(IEnumerable<Node> children)
        {
            int length = 0;
            foreach (var child in children)
            {
                if (child.TextLength == -1) return -1;
                length += child.TextLength + 1;
            }
            return length;
        }
    }

    // Format binary operands in hex.
    // Example: (| temp0 $0800)
    class NumberFormatVisitor : Visitor
    {
        public override void Visit(Number number)
        {
            switch (number.Parent.Type)
            {
                case NodeType.BinAnd:
                case NodeType.BinOr:
                case NodeType.BinNot:
                case NodeType.Shl:
                case NodeType.Shr:
                case NodeType.Xor:
                case NodeType.AssignmentBinAnd:
                case NodeType.AssignmentBinOr:
                case NodeType.AssignmentShl:
                case NodeType.AssignmentShr:
                case NodeType.AssignmentXor:
                    number.IsHex = true;
                    break;
            }
        }
    }

    // Set the signage of comparison operands
    class NumberSignVisitor : Visitor
    {
        public override void Visit(Number number)
        {
            switch (number.Parent.Type)
            {
                case NodeType.Gt:
                case NodeType.Ge:
                case NodeType.Lt:
                case NodeType.Le:
                    if (number.Value >= 0x8000)
                    {
                        // should be negative
                        // "flip the bits, add one"
                        number.Value = (-number.Value ^ 0xffff) + 1;
                        number.IsHex = false; // just in case
                    }
                    break;
                case NodeType.Ugt:
                case NodeType.Uge:
                case NodeType.Ult:
                case NodeType.Ule:
                    if (number.Value < 0)
                    {
                        // should be unsigned
                        // "flip the bits, add one"
                        number.Value = (-number.Value ^ 0xffff) + 1;
                    }
                    break;
            }
        }
    }

    // There are functions that are known to take selector literals as a parameter.
    // Identify these and replace the Number with a Selector and tag it as a literal
    // so that it's printed with a "#".
    // Example: (Obj respondsTo: #dispose)
    class SelectorLiteralVisitor : Visitor
    {
        Symbols symbols;

        public SelectorLiteralVisitor(Symbols symbols) { this.symbols = symbols; }

        public override void Visit(SendMessage node)
        {
            if (node.Selector.Type == NodeType.Selector)
            {
                var selector = node.Selector as Selector;
                switch (selector.Name)
                {
                    case "allTrue":
                    case "firstTrue":
                    case "eachElementDo":
                    case "respondsTo":
                    case "eachLineDo":
                    case "selector":
                    case "perform": // realm
                        if (node.Children.Count > 1 && node.Children[1].Type == NodeType.Number)
                        {
                            node.Replace(node.Children[1], CreateSelectorLiteralNode((Number)node.Children[1]));
                        }
                        break;
                }
            }
        }

        public override void Visit(PublicCall node)
        {
            // (Eval object #selector ...)
            if ((node.Script == 999 && node.Export == 7) || (node.Script == 64999 && node.Export == 6))
            {
                if (node.Children.Count > 1 && node.Children[1].Type == NodeType.Number)
                {
                    node.Replace(node.Children[1], CreateSelectorLiteralNode((Number)node.Children[1]));
                }
            }
        }

        public override void Visit(KernelCall node)
        {
            if (node.Name == "RespondsTo" &&
                node.Children.Count >= 2 &&
                node.Children[1].Type == NodeType.Number)
            {
                node.Replace(node.Children[1], CreateSelectorLiteralNode((Number)node.Children[1]));
            }
        }

        Selector CreateSelectorLiteralNode(Number number)
        {
            var selector = new Selector(symbols.Selector(number.Value), number.Value);
            selector.IsLiteral = true;
            return selector;
        }
    }
}
