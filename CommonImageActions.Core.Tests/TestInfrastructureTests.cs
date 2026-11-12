using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    /// <summary>
    /// The font and the image files that other tests rely on are built by hand in this project.
    /// These tests make sure that what was built is what it is meant to be, so that a failure
    /// in another test is about the library and not about the test data.
    /// </summary>
    public class TestInfrastructureTests
    {
        [Fact]
        public void TestFont_ShouldBeAFontThatSkiaCanLoad()
        {
            // Act
            using var typeface = SKTypeface.FromData(SKData.CreateCopy(TestFont.Create()));

            // Assert
            Assert.NotNull(typeface);
            Assert.Equal(TestFont.FamilyName, typeface.FamilyName);

            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = 100
            };

            //every character is 800 units wide in a font where 1000 units is the text size
            Assert.Equal(80, paint.MeasureText("A"), 1);
            Assert.Equal(240, paint.MeasureText("A L"), 1);
            Assert.True(typeface.GetGlyphs("AL ").All(x => x != 0), "The font should have a glyph for every character.");
        }

        [Fact]
        public void TestFont_ShouldDrawBlocksAndAnLShape()
        {
            // Arrange
            using var typeface = SKTypeface.FromData(SKData.CreateCopy(TestFont.Create()));
            using var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = 100,
                Color = SKColors.Black,
                IsAntialias = false
            };
            using var bitmap = new SKBitmap(200, 120);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);

            // Act - the baseline is at y 100, A starts at x 0 and L at x 80
            canvas.DrawText("AL", 0, 100, paint);

            // Assert - the A is a block from 10 to 70 across and 30 to 100 down
            Assert.Equal(SKColors.Black, bitmap.GetPixel(40, 65));
            Assert.Equal(SKColors.Black, bitmap.GetPixel(15, 35));
            Assert.Equal(SKColors.Black, bitmap.GetPixel(65, 95));
            Assert.Equal(SKColors.White, bitmap.GetPixel(40, 20));
            Assert.Equal(SKColors.White, bitmap.GetPixel(75, 65));

            //the L has its upright on the left and its foot along the bottom, the top right is empty
            Assert.Equal(SKColors.Black, bitmap.GetPixel(80 + 20, 40));
            Assert.Equal(SKColors.Black, bitmap.GetPixel(80 + 60, 90));
            Assert.Equal(SKColors.White, bitmap.GetPixel(80 + 60, 40));
        }

        [Theory]
        [InlineData(1, SKEncodedOrigin.TopLeft)]
        [InlineData(2, SKEncodedOrigin.TopRight)]
        [InlineData(3, SKEncodedOrigin.BottomRight)]
        [InlineData(4, SKEncodedOrigin.BottomLeft)]
        [InlineData(5, SKEncodedOrigin.LeftTop)]
        [InlineData(6, SKEncodedOrigin.RightTop)]
        [InlineData(7, SKEncodedOrigin.RightBottom)]
        [InlineData(8, SKEncodedOrigin.LeftBottom)]
        public void WithExifOrientation_ShouldSetTheOrientationSkiaReads(int orientation, SKEncodedOrigin expected)
        {
            // Arrange
            var jpeg = TestHelpers.CreateQuadrantImage(40, 20, SKEncodedImageFormat.Jpeg);

            // Act
            var tagged = TestHelpers.WithExifOrientation(jpeg, orientation);

            // Assert
            using var stream = new MemoryStream(tagged);
            using var codec = SKCodec.Create(stream);
            Assert.NotNull(codec);
            Assert.Equal(expected, codec.EncodedOrigin);
            Assert.Equal((40, 20), (codec.Info.Width, codec.Info.Height));
        }

        [Fact]
        public void CreatedImageFiles_ShouldBeTheFormatsTheyClaim()
        {
            Assert.Equal((6, 4, SKEncodedImageFormat.Bmp), TestHelpers.GetImageInfo(TestHelpers.CreateBmp(6, 4, TestHelpers.Green)));
            Assert.Equal((16, 16, SKEncodedImageFormat.Ico), TestHelpers.GetImageInfo(TestHelpers.CreateIco(16, TestHelpers.Blue)));
            Assert.Equal((1, 1, SKEncodedImageFormat.Gif), TestHelpers.GetImageInfo(TestHelpers.CreateAnimatedGif()));

            Assert.Equal(TestHelpers.Green, TestHelpers.GetPixel(TestHelpers.CreateBmp(6, 4, TestHelpers.Green), 5, 3));
            Assert.Equal(TestHelpers.Blue, TestHelpers.GetPixel(TestHelpers.CreateIco(16, TestHelpers.Blue), 8, 8));
        }
    }
}
