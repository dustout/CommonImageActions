using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorTests
    {
        [Fact]
        public async Task ProcessImageAsync_Stream_ShouldReturnProcessedImage()
        {
            var testJpg = Properties.Resources.testJpg;
            var actions = new ImageActions()
            {
                Width = 100,
            };
            using var stream = new MemoryStream(testJpg);

            // Act
            var result = await ImageProcessor.ProcessImageAsync(stream, actions);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void GetInitials_ShouldReturnInitials()
        {
            // Arrange
            var input = "John Doe";

            // Act
            var result = ImageProcessor.GetInitials(input);

            // Assert
            Assert.Equal("JD", result);
        }

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
