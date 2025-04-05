using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorTests
    {
        

        [Fact]
        public void CalculateHash_ShouldReturnHash()
        {
            // Arrange
            var input = "test";

            // Act
            var result = ImageProcessor.CalculateHash(input);

            // Assert
            Assert.NotEqual(0ul, result);
        }
    }
}
