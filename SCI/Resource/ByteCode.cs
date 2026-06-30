using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

// Operations, Opcodes, and OpFlags

namespace SCI.Resource
{
    public enum Operation
    {
        unused,

        bnot, add, sub, mul, div, mod, shr, shl, xor, and, or, neg, not,
        eq, ne,
        gt, ge, lt, le,
        ugt, uge, ult, ule,
        bt, bnt, jmp,
        ldi, push, pushi, toss, dup,
        link, call, callk, callb, calle, ret,
        send, class_, self,
        pushInfo, info, pushSuperP, superP,
        super, rest,
        lea, selfID, pprev,
        pToa, aTop, pTos, sTop,
        ipToa, dpToa, ipTos, dpTos,
        lofsa, lofss,
        push0, push1, push2, pushSelf,

        file, line,

        loadID, pushID, // LSCI

        lag,         lal,     lat,     lap,     lsg,     lsl,     lst,     lsp,     lagi,     lali,     lati,     lapi,     lsgi,     lsli,     lsti,     lspi,
        sag,         sal,     sat,     sap,     ssg,     ssl,     sst,     ssp,     sagi,     sali,     sati,     sapi,     ssgi,     ssli,     ssti,     sspi,
        plusag,   plusal,  plusat,  plusap,  plussg,  plussl,  plusst,  plussp,  plusagi,  plusali,  plusati,  plusapi,  plussgi,  plussli,  plussti,  plusspi,
        minusag, minusal, minusat, minusap, minussg, minussl, minusst, minussp, minusagi, minusali, minusati, minusapi, minussgi, minussli, minussti, minusspi,
    }

    public static class OperationExtension
    {
        static string[] operations =
        {
            "unused",

            "bnot", "add", "sub", "mul", "div", "mod", "shr", "shl", "xor", "and", "or", "neg", "not",
            "eq?", "ne?",
            "gt?", "ge?", "lt?", "le?",
            "ugt?", "uge?", "ult?", "ule?",
            "bt", "bnt", "jmp",
            "ldi", "push", "pushi", "toss", "dup",
            "link", "call", "callk", "callb", "calle", "ret",
            "send", "class", "self",
            "pushInfo", "info", "pushSuperP", "superP",
            "super", "&rest",
            "lea", "selfID", "pprev",
            "pToa", "aTop", "pTos", "sTop",
            "ipToa", "dpToa", "ipTos", "dpTos",
            "lofsa", "lofss",
            "push0", "push1", "push2", "pushSelf",

            "file", "line",

            "loadID", "pushID", // LSCI

            "lag", "lal", "lat", "lap", "lsg", "lsl", "lst", "lsp", "lagi", "lali", "lati", "lapi", "lsgi", "lsli", "lsti", "lspi",
            "sag", "sal", "sat", "sap", "ssg", "ssl", "sst", "ssp", "sagi", "sali", "sati", "sapi", "ssgi", "ssli", "ssti", "sspi",
            "+ag", "+al", "+at", "+ap", "+sg", "+sl", "+st", "+sp", "+agi", "+ali", "+ati", "+api", "+sgi", "+sli", "+sti", "+spi",
            "-ag", "-al", "-at", "-ap", "-sg", "-sl", "-st", "-sp", "-agi", "-ali", "-ati", "-api", "-sgi", "-sli", "-sti", "-spi",
        };

        public static string GetName(this Operation operation)
        {
            return operations[(int)operation];
        }
    }

    public enum ByteCodeVersion
    {
        SCI0_11, // 8-bit stack-size operands
        LSCI,    // 8-bit stack-size operands, lofsa/lofss => loadID/pushID, lea removed, calle changed
        SCI2,    // 16-bit stack-size operands, file/line debug instructions
        SCI3     // pushInfo/info/pushSuper/superP
    }

    public class Opcode
    {
        static Dictionary<ByteCodeVersion, IReadOnlyList<Opcode>> opcodeSets = new Dictionary<ByteCodeVersion, IReadOnlyList<Opcode>>(4);

        static Opcode()
        {
            opcodeSets.Add(ByteCodeVersion.SCI0_11, BuildOpcodeSet(ByteCodeVersion.SCI0_11));
            opcodeSets.Add(ByteCodeVersion.LSCI,    BuildOpcodeSet(ByteCodeVersion.LSCI));
            opcodeSets.Add(ByteCodeVersion.SCI2,    BuildOpcodeSet(ByteCodeVersion.SCI2));
            opcodeSets.Add(ByteCodeVersion.SCI3,    BuildOpcodeSet(ByteCodeVersion.SCI3));
        }

        public static IReadOnlyList<Opcode> GetOpcodeSet(ByteCodeVersion version)
        {
            return opcodeSets[version];
        }

        // returns a 256 opcode set for the specified version.
        // unused opcodes have the "unused" operation so that
        // an opcode value can be used as an index into the collection.
        static IReadOnlyList<Opcode> BuildOpcodeSet(ByteCodeVersion version)
        {
            byte varSize = (byte)((version <= ByteCodeVersion.LSCI) ? 1 : 2);

            var set = new List<Opcode>(256);
            set.Add(new Opcode(0x00, Operation.bnot));
            set.Add(new Opcode(0x01, Operation.bnot));
            set.Add(new Opcode(0x02, Operation.add));
            set.Add(new Opcode(0x03, Operation.add));
            set.Add(new Opcode(0x04, Operation.sub));
            set.Add(new Opcode(0x05, Operation.sub));
            set.Add(new Opcode(0x06, Operation.mul));
            set.Add(new Opcode(0x07, Operation.mul));
            set.Add(new Opcode(0x08, Operation.div));
            set.Add(new Opcode(0x09, Operation.div));
            set.Add(new Opcode(0x0a, Operation.mod));
            set.Add(new Opcode(0x0b, Operation.mod));
            set.Add(new Opcode(0x0c, Operation.shr));
            set.Add(new Opcode(0x0d, Operation.shr));
            set.Add(new Opcode(0x0e, Operation.shl));
            set.Add(new Opcode(0x0f, Operation.shl));
            set.Add(new Opcode(0x10, Operation.xor));
            set.Add(new Opcode(0x11, Operation.xor));
            set.Add(new Opcode(0x12, Operation.and));
            set.Add(new Opcode(0x13, Operation.and));
            set.Add(new Opcode(0x14, Operation.or));
            set.Add(new Opcode(0x15, Operation.or));
            set.Add(new Opcode(0x16, Operation.neg));
            set.Add(new Opcode(0x17, Operation.neg));
            set.Add(new Opcode(0x18, Operation.not));
            set.Add(new Opcode(0x19, Operation.not));
            set.Add(new Opcode(0x1a, Operation.eq));
            set.Add(new Opcode(0x1b, Operation.eq));
            set.Add(new Opcode(0x1c, Operation.ne));
            set.Add(new Opcode(0x1d, Operation.ne));
            set.Add(new Opcode(0x1e, Operation.gt));
            set.Add(new Opcode(0x1f, Operation.gt));
            set.Add(new Opcode(0x20, Operation.ge));
            set.Add(new Opcode(0x21, Operation.ge));
            set.Add(new Opcode(0x22, Operation.lt));
            set.Add(new Opcode(0x23, Operation.lt));
            set.Add(new Opcode(0x24, Operation.le));
            set.Add(new Opcode(0x25, Operation.le));
            set.Add(new Opcode(0x26, Operation.ugt));
            set.Add(new Opcode(0x27, Operation.ugt));
            set.Add(new Opcode(0x28, Operation.uge));
            set.Add(new Opcode(0x29, Operation.uge));
            set.Add(new Opcode(0x2a, Operation.ult));
            set.Add(new Opcode(0x2b, Operation.ult));
            set.Add(new Opcode(0x2c, Operation.ule));
            set.Add(new Opcode(0x2d, Operation.ule));
            set.Add(new Opcode(0x2e, Operation.bt, 2));
            set.Add(new Opcode(0x2f, Operation.bt, 1));
            set.Add(new Opcode(0x30, Operation.bnt, 2));
            set.Add(new Opcode(0x31, Operation.bnt, 1));
            set.Add(new Opcode(0x32, Operation.jmp, 2));
            set.Add(new Opcode(0x33, Operation.jmp, 1));
            set.Add(new Opcode(0x34, Operation.ldi, 2));
            set.Add(new Opcode(0x35, Operation.ldi, 1));
            set.Add(new Opcode(0x36, Operation.push));
            set.Add(new Opcode(0x37, Operation.push));
            set.Add(new Opcode(0x38, Operation.pushi, 2));
            set.Add(new Opcode(0x39, Operation.pushi, 1));
            set.Add(new Opcode(0x3a, Operation.toss));
            set.Add(new Opcode(0x3b, Operation.toss));
            set.Add(new Opcode(0x3c, Operation.dup));
            set.Add(new Opcode(0x3d, Operation.dup));
            set.Add(new Opcode(0x3e, Operation.link, 2));
            set.Add(new Opcode(0x3f, Operation.link, 1));
            set.Add(new Opcode(0x40, Operation.call, 2, varSize));
            set.Add(new Opcode(0x41, Operation.call, 1, varSize));
            set.Add(new Opcode(0x42, Operation.callk, 2, varSize));
            set.Add(new Opcode(0x43, Operation.callk, 1, varSize));
            set.Add(new Opcode(0x44, Operation.callb, 2, varSize));
            set.Add(new Opcode(0x45, Operation.callb, 1, varSize));
            if (version != ByteCodeVersion.LSCI)
            {
                set.Add(new Opcode(0x46, Operation.calle, 2, 2, varSize));
            }
            else
            {
                set.Add(new Opcode(0x46, Operation.calle, 2, 1, varSize));
            }
            set.Add(new Opcode(0x47, Operation.calle, 1, 1, varSize));
            set.Add(new Opcode(0x48, Operation.ret));
            set.Add(new Opcode(0x49, Operation.ret));
            set.Add(new Opcode(0x4a, Operation.send, varSize));
            set.Add(new Opcode(0x4b, Operation.send, varSize));
            if (version == ByteCodeVersion.SCI3)
            {
                set.Add(new Opcode(0x4c, Operation.info));
                set.Add(new Opcode(0x4d, Operation.pushInfo));
                set.Add(new Opcode(0x4e, Operation.superP));
                set.Add(new Opcode(0x4f, Operation.pushSuperP));
            }
            else
            {
                set.Add(new Opcode(0x4c, Operation.unused));
                set.Add(new Opcode(0x4d, Operation.unused));
                set.Add(new Opcode(0x4e, Operation.unused));
                set.Add(new Opcode(0x4f, Operation.unused));
            }
            set.Add(new Opcode(0x50, Operation.class_, 2));
            set.Add(new Opcode(0x51, Operation.class_, 1));
            set.Add(new Opcode(0x52, Operation.unused));
            set.Add(new Opcode(0x53, Operation.unused));
            set.Add(new Opcode(0x54, Operation.self, varSize));
            set.Add(new Opcode(0x55, Operation.self, varSize));
            set.Add(new Opcode(0x56, Operation.super, 2, varSize));
            set.Add(new Opcode(0x57, Operation.super, 1, varSize));
            set.Add(new Opcode(0x58, Operation.rest, 2));
            set.Add(new Opcode(0x59, Operation.rest, 1));
            if (version != ByteCodeVersion.LSCI)
            {
                set.Add(new Opcode(0x5a, Operation.lea, 2, 2));
                set.Add(new Opcode(0x5b, Operation.lea, 1, 1));
            }
            else
            {
                set.Add(new Opcode(0x5a, Operation.loadID, 2));
                set.Add(new Opcode(0x5b, Operation.unused));
            }
            set.Add(new Opcode(0x5c, Operation.selfID));
            set.Add(new Opcode(0x5d, Operation.selfID));
            set.Add(new Opcode(0x5e, Operation.unused));
            set.Add(new Opcode(0x5f, Operation.unused));
            set.Add(new Opcode(0x60, Operation.pprev));
            set.Add(new Opcode(0x61, Operation.pprev));
            set.Add(new Opcode(0x62, Operation.pToa, 2));
            set.Add(new Opcode(0x63, Operation.pToa, 1));
            set.Add(new Opcode(0x64, Operation.aTop, 2));
            set.Add(new Opcode(0x65, Operation.aTop, 1));
            set.Add(new Opcode(0x66, Operation.pTos, 2));
            set.Add(new Opcode(0x67, Operation.pTos, 1));
            set.Add(new Opcode(0x68, Operation.sTop, 2));
            set.Add(new Opcode(0x69, Operation.sTop, 1));
            set.Add(new Opcode(0x6a, Operation.ipToa, 2));
            set.Add(new Opcode(0x6b, Operation.ipToa, 1));
            set.Add(new Opcode(0x6c, Operation.dpToa, 2));
            set.Add(new Opcode(0x6d, Operation.dpToa, 1));
            set.Add(new Opcode(0x6e, Operation.ipTos, 2));
            set.Add(new Opcode(0x6f, Operation.ipTos, 1));
            set.Add(new Opcode(0x70, Operation.dpTos, 2));
            set.Add(new Opcode(0x71, Operation.dpTos, 1));
            if (version != ByteCodeVersion.LSCI)
            {
                // lofsa/lofss operands must be 16-bit because they have relocations
                set.Add(new Opcode(0x72, Operation.lofsa, 2));
                set.Add(new Opcode(0x73, Operation.unused));
                set.Add(new Opcode(0x74, Operation.lofss, 2)); // unused by sierra's compiler
                set.Add(new Opcode(0x75, Operation.unused));
            }
            else
            {
                set.Add(new Opcode(0x72, Operation.unused));
                set.Add(new Opcode(0x73, Operation.unused));
                set.Add(new Opcode(0x74, Operation.pushID, 2));
                set.Add(new Opcode(0x75, Operation.unused));
            }
            set.Add(new Opcode(0x76, Operation.push0));
            set.Add(new Opcode(0x77, Operation.push0));
            set.Add(new Opcode(0x78, Operation.push1));
            set.Add(new Opcode(0x79, Operation.push1));
            set.Add(new Opcode(0x7a, Operation.push2));
            set.Add(new Opcode(0x7b, Operation.push2));
            set.Add(new Opcode(0x7c, Operation.pushSelf));
            if (version == ByteCodeVersion.SCI0_11)
            {
                set.Add(new Opcode(0x7d, Operation.pushSelf)); // used by third party compilers
                set.Add(new Opcode(0x7e, Operation.unused));
            }
            else
            {
                set.Add(new Opcode(0x7d, Operation.file, 0)); // special: length 0 for filename
                set.Add(new Opcode(0x7e, Operation.line, 2));
            }
            set.Add(new Opcode(0x7f, Operation.unused));

            set.Add(new Opcode(0x80, Operation.lag, 2));
            set.Add(new Opcode(0x81, Operation.lag, 1));
            set.Add(new Opcode(0x82, Operation.lal, 2));
            set.Add(new Opcode(0x83, Operation.lal, 1));
            set.Add(new Opcode(0x84, Operation.lat, 2));
            set.Add(new Opcode(0x85, Operation.lat, 1));
            set.Add(new Opcode(0x86, Operation.lap, 2));
            set.Add(new Opcode(0x87, Operation.lap, 1));
            set.Add(new Opcode(0x88, Operation.lsg, 2));
            set.Add(new Opcode(0x89, Operation.lsg, 1));
            set.Add(new Opcode(0x8a, Operation.lsl, 2));
            set.Add(new Opcode(0x8b, Operation.lsl, 1));
            set.Add(new Opcode(0x8c, Operation.lst, 2));
            set.Add(new Opcode(0x8d, Operation.lst, 1));
            set.Add(new Opcode(0x8e, Operation.lsp, 2));
            set.Add(new Opcode(0x8f, Operation.lsp, 1));
            set.Add(new Opcode(0x90, Operation.lagi, 2));
            set.Add(new Opcode(0x91, Operation.lagi, 1));
            set.Add(new Opcode(0x92, Operation.lali, 2));
            set.Add(new Opcode(0x93, Operation.lali, 1));
            set.Add(new Opcode(0x94, Operation.lati, 2));
            set.Add(new Opcode(0x95, Operation.lati, 1));
            set.Add(new Opcode(0x96, Operation.lapi, 2));
            set.Add(new Opcode(0x97, Operation.lapi, 1));
            set.Add(new Opcode(0x98, Operation.lsgi, 2));
            set.Add(new Opcode(0x99, Operation.lsgi, 1));
            set.Add(new Opcode(0x9a, Operation.lsli, 2));
            set.Add(new Opcode(0x9b, Operation.lsli, 1));
            set.Add(new Opcode(0x9c, Operation.lsti, 2));
            set.Add(new Opcode(0x9d, Operation.lsti, 1));
            set.Add(new Opcode(0x9e, Operation.lspi, 2));
            set.Add(new Opcode(0x9f, Operation.lspi, 1));
            set.Add(new Opcode(0xa0, Operation.sag, 2));
            set.Add(new Opcode(0xa1, Operation.sag, 1));
            set.Add(new Opcode(0xa2, Operation.sal, 2));
            set.Add(new Opcode(0xa3, Operation.sal, 1));
            set.Add(new Opcode(0xa4, Operation.sat, 2));
            set.Add(new Opcode(0xa5, Operation.sat, 1));
            set.Add(new Opcode(0xa6, Operation.sap, 2));
            set.Add(new Opcode(0xa7, Operation.sap, 1));
            set.Add(new Opcode(0xa8, Operation.ssg, 2));
            set.Add(new Opcode(0xa9, Operation.ssg, 1));
            set.Add(new Opcode(0xaa, Operation.ssl, 2));
            set.Add(new Opcode(0xab, Operation.ssl, 1));
            set.Add(new Opcode(0xac, Operation.sst, 2));
            set.Add(new Opcode(0xad, Operation.sst, 1));
            set.Add(new Opcode(0xae, Operation.ssp, 2));
            set.Add(new Opcode(0xaf, Operation.ssp, 1));
            set.Add(new Opcode(0xb0, Operation.sagi, 2));
            set.Add(new Opcode(0xb1, Operation.sagi, 1));
            set.Add(new Opcode(0xb2, Operation.sali, 2));
            set.Add(new Opcode(0xb3, Operation.sali, 1));
            set.Add(new Opcode(0xb4, Operation.sati, 2));
            set.Add(new Opcode(0xb5, Operation.sati, 1));
            set.Add(new Opcode(0xb6, Operation.sapi, 2));
            set.Add(new Opcode(0xb7, Operation.sapi, 1));
            set.Add(new Opcode(0xb8, Operation.ssgi, 2));
            set.Add(new Opcode(0xb9, Operation.ssgi, 1));
            set.Add(new Opcode(0xba, Operation.ssli, 2));
            set.Add(new Opcode(0xbb, Operation.ssli, 1));
            set.Add(new Opcode(0xbc, Operation.ssti, 2));
            set.Add(new Opcode(0xbd, Operation.ssti, 1));
            set.Add(new Opcode(0xbe, Operation.sspi, 2));
            set.Add(new Opcode(0xbf, Operation.sspi, 1));
            set.Add(new Opcode(0xc0, Operation.plusag, 2));
            set.Add(new Opcode(0xc1, Operation.plusag, 1));
            set.Add(new Opcode(0xc2, Operation.plusal, 2));
            set.Add(new Opcode(0xc3, Operation.plusal, 1));
            set.Add(new Opcode(0xc4, Operation.plusat, 2));
            set.Add(new Opcode(0xc5, Operation.plusat, 1));
            set.Add(new Opcode(0xc6, Operation.plusap, 2));
            set.Add(new Opcode(0xc7, Operation.plusap, 1));
            set.Add(new Opcode(0xc8, Operation.plussg, 2));
            set.Add(new Opcode(0xc9, Operation.plussg, 1));
            set.Add(new Opcode(0xca, Operation.plussl, 2));
            set.Add(new Opcode(0xcb, Operation.plussl, 1));
            set.Add(new Opcode(0xcc, Operation.plusst, 2));
            set.Add(new Opcode(0xcd, Operation.plusst, 1));
            set.Add(new Opcode(0xce, Operation.plussp, 2));
            set.Add(new Opcode(0xcf, Operation.plussp, 1));
            set.Add(new Opcode(0xd0, Operation.plusagi, 2));
            set.Add(new Opcode(0xd1, Operation.plusagi, 1));
            set.Add(new Opcode(0xd2, Operation.plusali, 2));
            set.Add(new Opcode(0xd3, Operation.plusali, 1));
            set.Add(new Opcode(0xd4, Operation.plusati, 2));
            set.Add(new Opcode(0xd5, Operation.plusati, 1));
            set.Add(new Opcode(0xd6, Operation.plusapi, 2));
            set.Add(new Opcode(0xd7, Operation.plusapi, 1));
            set.Add(new Opcode(0xd8, Operation.plussgi, 2));
            set.Add(new Opcode(0xd9, Operation.plussgi, 1));
            set.Add(new Opcode(0xda, Operation.plussli, 2));
            set.Add(new Opcode(0xdb, Operation.plussli, 1));
            set.Add(new Opcode(0xdc, Operation.plussti, 2));
            set.Add(new Opcode(0xdd, Operation.plussti, 1));
            set.Add(new Opcode(0xde, Operation.plusspi, 2));
            set.Add(new Opcode(0xdf, Operation.plusspi, 1));
            set.Add(new Opcode(0xe0, Operation.minusag, 2));
            set.Add(new Opcode(0xe1, Operation.minusag, 1));
            set.Add(new Opcode(0xe2, Operation.minusal, 2));
            set.Add(new Opcode(0xe3, Operation.minusal, 1));
            set.Add(new Opcode(0xe4, Operation.minusat, 2));
            set.Add(new Opcode(0xe5, Operation.minusat, 1));
            set.Add(new Opcode(0xe6, Operation.minusap, 2));
            set.Add(new Opcode(0xe7, Operation.minusap, 1));
            set.Add(new Opcode(0xe8, Operation.minussg, 2));
            set.Add(new Opcode(0xe9, Operation.minussg, 1));
            set.Add(new Opcode(0xea, Operation.minussl, 2));
            set.Add(new Opcode(0xeb, Operation.minussl, 1));
            set.Add(new Opcode(0xec, Operation.minusst, 2));
            set.Add(new Opcode(0xed, Operation.minusst, 1));
            set.Add(new Opcode(0xee, Operation.minussp, 2));
            set.Add(new Opcode(0xef, Operation.minussp, 1));
            set.Add(new Opcode(0xf0, Operation.minusagi, 2));
            set.Add(new Opcode(0xf1, Operation.minusagi, 1));
            set.Add(new Opcode(0xf2, Operation.minusali, 2));
            set.Add(new Opcode(0xf3, Operation.minusali, 1));
            set.Add(new Opcode(0xf4, Operation.minusati, 2));
            set.Add(new Opcode(0xf5, Operation.minusati, 1));
            set.Add(new Opcode(0xf6, Operation.minusapi, 2));
            set.Add(new Opcode(0xf7, Operation.minusapi, 1));
            set.Add(new Opcode(0xf8, Operation.minussgi, 2));
            set.Add(new Opcode(0xf9, Operation.minussgi, 1));
            set.Add(new Opcode(0xfa, Operation.minussli, 2));
            set.Add(new Opcode(0xfb, Operation.minussli, 1));
            set.Add(new Opcode(0xfc, Operation.minussti, 2));
            set.Add(new Opcode(0xfd, Operation.minussti, 1));
            set.Add(new Opcode(0xfe, Operation.minusspi, 2));
            set.Add(new Opcode(0xff, Operation.minusspi, 1));

            return set;
        }

        public byte Value { get; private set; }
        public Operation Operation { get; private set; }
        public ReadOnlyCollection<byte> Operands { get; private set; } // operand sizes
        public byte Length { get; private set; } // doesn't apply to "file"

        public Opcode(byte value, Operation operation, params byte[] operands)
        {
            Value = value;
            Operation = operation;
            Operands = new ReadOnlyCollection<byte>(operands);

            // calculate instruction length. this value will be incorrect
            // for "file" as its operand is a null-terminated string.
            Length = 1;
            foreach (byte operand in operands)
            {
                Length += operand;
            }
        }

        public override string ToString()
        {
            var operands = new StringBuilder();
            foreach (byte operand in Operands)
            {
                switch (operand)
                {
                    case 1: operands.Append(" byte"); break;
                    case 2: operands.Append(" word"); break;
                    default: operands.Append(" string"); break;
                }
            }
            return string.Format("{0:X2}: {1}{2}", Value, Operation.GetName(), operands);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType()) return false;
            Opcode o = (Opcode)obj;
            return Value.Equals(o.Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }

    [Flags]
    public enum OpFlags
    {
        None = 0,

        ReadsAcc = 1,
        ReadsPprev = 2,
        PopsStack = 4,

        WritesAcc = 8,
        WritesPprev = 16,
        PushesStack = 32,

        PeeksStack = 64, // dup only
        InvalidatesPprev = 128, // calling a function (even kernel, since it sends messages)
        Branches = 256,

        // these two might not be helpful
        ReadsRest = 512,   // sending a message
        PushesRest = 1024, // rest only

        // groupings to keep the code table pretty
        Math = ReadsAcc | PopsStack | WritesAcc,
        Compare = ReadsAcc | PopsStack | WritesAcc | WritesPprev,

        // i can see querying this when going in reverse
        HasNeeds = ReadsAcc | ReadsPprev | PopsStack | PeeksStack
    }

    static class OpFlagExtensions
    {
        public static OpFlags GetFlags(this Operation operation)
        {
            return Flags[operation];
        }

        static IReadOnlyDictionary<Operation, OpFlags> Flags = new Dictionary<Operation, OpFlags>
        {
            { Operation.unused, OpFlags.None },
            { Operation.bnot, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.add, OpFlags.Math },
            { Operation.sub, OpFlags.Math },
            { Operation.mul, OpFlags.Math },
            { Operation.div, OpFlags.Math },
            { Operation.mod, OpFlags.Math },
            { Operation.shr, OpFlags.Math },
            { Operation.shl, OpFlags.Math },
            { Operation.xor, OpFlags.Math },
            { Operation.and, OpFlags.Math },
            { Operation.or, OpFlags.Math },
            { Operation.neg, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.not, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.eq, OpFlags.Compare },
            { Operation.ne, OpFlags.Compare },
            { Operation.gt, OpFlags.Compare },
            { Operation.ge, OpFlags.Compare },
            { Operation.lt, OpFlags.Compare },
            { Operation.le, OpFlags.Compare },
            { Operation.ugt, OpFlags.Compare },
            { Operation.uge, OpFlags.Compare },
            { Operation.ult, OpFlags.Compare },
            { Operation.ule, OpFlags.Compare },
            { Operation.bt, OpFlags.ReadsAcc | OpFlags.Branches },
            { Operation.bnt, OpFlags.ReadsAcc | OpFlags.Branches},
            { Operation.jmp, OpFlags.Branches },
            { Operation.ldi, OpFlags.WritesAcc },
            { Operation.push, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.pushi, OpFlags.PushesStack },
            { Operation.toss, OpFlags.PopsStack },
            { Operation.dup, OpFlags.PeeksStack | OpFlags.PushesStack },
            { Operation.link, OpFlags.None },
            { Operation.call, OpFlags.PopsStack | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.callk, OpFlags.PopsStack | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.callb, OpFlags.PopsStack | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.calle, OpFlags.PopsStack | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.ret, OpFlags.None },
            { Operation.send, OpFlags.ReadsAcc | OpFlags.PopsStack | OpFlags.ReadsRest | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.class_, OpFlags.WritesAcc },
            { Operation.self, OpFlags.PopsStack | OpFlags.ReadsRest | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.info, OpFlags.WritesAcc },
            { Operation.pushInfo, OpFlags.PushesStack },
            { Operation.superP, OpFlags.WritesAcc },
            { Operation.pushSuperP, OpFlags.PushesStack },
            { Operation.super, OpFlags.PopsStack | OpFlags.ReadsRest | OpFlags.WritesAcc | OpFlags.InvalidatesPprev },
            { Operation.rest, OpFlags.PushesRest },
            { Operation.lea, OpFlags.WritesAcc },
            { Operation.selfID, OpFlags.WritesAcc },
            { Operation.pprev, OpFlags.ReadsPprev | OpFlags.PushesStack },
            { Operation.pToa, OpFlags.WritesAcc },
            { Operation.aTop, OpFlags.ReadsAcc },
            { Operation.pTos, OpFlags.PushesStack },
            { Operation.sTop, OpFlags.PopsStack },
            { Operation.ipToa, OpFlags.WritesAcc },
            { Operation.dpToa , OpFlags.WritesAcc },
            { Operation.ipTos, OpFlags.PushesStack },
            { Operation.dpTos, OpFlags.PushesStack },
            { Operation.lofsa, OpFlags.WritesAcc },
            { Operation.lofss, OpFlags.PushesStack },
            { Operation.push0, OpFlags.PushesStack },
            { Operation.push1, OpFlags.PushesStack },
            { Operation.push2, OpFlags.PushesStack },
            { Operation.pushSelf, OpFlags.PushesStack },
            { Operation.file, OpFlags.None },
            { Operation.line, OpFlags.None },
            { Operation.loadID, OpFlags.WritesAcc },
            { Operation.pushID, OpFlags.PushesStack },
            // load
            { Operation.lag, OpFlags.WritesAcc },
            { Operation.lal, OpFlags.WritesAcc },
            { Operation.lat, OpFlags.WritesAcc },
            { Operation.lap, OpFlags.WritesAcc },
            { Operation.lsg, OpFlags.PushesStack },
            { Operation.lsl, OpFlags.PushesStack },
            { Operation.lst, OpFlags.PushesStack },
            { Operation.lsp, OpFlags.PushesStack },
            { Operation.lagi, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.lali, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.lati, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.lapi, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.lsgi, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.lsli, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.lsti, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.lspi, OpFlags.ReadsAcc | OpFlags.PushesStack },
            // store
            { Operation.sag, OpFlags.ReadsAcc },
            { Operation.sal, OpFlags.ReadsAcc },
            { Operation.sat, OpFlags.ReadsAcc },
            { Operation.sap, OpFlags.ReadsAcc },
            { Operation.ssg, OpFlags.PopsStack },
            { Operation.ssl, OpFlags.PopsStack },
            { Operation.sst, OpFlags.PopsStack },
            { Operation.ssp, OpFlags.PopsStack },
            // i will never stop being surprised by these next four
            { Operation.sagi, OpFlags.ReadsAcc | OpFlags.PopsStack | OpFlags.WritesAcc },
            { Operation.sali, OpFlags.ReadsAcc | OpFlags.PopsStack | OpFlags.WritesAcc },
            { Operation.sati, OpFlags.ReadsAcc | OpFlags.PopsStack | OpFlags.WritesAcc },
            { Operation.sapi, OpFlags.ReadsAcc | OpFlags.PopsStack | OpFlags.WritesAcc },
            { Operation.ssgi, OpFlags.ReadsAcc | OpFlags.PopsStack },
            { Operation.ssli, OpFlags.ReadsAcc | OpFlags.PopsStack },
            { Operation.ssti, OpFlags.ReadsAcc | OpFlags.PopsStack },
            { Operation.sspi, OpFlags.ReadsAcc | OpFlags.PopsStack },
            // plus
            { Operation.plusag, OpFlags.WritesAcc },
            { Operation.plusal, OpFlags.WritesAcc },
            { Operation.plusat, OpFlags.WritesAcc },
            { Operation.plusap, OpFlags.WritesAcc },
            { Operation.plussg, OpFlags.PushesStack },
            { Operation.plussl, OpFlags.PushesStack },
            { Operation.plusst, OpFlags.PushesStack },
            { Operation.plussp, OpFlags.PushesStack },
            { Operation.plusagi, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.plusali, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.plusati, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.plusapi, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.plussgi, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.plussli, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.plussti, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.plusspi, OpFlags.ReadsAcc | OpFlags.PushesStack },
            // minus
            { Operation.minusag, OpFlags.WritesAcc },
            { Operation.minusal, OpFlags.WritesAcc },
            { Operation.minusat, OpFlags.WritesAcc },
            { Operation.minusap, OpFlags.WritesAcc },
            { Operation.minussg, OpFlags.PushesStack },
            { Operation.minussl, OpFlags.PushesStack },
            { Operation.minusst, OpFlags.PushesStack },
            { Operation.minussp, OpFlags.PushesStack },
            { Operation.minusagi, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.minusali, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.minusati, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.minusapi, OpFlags.ReadsAcc | OpFlags.WritesAcc },
            { Operation.minussgi, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.minussli, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.minussti, OpFlags.ReadsAcc | OpFlags.PushesStack },
            { Operation.minusspi, OpFlags.ReadsAcc | OpFlags.PushesStack },
        };
    }
}

