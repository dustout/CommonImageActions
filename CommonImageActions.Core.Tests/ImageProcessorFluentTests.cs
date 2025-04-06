using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorFluentTests
    {
        [Fact]
        public async Task ProcessImageAsync_ShouldReturnImageForCircleShape()
        {
            var testJpg = Properties.Resources.testJpg;

            var result = await ImageProcessor.Process(testJpg)
                .Width(100)
                .Height(100)
                .Mode(ImageMode.Fit)
                .Shape(ImageShape.Circle)
                .ToImageAsync();

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            var isImage = TestHelpers.IsImage(result);
            Assert.True(isImage, "The result is not a valid image.");
        }

    }
}
