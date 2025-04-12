using CommonImageActions.Core;
using CommonImageActions.Core.Tests;

namespace CommonImageActions.Pdf.Tests
{
    public class PdfProcessorTests
    {
        [Fact]
        public async Task ProcessPdfsAsync_ShouldReturnProcessedPdfsInOrder()
        {
            // Arrange
            var pdfsData = new List<byte[]>
            {
                 Properties.Resources.testPdf,
                 Properties.Resources.testPdf
            };
            var actions = new ImageActions()
            {
                Format = SkiaSharp.SKEncodedImageFormat.Png,
                Height = 100
            };

            // Act
            var result = await PdfProcessor.ProcessPdfsAsync(pdfsData, actions);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(pdfsData.Count, result.Count());

            var isImage = TestHelpers.IsImage(result.First());
            Assert.True(isImage, "The result is not a valid image.");
        }

        [Fact]
        public async Task ProcessPdfAsync_WithByteArray_ShouldReturnProcessedPdf()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var actions = new ImageActions()
            {
                Format = SkiaSharp.SKEncodedImageFormat.Png,
                Height = 100
            };

            // Act
            var result = await PdfProcessor.ProcessPdfAsync(pdfData, actions);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<byte[]>(result);

            var isImage = TestHelpers.IsImage(result);
            Assert.True(isImage, "The result is not a valid image.");
        }

        [Fact]
        public async Task ProcessPdfAsync_WithByteArray_ShouldGetPageTwoPdf()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var actions = new ImageActions()
            {
                Format = SkiaSharp.SKEncodedImageFormat.Png,
                Height = 100,
                Page = 2
            };

            // Act
            var result = await PdfProcessor.ProcessPdfAsync(pdfData, actions);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<byte[]>(result);

            var isImage = TestHelpers.IsImage(result);
            Assert.True(isImage, "The result is not a valid image.");
        }

        [Fact]
        public async Task ProcessPdfAsync_WithByteArray_ShouldFailLoadingPdf()
        {
            // Arrange
            var imageData = Properties.Resources.testJpg;
            var actions = new ImageActions()
            {
                Format = SkiaSharp.SKEncodedImageFormat.Png,
                Height = 100,
                Page = 2
            };

            await Assert.ThrowsAsync<PdfProcessorException>(async ()=>
            {
                await PdfProcessor.ProcessPdfAsync(imageData, actions);
            });
        }


        [Fact]
        public async Task ProcessPdfAsync_WithStream_ShouldReturnProcessedPdf()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var stream = new MemoryStream(pdfData);
            var actions = new ImageActions()
            {
                Format = SkiaSharp.SKEncodedImageFormat.Png,
                Height = 100
            };

            // Act
            var result = await PdfProcessor.ProcessPdfAsync(stream, actions);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<byte[]>(result);

            var isImage = TestHelpers.IsImage(result);
            Assert.True(isImage, "The result is not a valid image.");
        }

        [Fact]
        public async Task ProcessPdfAsync_WithNullActions_ShouldThrowArgumentNullException()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var stream = new MemoryStream(pdfData);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfProcessor.ProcessPdfAsync(stream, null));
        }

        [Fact]
        public void ConvertFromBGRA32ToBmp_ShouldReturnBmpData()
        {
            // Arrange
            var width = 2;
            var height = 2;
            var managedArray = new byte[]
            {
                0x00, 0x00, 0x00, 0xFF, // Pixel 1
                0xFF, 0x00, 0x00, 0xFF, // Pixel 2
                0x00, 0xFF, 0x00, 0xFF, // Pixel 3
                0x00, 0x00, 0xFF, 0xFF  // Pixel 4
            };

            // Act
            var result = PdfProcessor.ConvertFromBGRA32ToBmp(managedArray, width, height);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }
    }
}