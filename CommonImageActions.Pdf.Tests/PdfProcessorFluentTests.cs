using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;

namespace CommonImageActions.Pdf.Tests
{
    public class PdfProcessorFluentTests
    {
        [Fact]
        public async Task ProcessImageAsync_ShouldReturnImageForCircleShape()
        {
            var testPdf = Properties.Resources.testPdf;

            var result = await PdfProcessor.Process(testPdf)
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
