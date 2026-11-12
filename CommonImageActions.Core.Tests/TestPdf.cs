using SkiaSharp;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CommonImageActions.Core.Tests
{
    /// <summary>
    /// Builds a small pdf in memory where every page is filled with one colour, optionally
    /// protected with a password. Knowing the colour of each page lets a test tell which page was
    /// rendered, and whether a protected file was really opened.
    /// </summary>
    public static class TestPdf
    {
        public const int PageWidth = 200;
        public const int PageHeight = 100;

        //the fixed string the pdf standard uses to pad passwords to 32 bytes
        private static readonly byte[] PasswordPadding =
        {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
        };

        //what the user is allowed to do with the file, as the pdf standard encodes it
        private const int Permissions = -44;

        /// <summary>
        /// A pdf with one page for each colour.
        /// </summary>
        public static byte[] Create(params SKColor[] pageColors)
        {
            return Build(null, pageColors);
        }

        /// <summary>
        /// A pdf that needs the password to be opened.
        /// </summary>
        public static byte[] CreateProtected(string password, params SKColor[] pageColors)
        {
            return Build(password, pageColors);
        }

        private static byte[] Build(string? password, SKColor[] pageColors)
        {
            if (pageColors.Length == 0)
            {
                pageColors = new[] { TestHelpers.Red };
            }

            //the file id is part of how the encryption key is made
            var fileId = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
            byte[]? ownerEntry = null;
            byte[]? userEntry = null;
            byte[]? encryptionKey = null;
            if (password != null)
            {
                ownerEntry = Rc4(FirstBytes(Md5(Pad("owner password")), 5), Pad(password));
                encryptionKey = FirstBytes(Md5(Concat(Pad(password), ownerEntry, BitConverter.GetBytes(Permissions), fileId)), 5);
                userEntry = Rc4(encryptionKey, PasswordPadding);
            }

            //object 1 is the catalog, 2 is the list of pages, then a page and its drawing for each colour
            var objects = new List<byte[]>();
            var pageNumbers = Enumerable.Range(0, pageColors.Length).Select(i => 3 + (i * 2)).ToList();
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(Ascii($"<< /Type /Pages /Kids [{string.Join(" ", pageNumbers.Select(x => $"{x} 0 R"))}] /Count {pageColors.Length} >>"));

            for (var i = 0; i < pageColors.Length; i++)
            {
                var pageNumber = pageNumbers[i];
                var contentNumber = pageNumber + 1;
                objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Contents {contentNumber} 0 R /Resources << >> >>"));

                //set the fill colour, then fill a rectangle the size of the page
                var color = pageColors[i];
                var drawing = Ascii(string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} rg 0 0 {3} {4} re f",
                    color.Red / 255.0, color.Green / 255.0, color.Blue / 255.0, PageWidth, PageHeight));

                //in a protected file the drawing is encrypted with a key made for that one object
                if (encryptionKey != null)
                {
                    var objectKey = Md5(Concat(encryptionKey, new[] { (byte)contentNumber, (byte)(contentNumber >> 8), (byte)(contentNumber >> 16), (byte)0, (byte)0 }));
                    drawing = Rc4(FirstBytes(objectKey, encryptionKey.Length + 5), drawing);
                }

                objects.Add(Concat(Ascii($"<< /Length {drawing.Length} >>\nstream\n"), drawing, Ascii("\nendstream")));
            }

            var encryptNumber = 0;
            if (encryptionKey != null)
            {
                encryptNumber = objects.Count + 1;
                objects.Add(Ascii($"<< /Filter /Standard /V 1 /R 2 /O <{Hex(ownerEntry!)}> /U <{Hex(userEntry!)}> /P {Permissions} >>"));
            }

            //write the objects out, remembering where each one starts for the table at the end
            using var stream = new MemoryStream();
            void Write(byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

            Write(Ascii("%PDF-1.4\n"));
            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(stream.Position);
                Write(Ascii($"{i + 1} 0 obj\n"));
                Write(objects[i]);
                Write(Ascii("\nendobj\n"));
            }

            var tableOffset = stream.Position;
            Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
            foreach (var offset in offsets)
            {
                Write(Ascii($"{offset:0000000000} 00000 n \n"));
            }

            var encrypt = encryptionKey != null ? $" /Encrypt {encryptNumber} 0 R" : string.Empty;
            Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{encrypt} /ID [<{Hex(fileId)}> <{Hex(fileId)}>] >>\nstartxref\n{tableOffset}\n%%EOF\n"));
            return stream.ToArray();
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(x => x.ToString("X2")));

        private static byte[] Md5(byte[] data)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(data);
        }

        private static byte[] FirstBytes(byte[] data, int count) => data.Take(Math.Min(count, data.Length)).ToArray();

        private static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

        /// <summary>
        /// A password cut or padded to exactly 32 bytes.
        /// </summary>
        private static byte[] Pad(string password)
        {
            return FirstBytes(Concat(Encoding.Latin1.GetBytes(password), PasswordPadding), 32);
        }

        /// <summary>
        /// The RC4 cipher, which is what a pdf with this level of protection is encrypted with.
        /// .net does not include it. Encrypting and decrypting are the same operation.
        /// </summary>
        private static byte[] Rc4(byte[] key, byte[] data)
        {
            var state = Enumerable.Range(0, 256).Select(x => (byte)x).ToArray();
            var j = 0;
            for (var i = 0; i < 256; i++)
            {
                j = (j + state[i] + key[i % key.Length]) & 0xFF;
                (state[i], state[j]) = (state[j], state[i]);
            }

            var result = new byte[data.Length];
            var a = 0;
            var b = 0;
            for (var n = 0; n < data.Length; n++)
            {
                a = (a + 1) & 0xFF;
                b = (b + state[a]) & 0xFF;
                (state[a], state[b]) = (state[b], state[a]);
                result[n] = (byte)(data[n] ^ state[(state[a] + state[b]) & 0xFF]);
            }

            return result;
        }
    }
}
