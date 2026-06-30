using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SCI.Resource
{
    // Spans default to unknown endian which acts like little endian.
    // SCI is usually little, and it's helpful to know when we're using
    // little because we know and when we just don't know any better.
    public enum Endian
    {
        Unknown,
        Little,
        Big
    }

    public class Span : IEnumerable<byte>
    {
        readonly byte[] array;
        readonly int start;
        readonly int length;

        public int Length { get { return length; } }
        public Endian Endian { get; set; } // mutable; we might change our mind (auto-detect, etc)

        public byte[] Array { get { return array; } }
        public int Start { get { return start; } }

        public Span(byte[] array, Endian endian = Endian.Unknown)
        {
            this.array = array;
            this.start = 0;
            this.length = array.Length;
            Endian = endian;
        }

        public Span(byte[] array, int start, Endian endian = Endian.Unknown)
        {
            this.array = array;
            this.start = start;
            this.length = array.Length - start;
            ValidateInitialState();
            Endian = endian;
        }

        public Span(byte[] array, int start, int length, Endian endian = Endian.Unknown)
        {
            this.array = array;
            this.start = start;
            this.length = length;
            ValidateInitialState();
            Endian = endian;
        }

        public Span(string fileName, Endian endian = Endian.Unknown)
        {
            this.array = File.ReadAllBytes(fileName);
            this.start = 0;
            this.length = array.Length;
            Endian = endian;
        }

        public Span Slice(int start)
        {
            if (!(0 <= start && start <= Length)) throw new ArgumentOutOfRangeException("start");

            return new Span(array, this.start + start, length - start, Endian);
        }

        public Span Slice(int start, int length)
        {
            if (!(0 <= start && start <= Length)) throw new ArgumentOutOfRangeException("start");
            if (!(start + length <= Length)) throw new ArgumentOutOfRangeException("length");

            return new Span(array, this.start + start, length, Endian);
        }

        void ValidateInitialState()
        {
            if (!(0 <= start && start <= array.Length)) throw new ArgumentOutOfRangeException("start");
            if (!(0 <= length && start + length <= array.Length)) throw new ArgumentOutOfRangeException("length");
        }

        public byte this[int i]
        {
            get
            {
                ValidatePosition(i, 1);
                return array[start + i];
            }
            set
            {
                ValidatePosition(i, 1);
                array[start + i] = value;
            }
        }

        public byte this [UInt32 i]
        {
            get { return this[(int)i]; }
            set { this[(int)i] = value; }
        }

        void ValidatePosition(int pos, int length)
        {
            if (!(0 <= pos && pos + length <= Length)) throw new IndexOutOfRangeException();
        }

        public byte[] ToArray()
        {
            var newArray = new byte[Length];
            System.Array.Copy(array, start, newArray, 0, Length);
            return newArray;
        }

        public byte GetByte(int pos)
        {
            return this[pos];
        }

        public Int16 GetInt16LE(int pos)
        {
            ValidatePosition(pos, 2);
            int i = start + pos;
            return (Int16)(array[i] | (array[i + 1] << 8));
        }

        public Int16 GetInt16BE(int pos)
        {
            ValidatePosition(pos, 2);
            int i = start + pos;
            return (Int16)((array[i] << 8) | array[i + 1]);
        }

        public Int16 GetInt16(int pos)
        {
            ValidatePosition(pos, 2);
            int i = start + pos;
            if (Endian != Endian.Big)
            {
                return (Int16)(array[i] | (array[i + 1] << 8));
            }
            else
            {
                return (Int16)((array[i] << 8) | array[i + 1]);
            }
        }

        public UInt16 GetUInt16LE(int pos)
        {
            ValidatePosition(pos, 2);
            int i = start + pos;
            return (UInt16)(array[i] | (array[i + 1] << 8));
        }

        public UInt16 GetUInt16BE(int pos)
        {
            ValidatePosition(pos, 2);
            int i = start + pos;
            return (UInt16)((array[i] << 8) | array[i + 1]);
        }

        public UInt16 GetUInt16(int pos)
        {
            ValidatePosition(pos, 2);
            int i = start + pos;
            if (Endian != Endian.Big)
            {
                return (UInt16)(array[i] | (array[i + 1] << 8));
            }
            else
            {
                return (UInt16)((array[i] << 8) | array[i + 1]);
            }
        }

        public Int32 GetInt32LE(int pos)
        {
            ValidatePosition(pos, 4);
            int i = start + pos;
            return (Int32)(array[i] | (array[i + 1] << 8) | (array[i + 2] << 16) | (array[i + 3] << 24));
        }

        public Int32 GetInt32BE(int pos)
        {
            ValidatePosition(pos, 4);
            int i = start + pos;
            return (Int32)((array[i] << 24) | (array[i + 1] << 16) | (array[i + 2] << 8) | array[i + 3]);
        }

        public Int32 GetInt32(int pos)
        {
            ValidatePosition(pos, 4);
            int i = start + pos;
            if (Endian != Endian.Big)
            {
                return (Int32)(array[i] | (array[i + 1] << 8) | (array[i + 2] << 16) | (array[i + 3] << 24));
            }
            else
            {
                return (Int32)((array[i] << 24) | (array[i + 1] << 16) | (array[i + 2] << 8) | array[i + 3]);
            }
        }

        public UInt32 GetUInt32LE(int pos)
        {
            ValidatePosition(pos, 4);
            int i = start + pos;
            return (UInt32)(array[i] | (array[i + 1] << 8) | (array[i + 2] << 16) | (array[i + 3] << 24));
        }

        public UInt32 GetUInt32BE(int pos)
        {
            ValidatePosition(pos, 4);
            int i = start + pos;
            return (UInt32)((array[i] << 24) | (array[i + 1] << 16) | (array[i + 2] << 8) | array[i + 3]);
        }

        public UInt32 GetUInt32(int pos)
        {
            ValidatePosition(pos, 4);
            int i = start + pos;
            if (Endian != Endian.Big)
            {
                return (UInt32)(array[i] | (array[i + 1] << 8) | (array[i + 2] << 16) | (array[i + 3] << 24));
            }
            else
            {
                return (UInt32)((array[i] << 24) | (array[i + 1] << 16) | (array[i + 2] << 8) | array[i + 3]);
            }
        }

        // returns a potentially null terminated string up to the specified length
        public string GetString(int pos, int length)
        {
            var s = new StringBuilder(length);
            for (int i = 0; i < length; ++i)
            {
                var b = this[pos + i];
                if (b == 0)
                {
                    break;
                }
                s.Append((char)b);
            }
            return s.ToString();
        }

        public void CopyTo(Span destination, int destinationIndex, int length)
        {
            System.Array.Copy(
                array,
                start,
                destination.array,
                destination.start + destinationIndex,
                length);
        }

        public void CopyTo(byte[] destination, int destinationIndex, int length)
        {
            System.Array.Copy(
                array,
                start,
                destination,
                destinationIndex,
                length);
        }

        public void CopyTo(int sourceIndex, Span destination, int destinationIndex, int length)
        {
            System.Array.Copy(
                array,
                start + sourceIndex, 
                destination.array,
                destination.start + destinationIndex,
                length);
        }

        public void CopyTo(int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            System.Array.Copy(
                array,
                start + sourceIndex,
                destination,
                destinationIndex,
                length);
        }

        public IEnumerator<byte> GetEnumerator()
        {
            return array.Skip(start).Take(Length).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }

    //
    // SpanStream - a light stream wrapper for Span
    //

    public class SpanStream
    {
        readonly Span data;
        int pos = 0;

        public SpanStream(byte[] data, Endian endian = Endian.Unknown)
        {
            this.data = new Span(data, endian);
        }

        public SpanStream(Span data)
        {
            this.data = data;
        }

        public Span Data { get { return data; } }

        public int Position { get { return pos; } }

        public UInt16 Position16
        {
            get
            {
                if (!(UInt16.MinValue <= pos && pos <= UInt16.MaxValue))
                {
                    throw new Exception("Position16 is out of bounds");
                }
                return (UInt16)pos;
            }
        }

        public int Length {  get { return data.Length; } }

        public bool EOF { get { return pos >= data.Length; } }

        public void Seek(int pos)
        {
            if (!(0 <= pos && pos <= data.Length)) throw new ArgumentOutOfRangeException("pos");
            this.pos = pos;
        }

        public void Seek(uint pos)
        {
            Seek((int)pos);
        }

        public void Seek(long pos)
        {
            Seek((int)pos);
        }

        public void SeekToAlignment(int alignment)
        {
            int bytesOver = pos % alignment;
            if (bytesOver != 0)
            {
                int newPos = pos + (alignment - bytesOver);
                Seek(newPos);
            }
        }

        public void Skip(int distance)
        {
            Seek(this.pos + distance);
        }

        public byte ReadByte()
        {
            byte b = data[pos];
            pos += 1;
            return b;
        }

        public Int16 ReadInt16LE()
        {
            Int16 n = data.GetInt16LE(pos);
            pos += 2;
            return n;
        }

        public Int16 ReadInt16BE()
        {
            Int16 n = data.GetInt16BE(pos);
            pos += 2;
            return n;
        }

        public Int16 ReadInt16()
        {
            Int16 n = data.GetInt16(pos);
            pos += 2;
            return n;
        }

        public UInt16 ReadUInt16LE()
        {
            UInt16 n = data.GetUInt16LE(pos);
            pos += 2;
            return n;
        }

        public UInt16 ReadUInt16BE()
        {
            UInt16 n = data.GetUInt16BE(pos);
            pos += 2;
            return n;
        }

        public UInt16 ReadUInt16()
        {
            UInt16 n = data.GetUInt16(pos);
            pos += 2;
            return n;
        }

        public Int32 ReadInt32LE()
        {
            Int32 n = data.GetInt32LE(pos);
            pos += 4;
            return n;
        }

        public Int32 ReadInt32BE()
        {
            Int32 n = data.GetInt32BE(pos);
            pos += 4;
            return n;
        }

        public Int32 ReadInt32()
        {
            Int32 n = data.GetInt32(pos);
            pos += 4;
            return n;
        }

        public UInt32 ReadUInt32LE()
        {
            UInt32 n = data.GetUInt32LE(pos);
            pos += 4;
            return n;
        }

        public UInt32 ReadUInt32BE()
        {
            UInt32 n = data.GetUInt32BE(pos);
            pos += 4;
            return n;
        }

        public UInt32 ReadUInt32()
        {
            UInt32 n = data.GetUInt32(pos);
            pos += 4;
            return n;
        }

        public Span ReadBytes(int length)
        {
            var slice = data.Slice(pos, length);
            pos += length;
            return slice;
        }

        public Span ReadBytes(uint length)
        {
            return ReadBytes((int)length);
        }

        public string ReadString()
        {
            var s = new StringBuilder();
            byte b;
            while ((b = ReadByte()) != 0)
            {
                s.Append((char)b);
            }
            return s.ToString();
        }

        public string ReadString(int length)
        {
            var s = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                byte b = ReadByte();
                s.Append((char)b);
            }
            // trim trailing zeros
            while (s.Length > 0 && s[s.Length - 1] == '\0')
            {
                s.Length--;
            }
            return s.ToString();
        }

        public byte GetByte(int pos)
        {
            return data[pos];
        }

        public Int16 GetInt16LE(int pos)
        {
            return data.GetInt16LE(pos);
        }

        public Int16 GetInt16BE(int pos)
        {
            return data.GetInt16BE(pos);
        }

        public Int16 GetInt16(int pos)
        {
            return data.GetInt16(pos);
        }

        public UInt16 GetUInt16LE(int pos)
        {
            return data.GetUInt16LE(pos);
        }

        public UInt16 GetUInt16BE(int pos)
        {
            return data.GetUInt16BE(pos);
        }

        public UInt16 GetUInt16(int pos)
        {
            return data.GetUInt16(pos);
        }

        public Int32 GetInt32LE(int pos)
        {
            return data.GetInt32LE(pos);
        }

        public Int32 GetInt32BE(int pos)
        {
            return data.GetInt32BE(pos);
        }

        public Int32 GetInt32(int pos)
        {
            return data.GetInt32(pos);
        }

        public UInt32 GetUInt32LE(int pos)
        {
            return data.GetUInt32LE(pos);
        }

        public UInt32 GetUInt32BE(int pos)
        {
            return data.GetUInt32BE(pos);
        }

        public UInt32 GetUInt32(int pos)
        {
            return data.GetUInt32(pos);
        }
    }
}
