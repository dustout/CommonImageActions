using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;
using Xunit;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorTests
    {
        [Fact]
        public async Task ProcessImagesAsync_ShouldReturnProcessedImages()
        {
            var testJpg = Properties.Resources.testJpg;

            var imagesData = new List<byte[]>();

            //make 100 copies of the data and process them all
            for(var i=0; i < 100; i++)
            {
                var newTestJpg = testJpg.ToArray();
                imagesData.Add(newTestJpg);
            }

            // Arrange
            var actions = new ImageActions();

            // Act
            var result = await ImageProcessor.ProcessImagesAsync(imagesData, actions);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(imagesData.Count, result.Count());
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldReturnProcessedImage()
        {
            var testJpg = Properties.Resources.testJpg;
            var actions = new ImageActions()
            {
                Width = 100,
            };

            // Act
            var result = await ImageProcessor.ProcessImageAsync(testJpg, actions);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

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
        public async Task ProcessImageAsync_Stream_ShouldThrowArgumentNullException_WhenActionsIsNull()
        {
            // Arrange
            var imageData = new byte[] { 1, 2, 3 };
            using var stream = new MemoryStream(imageData);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => ImageProcessor.ProcessImageAsync(stream, null));
        }

        [Fact]
        public async Task ProcessVirtualImageAsync_ShouldReturnProcessedImage()
        {
            // Arrange
            var actions = new ImageActions();

            // Act
            var result = await ImageProcessor.ProcessVirtualImageAsync(actions);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void EncodeSkiaImage_ShouldReturnEncodedImage()
        {
            // Arrange
            var bitmap = new SKBitmap(100, 100);
            using var canvas = new SKCanvas(bitmap);
            using var newImage = new SkiaImage(bitmap);
            var actions = new ImageActions();

            // Act
            var result = ImageProcessor.EncodeSkiaImage(newImage, actions);

            // Assert
            Assert.NotNull(result);
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
