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
