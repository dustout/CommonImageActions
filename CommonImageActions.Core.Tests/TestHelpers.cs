using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommonImageActions.Core.Tests
{
    public static class TestHelpers
    {
        public static bool IsImage(byte[] imageData)
        {
            using var stream = new MemoryStream(imageData);
            using var image = SKBitmap.Decode(stream);
            return image != null;
        }

        /// <summary>
        /// Create a solid colour image of the given size, so tests can use images of known dimensions.
        /// </summary>
        public static byte[] CreateImage(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png, SKColor? color = null)
        {
            using var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(color ?? SKColors.Red);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 90);
            return data.ToArray();
        }

        public static readonly SKColor Red = new SKColor(255, 0, 0);
        public static readonly SKColor Green = new SKColor(0, 255, 0);
        public static readonly SKColor Blue = new SKColor(0, 0, 255);
        public static readonly SKColor Yellow = new SKColor(255, 255, 0);

        /// <summary>
        /// Create an image split into four blocks of colour: red top left, green top right,
        /// blue bottom left and yellow bottom right. Which colour ends up where shows what a
        /// crop, flip or rotation did to the image.
        /// </summary>
        public static byte[] CreateQuadrantImage(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
        {
            using var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                var halfWidth = width / 2f;
                var halfHeight = height / 2f;
                using var paint = new SKPaint();

                paint.Color = Red;
                canvas.DrawRect(new SKRect(0, 0, halfWidth, halfHeight), paint);
                paint.Color = Green;
                canvas.DrawRect(new SKRect(halfWidth, 0, width, halfHeight), paint);
                paint.Color = Blue;
                canvas.DrawRect(new SKRect(0, halfHeight, halfWidth, height), paint);
                paint.Color = Yellow;
                canvas.DrawRect(new SKRect(halfWidth, halfHeight, width, height), paint);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 100);
            return data.ToArray();
        }

        /// <summary>
        /// Add an exif orientation to a jpeg. A camera stores the picture the way the sensor saw
        /// it and uses this number (1 to 8) to say how it has to be turned or mirrored to be the
        /// right way up.
        /// </summary>
        public static byte[] WithExifOrientation(byte[] jpeg, int orientation)
        {
            //exif data is a small tiff file: a header, then a list of tags. This one has a single tag
            var exif = new byte[]
            {
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
                (byte)'I', (byte)'I', 0x2A, 0x00, //little endian tiff
                0x08, 0x00, 0x00, 0x00, //the tags start 8 bytes in
                0x01, 0x00, //one tag
                0x12, 0x01, //the orientation tag
                0x03, 0x00, //its value is a short
                0x01, 0x00, 0x00, 0x00, //one of them
                (byte)orientation, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00 //no more lists of tags
            };

            //it goes into the jpeg as its own segment, straight after the two bytes that start the file
            var length = exif.Length + 2;
            var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(exif).ToArray();
            return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
        }

        /// <summary>
        /// A solid colour bmp. Skia can read a bmp but can not write one, so it is built by hand.
        /// </summary>
        public static byte[] CreateBmp(int width, int height, SKColor color)
        {
            //each row is padded to a multiple of four bytes
            var rowSize = ((width * 3) + 3) / 4 * 4;
            var pixelBytes = rowSize * height;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(54 + pixelBytes); //file size
            writer.Write(0);
            writer.Write(54); //where the pixels start
            writer.Write(40); //size of the header that follows
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1); //colour planes
            writer.Write((short)24); //bits per pixel
            writer.Write(0); //not compressed
            writer.Write(pixelBytes);
            writer.Write(2835); //pixels per metre
            writer.Write(2835);
            writer.Write(0);
            writer.Write(0);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    writer.Write(color.Blue);
                    writer.Write(color.Green);
                    writer.Write(color.Red);
                }

                for (var padding = width * 3; padding < rowSize; padding++)
                {
                    writer.Write((byte)0);
                }
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// A solid colour ico (a windows icon), which is a small header wrapped around a png.
        /// </summary>
        public static byte[] CreateIco(int size, SKColor color)
        {
            var png = CreateImage(size, size, SKEncodedImageFormat.Png, color);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((short)0);
            writer.Write((short)1); //this is an icon
            writer.Write((short)1); //holding one image
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0); //no palette
            writer.Write((byte)0);
            writer.Write((short)1); //colour planes
            writer.Write((short)32); //bits per pixel
            writer.Write(png.Length);
            writer.Write(22); //where the image starts
            writer.Write(png);
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// An animated gif that is one pixel in size with two frames: the first red, the second blue.
        /// </summary>
        public static byte[] CreateAnimatedGif()
        {
            return new byte[]
            {
                (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
                0x01, 0x00, 0x01, 0x00, //one pixel wide and high
                0x80, 0x00, 0x00, //has a table of two colours
                0xFF, 0x00, 0x00, //colour 0 is red
                0x00, 0x00, 0xFF, //colour 1 is blue

                //first frame, shown for a tenth of a second, its one pixel is colour 0
                0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
                0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
                0x02, 0x02, 0x44, 0x01, 0x00,

                //second frame, its one pixel is colour 1
                0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
                0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
                0x02, 0x02, 0x4C, 0x01, 0x00,

                0x3B //end of file
            };
        }

        /// <summary>
        /// A value that is not the default for a type, for tests that fill in every property of
        /// an object without knowing what the properties are.
        /// </summary>
        public static object CreateValue(Type type)
        {
            var actualType = Nullable.GetUnderlyingType(type) ?? type;

            if (actualType == typeof(string))
            {
                return "abc";
            }

            if (actualType == typeof(bool))
            {
                return true;
            }

            if (actualType == typeof(int))
            {
                return 7;
            }

            if (actualType == typeof(double))
            {
                return 0.75;
            }

            if (actualType == typeof(byte[]))
            {
                return CreateImage(4, 4);
            }

            //the last value of an enum, so it is not whatever the default happens to be
            if (actualType.IsEnum)
            {
                var values = Enum.GetValues(actualType);
                return values.GetValue(values.Length - 1)!;
            }

            throw new NotSupportedException($"Add a value for {actualType.Name} to TestHelpers.CreateValue.");
        }

        /// <summary>
        /// Read one pixel of an encoded image.
        /// </summary>
        public static SKColor GetPixel(byte[] imageData, int x, int y)
        {
            using var bitmap = SKBitmap.Decode(imageData);
            return bitmap.GetPixel(x, y);
        }

        /// <summary>
        /// Check a colour is what was expected, allowing for the small differences that resizing
        /// and encoding introduce.
        /// </summary>
        public static void AssertColor(SKColor expected, SKColor actual, int tolerance = 4)
        {
            var isClose = Math.Abs(expected.Red - actual.Red) <= tolerance
                && Math.Abs(expected.Green - actual.Green) <= tolerance
                && Math.Abs(expected.Blue - actual.Blue) <= tolerance
                && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance;
            Assert.True(isClose, $"Expected a colour close to {expected} but it was {actual}.");
        }

        /// <summary>
        /// Read the size and format of an encoded image.
        /// </summary>
        public static (int Width, int Height, SKEncodedImageFormat Format) GetImageInfo(byte[] imageData)
        {
            using var stream = new MemoryStream(imageData);
            using var codec = SKCodec.Create(stream);
            return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
        }

        /// <summary>
        /// Load one of the sample images that are copied next to the test assembly (see the csproj).
        /// </summary>
        public static byte[] LoadTestImage(string fileName)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestImages", fileName));
        }
    }
}
