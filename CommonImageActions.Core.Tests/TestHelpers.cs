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
    }
}
