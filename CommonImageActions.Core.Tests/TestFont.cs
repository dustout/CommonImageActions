using System.Text;

namespace CommonImageActions.Core.Tests
{
    /// <summary>
    /// Builds a small TrueType font in memory, so that tests which draw text do not depend on
    /// which fonts are installed on the machine (a build server may have none at all).
    /// Every printable character is a solid rectangle, apart from the capital L which is an L
    /// shape (so a test can tell which way up it was drawn) and the space which is empty.
    /// </summary>
    public static class TestFont
    {
        /// <summary>
        /// The name the font is registered under, use it as <see cref="ImageActions.FontFamily"/>.
        /// </summary>
        public const string FamilyName = "CommonImageActionsTestFont";

        private const int FirstCharacter = 0x20;
        private const int LastCharacter = 0x7E;
        private const short UnitsPerEm = 1000;
        private const short Ascender = 800;
        private const short Descender = -200;
        private const ushort AdvanceWidth = 800;

        private static readonly object RegisterLock = new object();
        private static bool _isRegistered;

        /// <summary>
        /// Make the font available to <see cref="ImageProcessor"/>. Safe to call from every test.
        /// </summary>
        public static void Register()
        {
            lock (RegisterLock)
            {
                if (_isRegistered == false)
                {
                    ImageProcessor.RegisterFont(FamilyName, Create());
                    _isRegistered = true;
                }
            }
        }

        public static byte[] Create()
        {
            //glyph 0 is the "missing character" glyph, then one glyph for each printable ascii character
            var glyphCount = 1 + (LastCharacter - FirstCharacter + 1);

            //outlines are drawn clockwise, in units where 1000 is the height of a line of text
            var box = Glyph((100, 0), (100, 700), (700, 700), (700, 0));
            var letterL = Glyph((100, 0), (100, 700), (300, 700), (300, 200), (700, 200), (700, 0));

            var glyf = new Writer();
            var loca = new Writer();
            var hmtx = new Writer();
            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                var character = glyph == 0 ? '\0' : (char)(FirstCharacter + glyph - 1);
                var outline = glyph == 0 || character == ' ' ? new byte[0] : character == 'L' ? letterL : box;

                loca.UInt32((uint)glyf.Length);
                glyf.Bytes(outline);

                hmtx.UInt16(AdvanceWidth);
                hmtx.Int16((short)(outline.Length == 0 ? 0 : 100));
            }
            loca.UInt32((uint)glyf.Length);

            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["OS/2"] = Os2(),
                ["cmap"] = Cmap(),
                ["glyf"] = glyf.ToArray(),
                ["head"] = Head(),
                ["hhea"] = Hhea(glyphCount),
                ["hmtx"] = hmtx.ToArray(),
                ["loca"] = loca.ToArray(),
                ["maxp"] = Maxp(glyphCount),
                ["name"] = Name(),
                ["post"] = Post(),
            };

            return Assemble(tables);
        }

        /// <summary>
        /// One glyph made of a single closed shape with straight edges.
        /// </summary>
        private static byte[] Glyph(params (short X, short Y)[] points)
        {
            var writer = new Writer();
            writer.Int16(1); //number of shapes
            writer.Int16(points.Min(p => p.X));
            writer.Int16(points.Min(p => p.Y));
            writer.Int16(points.Max(p => p.X));
            writer.Int16(points.Max(p => p.Y));
            writer.UInt16((ushort)(points.Length - 1)); //the last point of the shape
            writer.UInt16(0); //no hinting instructions

            //every point is on the outline, and its position is stored as two bytes
            foreach (var point in points)
            {
                writer.Byte(0x01);
            }

            //positions are stored as the distance from the previous point
            short previous = 0;
            foreach (var point in points)
            {
                writer.Int16((short)(point.X - previous));
                previous = point.X;
            }

            previous = 0;
            foreach (var point in points)
            {
                writer.Int16((short)(point.Y - previous));
                previous = point.Y;
            }

            writer.PadToFourBytes();
            return writer.ToArray();
        }

        /// <summary>
        /// Maps characters to glyphs: the printable ascii characters are glyphs 1 onwards, in order.
        /// </summary>
        private static byte[] Cmap()
        {
            var writer = new Writer();
            writer.UInt16(0); //version
            writer.UInt16(1); //one mapping
            writer.UInt16(3); //windows
            writer.UInt16(1); //unicode
            writer.UInt32(12); //where the mapping starts

            //a "format 4" mapping with one range of characters, and the end marker the format needs
            writer.UInt16(4);
            writer.UInt16(32); //length of this mapping
            writer.UInt16(0); //language
            writer.UInt16(4); //two ranges, times two
            writer.UInt16(4); //search range
            writer.UInt16(1); //entry selector
            writer.UInt16(0); //range shift
            writer.UInt16(LastCharacter);
            writer.UInt16(0xFFFF);
            writer.UInt16(0); //padding
            writer.UInt16(FirstCharacter);
            writer.UInt16(0xFFFF);
            writer.Int16((short)(1 - FirstCharacter)); //add this to a character to get its glyph
            writer.Int16(1);
            writer.UInt16(0);
            writer.UInt16(0);
            return writer.ToArray();
        }

        private static byte[] Head()
        {
            var writer = new Writer();
            writer.UInt32(0x00010000); //version
            writer.UInt32(0x00010000); //font revision
            writer.UInt32(0); //checksum adjustment, filled in once the whole font is assembled
            writer.UInt32(0x5F0F3CF5); //magic number
            writer.UInt16(0x0003); //flags
            writer.UInt16((ushort)UnitsPerEm);
            writer.UInt32(0); writer.UInt32(0); //created
            writer.UInt32(0); writer.UInt32(0); //modified
            writer.Int16(100); //bounds of all glyphs
            writer.Int16(0);
            writer.Int16(700);
            writer.Int16(700);
            writer.UInt16(0); //style
            writer.UInt16(8); //smallest readable size
            writer.Int16(2); //direction hint
            writer.Int16(1); //loca table uses four byte offsets
            writer.Int16(0); //glyph data format
            return writer.ToArray();
        }

        private static byte[] Hhea(int glyphCount)
        {
            var writer = new Writer();
            writer.UInt32(0x00010000);
            writer.Int16(Ascender);
            writer.Int16(Descender);
            writer.Int16(0); //line gap
            writer.UInt16(AdvanceWidth);
            writer.Int16(0); //min left side bearing
            writer.Int16(100); //min right side bearing
            writer.Int16(700); //furthest right any glyph reaches
            writer.Int16(1); //caret slope rise
            writer.Int16(0); //caret slope run
            writer.Int16(0); //caret offset
            writer.Int16(0); writer.Int16(0); writer.Int16(0); writer.Int16(0); //reserved
            writer.Int16(0); //metric data format
            writer.UInt16((ushort)glyphCount);
            return writer.ToArray();
        }

        private static byte[] Maxp(int glyphCount)
        {
            var writer = new Writer();
            writer.UInt32(0x00010000);
            writer.UInt16((ushort)glyphCount);
            writer.UInt16(6); //most points in a glyph
            writer.UInt16(1); //most shapes in a glyph
            writer.UInt16(0); writer.UInt16(0); //composite glyphs
            writer.UInt16(2); //zones
            writer.UInt16(0); writer.UInt16(0); writer.UInt16(0); writer.UInt16(0);
            writer.UInt16(0); writer.UInt16(0); writer.UInt16(0); writer.UInt16(0);
            return writer.ToArray();
        }

        private static byte[] Name()
        {
            //name ids: 1 family, 2 style, 3 unique id, 4 full name, 5 version, 6 postscript name
            var names = new (ushort Id, string Value)[]
            {
                (1, FamilyName),
                (2, "Regular"),
                (3, FamilyName + " Regular"),
                (4, FamilyName),
                (5, "Version 1.0"),
                (6, FamilyName),
            };

            var strings = new Writer();
            var records = new Writer();
            foreach (var name in names)
            {
                var value = Encoding.BigEndianUnicode.GetBytes(name.Value);
                records.UInt16(3); //windows
                records.UInt16(1); //unicode
                records.UInt16(0x0409); //english
                records.UInt16(name.Id);
                records.UInt16((ushort)value.Length);
                records.UInt16((ushort)strings.Length);
                strings.Bytes(value);
            }

            var writer = new Writer();
            writer.UInt16(0); //format
            writer.UInt16((ushort)names.Length);
            writer.UInt16((ushort)(6 + records.Length)); //where the strings start
            writer.Bytes(records.ToArray());
            writer.Bytes(strings.ToArray());
            return writer.ToArray();
        }

        private static byte[] Post()
        {
            var writer = new Writer();
            writer.UInt32(0x00030000); //version 3, no glyph names
            writer.UInt32(0); //italic angle
            writer.Int16(-100); //underline position
            writer.Int16(50); //underline thickness
            writer.UInt32(0); //not fixed pitch
            writer.UInt32(0); writer.UInt32(0); writer.UInt32(0); writer.UInt32(0); //memory hints
            return writer.ToArray();
        }

        private static byte[] Os2()
        {
            var writer = new Writer();
            writer.UInt16(3); //version
            writer.Int16((short)AdvanceWidth); //average character width
            writer.UInt16(400); //weight
            writer.UInt16(5); //width
            writer.UInt16(0); //embedding is allowed
            writer.Int16(650); writer.Int16(600); writer.Int16(0); writer.Int16(75); //subscript
            writer.Int16(650); writer.Int16(600); writer.Int16(0); writer.Int16(350); //superscript
            writer.Int16(50); writer.Int16(300); //strikeout
            writer.Int16(0); //family class
            for (var i = 0; i < 10; i++)
            {
                writer.Byte(0); //panose
            }
            writer.UInt32(1); writer.UInt32(0); writer.UInt32(0); writer.UInt32(0); //unicode ranges, basic latin
            writer.Byte((byte)'T'); writer.Byte((byte)'E'); writer.Byte((byte)'S'); writer.Byte((byte)'T'); //vendor
            writer.UInt16(0x0040); //regular
            writer.UInt16(FirstCharacter);
            writer.UInt16(LastCharacter);
            writer.Int16(Ascender);
            writer.Int16(Descender);
            writer.Int16(0); //line gap
            writer.UInt16((ushort)Ascender);
            writer.UInt16((ushort)(-Descender));
            writer.UInt32(1); writer.UInt32(0); //code pages, latin 1
            writer.Int16(500); //height of a lower case x
            writer.Int16(700); //height of a capital
            writer.UInt16(0); //default character
            writer.UInt16(FirstCharacter); //break character
            writer.UInt16(0); //max context
            return writer.ToArray();
        }

        /// <summary>
        /// Put the tables together into a font file, with the directory and checksums it needs.
        /// </summary>
        private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
        {
            var tableCount = tables.Count;
            var largestPowerOfTwo = 1;
            var entrySelector = 0;
            while (largestPowerOfTwo * 2 <= tableCount)
            {
                largestPowerOfTwo *= 2;
                entrySelector++;
            }

            var writer = new Writer();
            writer.UInt32(0x00010000); //truetype outlines
            writer.UInt16((ushort)tableCount);
            writer.UInt16((ushort)(largestPowerOfTwo * 16));
            writer.UInt16((ushort)entrySelector);
            writer.UInt16((ushort)((tableCount * 16) - (largestPowerOfTwo * 16)));

            //the directory comes first and says where each table is
            var offset = 12 + (tableCount * 16);
            var headOffset = 0;
            foreach (var table in tables)
            {
                if (table.Key == "head")
                {
                    headOffset = offset;
                }

                writer.Bytes(Encoding.ASCII.GetBytes(table.Key));
                writer.UInt32(Checksum(table.Value));
                writer.UInt32((uint)offset);
                writer.UInt32((uint)table.Value.Length);
                offset += (table.Value.Length + 3) / 4 * 4;
            }

            foreach (var table in tables)
            {
                writer.Bytes(table.Value);
                writer.PadToFourBytes();
            }

            //the checksum of the whole file is stored inside the head table
            var font = writer.ToArray();
            var adjustment = 0xB1B0AFBA - Checksum(font);
            font[headOffset + 8] = (byte)(adjustment >> 24);
            font[headOffset + 9] = (byte)(adjustment >> 16);
            font[headOffset + 10] = (byte)(adjustment >> 8);
            font[headOffset + 11] = (byte)adjustment;
            return font;
        }

        private static uint Checksum(byte[] data)
        {
            uint sum = 0;
            for (var i = 0; i < data.Length; i += 4)
            {
                uint value = 0;
                for (var j = 0; j < 4; j++)
                {
                    value = (value << 8) | (i + j < data.Length ? data[i + j] : (uint)0);
                }

                sum = unchecked(sum + value);
            }

            return sum;
        }

        /// <summary>
        /// Font files store numbers with the most significant byte first.
        /// </summary>
        private class Writer
        {
            private readonly MemoryStream _stream = new MemoryStream();

            public int Length => (int)_stream.Length;

            public void Byte(byte value) => _stream.WriteByte(value);

            public void Bytes(byte[] value) => _stream.Write(value, 0, value.Length);

            public void UInt16(ushort value)
            {
                _stream.WriteByte((byte)(value >> 8));
                _stream.WriteByte((byte)value);
            }

            public void Int16(short value) => UInt16(unchecked((ushort)value));

            public void UInt32(uint value)
            {
                UInt16((ushort)(value >> 16));
                UInt16((ushort)value);
            }

            public void PadToFourBytes()
            {
                while (_stream.Length % 4 != 0)
                {
                    _stream.WriteByte(0);
                }
            }

            public byte[] ToArray() => _stream.ToArray();
        }
    }
}
