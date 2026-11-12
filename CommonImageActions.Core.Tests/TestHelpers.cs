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
