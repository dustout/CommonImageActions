using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using SkiaSharp;

namespace CommonImageActions.Pdf.Tests
{
    public class PdfProcessorRegressionTests
    {
        //the test pdf is US letter, 612 x 792 points
        private const double PageWidth = 612;
        private const double PageHeight = 792;

        [Fact]
        public async Task ProcessPdfAsync_ShouldFallBackToPng_WhenFormatCanNotBeWritten()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var actions = new ImageActions()
            {
                Width = 100,
                Format = SKEncodedImageFormat.Gif
            };

            // Act
            var result = await PdfProcessor.ProcessPdfAsync(pdfData, actions);

            // Assert
            var info = TestHelpers.GetImageInfo(result);
            Assert.Equal(SKEncodedImageFormat.Png, info.Format);
            Assert.Equal(100, info.Width);
        }

        [Fact]
        public async Task ProcessPdfAsync_ShouldReturnRequestedSize_WhenLargerThanThePage()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var actions = new ImageActions()
            {
                Width = 1224
            };

            // Act
            var result = await PdfProcessor.ProcessPdfAsync(pdfData, actions);

            // Assert
            var info = TestHelpers.GetImageInfo(result);
            Assert.Equal((1224, 1584), (info.Width, info.Height));
        }

        [Fact]
        public async Task ProcessPdfAsync_ShouldNotChangeTheActionsItIsGiven()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            var actions = new ImageActions()
            {
                Width = 100
            };

            // Act
            await PdfProcessor.ProcessPdfAsync(pdfData, actions);

            // Assert
            Assert.Null(actions.Height);
            Assert.Null(actions.Format);
        }

        [Fact]
        public async Task ProcessPdfAsync_ShouldThrowArgumentNullException_WhenDataIsNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfProcessor.ProcessPdfAsync((byte[])null!, new ImageActions()));
        }

        [Fact]
        public void GetRenderScale_ShouldBeOne_WhenNoSizeOrASmallerSizeIsRequested()
        {
            Assert.Equal(1, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions()));
            Assert.Equal(1, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Width = 100 }));
            Assert.Equal(1, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Height = 100 }));
        }

        [Fact]
        public void GetRenderScale_ShouldScaleUp_WhenALargerSizeIsRequested()
        {
            Assert.Equal(2, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Width = 1224 }));
            Assert.Equal(2, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Height = 1584 }));

            //zoom and stretch cover the box so need the larger scale, fit and max sit inside it so need the smaller
            Assert.Equal(3, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Width = 1836, Height = 1584, Mode = ImageMode.Zoom }));
            Assert.Equal(2, PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Width = 1836, Height = 1584, Mode = ImageMode.Fit }));
        }

        [Fact]
        public void GetRenderScale_ShouldBeLimitedByMaxRenderDimension()
        {
            // Act
            var scale = PdfProcessor.GetRenderScale(PageWidth, PageHeight, new ImageActions() { Width = 100000 });

            // Assert
            Assert.Equal(PdfProcessor.MaxRenderDimension / PageHeight, scale);
        }

        [Fact]
        public async Task FluentBuilder_ShouldOfferTheSameOptionsAsImages()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;

            // Act - a quarter turn makes the portrait page landscape
            var result = await PdfProcessor.Process(pdfData)
                .Width(396)
                .Rotate(90)
                .IsInverted()
                .BackgroundColor("ffffff")
                .Quality(80)
                .Format(SKEncodedImageFormat.Jpeg)
                .ToImageAsync();

            // Assert
            var info = TestHelpers.GetImageInfo(result);
            Assert.Equal((396, 306, SKEncodedImageFormat.Jpeg), info);

            //the page is white, so inverted it is black
            var corner = TestHelpers.GetPixel(result, 5, 5);
            Assert.True(corner.Red < 20 && corner.Green < 20 && corner.Blue < 20, $"Expected black but was {corner}.");
        }

        [Fact]
        public async Task ShouldWriteTheResultToAStream()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            using var input = new MemoryStream(pdfData);
            using var output = new MemoryStream();
            using var fluentOutput = new MemoryStream();

            // Act
            await PdfProcessor.ProcessPdfAsync(input, output, new ImageActions() { Width = 100 });
            await PdfProcessor.Process(pdfData).Width(50).ToStreamAsync(fluentOutput);

            // Assert
            Assert.Equal(100, TestHelpers.GetImageInfo(output.ToArray()).Width);
            Assert.Equal(50, TestHelpers.GetImageInfo(fluentOutput.ToArray()).Width);
        }

        [Fact]
        public void ToImage_ShouldNotDeadlock_WhenTheThreadHasASynchronizationContext()
        {
            // Arrange
            var pdfData = Properties.Resources.testPdf;
            byte[]? result = null;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new BlockedSynchronizationContext());
                result = PdfProcessor.Process(pdfData).Width(100).ToImage();
            })
            {
                IsBackground = true
            };

            // Act
            thread.Start();
            var finished = thread.Join(TimeSpan.FromSeconds(30));

            // Assert
            Assert.True(finished, "ToImage did not return, it is deadlocked on the synchronization context.");
            Assert.NotNull(result);
        }

        /// <summary>
        /// Behaves like a UI thread that is blocked: work posted back to it is never run.
        /// </summary>
        private class BlockedSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
            }
        }
    }
}
