using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using SkiaSharp;

namespace CommonImageActions.Pdf.Tests
{
    /// <summary>
    /// These tests use pdfs that are built by <see cref="TestPdf"/>, where every page is filled
    /// with one colour. The colour of the image that comes back shows which page was rendered.
    /// </summary>
    public class PdfProcessorCoverageTests
    {
        private static readonly SKColor Transparent = new SKColor(0, 0, 0, 0);

        private static async Task<SKColor> RenderedColorAsync(byte[] pdf, ImageActions actions)
        {
            actions.Format = SKEncodedImageFormat.Png;
            var result = await PdfProcessor.ProcessPdfAsync(pdf, actions);
            var info = TestHelpers.GetImageInfo(result);
            return TestHelpers.GetPixel(result, info.Width / 2, info.Height / 2);
        }

        [Fact]
        public async Task ShouldRenderThePageThatIsAskedFor()
        {
            // Arrange
            var pdf = TestPdf.Create(TestHelpers.Red, TestHelpers.Green, TestHelpers.Blue);

            // Act & Assert - pages are counted from one
            TestHelpers.AssertColor(TestHelpers.Red, await RenderedColorAsync(pdf, new ImageActions()));
            TestHelpers.AssertColor(TestHelpers.Red, await RenderedColorAsync(pdf, new ImageActions() { Page = 1 }));
            TestHelpers.AssertColor(TestHelpers.Green, await RenderedColorAsync(pdf, new ImageActions() { Page = 2 }));
            TestHelpers.AssertColor(TestHelpers.Blue, await RenderedColorAsync(pdf, new ImageActions() { Page = 3 }));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(1000)]
        public async Task ShouldRenderTheFirstPage_WhenThePageDoesNotExist(int page)
        {
            var pdf = TestPdf.Create(TestHelpers.Red, TestHelpers.Green, TestHelpers.Blue);

            var color = await RenderedColorAsync(pdf, new ImageActions() { Page = page });

            TestHelpers.AssertColor(TestHelpers.Red, color);
        }

        [Fact]
        public async Task ShouldRenderThePageAtItsOwnSize_AndAtTheSizeAskedFor()
        {
            // Arrange - the page is 200 by 100 points
            var pdf = TestPdf.Create(TestHelpers.Blue);

            // Act
            var natural = await PdfProcessor.ProcessPdfAsync(pdf, new ImageActions());
            var smaller = await PdfProcessor.ProcessPdfAsync(pdf, new ImageActions() { Width = 50 });
            var larger = await PdfProcessor.ProcessPdfAsync(pdf, new ImageActions() { Width = 1000 });
            var square = await PdfProcessor.ProcessPdfAsync(pdf, new ImageActions() { Width = 300, Height = 300, Mode = ImageMode.Zoom });

            // Assert - a pdf is always returned as a png unless a format is given
            Assert.Equal((TestPdf.PageWidth, TestPdf.PageHeight, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(natural));
            Assert.Equal((50, 25, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(smaller));
            Assert.Equal((1000, 500, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(larger));
            Assert.Equal((300, 300, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(square));

            //filled right to the edges at every size
            foreach (var result in new[] { natural, smaller, larger, square })
            {
                var info = TestHelpers.GetImageInfo(result);
                TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(result, 0, 0));
                TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(result, info.Width - 1, info.Height - 1));
            }
        }

        [Fact]
        public async Task ShouldOpenAProtectedPdf_WithItsPassword()
        {
            // Arrange
            var pdf = TestPdf.CreateProtected("open sesame", TestHelpers.Green, TestHelpers.Blue);

            // Act
            var firstPage = await RenderedColorAsync(pdf, new ImageActions() { PdfPassword = "open sesame" });
            var secondPage = await RenderedColorAsync(pdf, new ImageActions() { PdfPassword = "open sesame", Page = 2 });
            var fluent = await PdfProcessor.Process(pdf).PdfPassword("open sesame").Format(SKEncodedImageFormat.Png).ToImageAsync();

            // Assert - the right colours can only come out if the file really was decrypted
            TestHelpers.AssertColor(TestHelpers.Green, firstPage);
            TestHelpers.AssertColor(TestHelpers.Blue, secondPage);
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(fluent, 100, 50));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("wrong password")]
        [InlineData("Open Sesame")]
        public async Task ShouldNotOpenAProtectedPdf_WithoutTheRightPassword(string? password)
        {
            var pdf = TestPdf.CreateProtected("open sesame", TestHelpers.Green);

            await Assert.ThrowsAsync<PdfProcessorException>(() => PdfProcessor.ProcessPdfAsync(pdf, new ImageActions() { PdfPassword = password }));
        }

        [Fact]
        public async Task ShouldIgnoreAPassword_WhenThePdfIsNotProtected()
        {
            var pdf = TestPdf.Create(TestHelpers.Red);

            var color = await RenderedColorAsync(pdf, new ImageActions() { PdfPassword = "not needed" });

            TestHelpers.AssertColor(TestHelpers.Red, color);
        }

        [Fact]
        public async Task ShouldFail_WhenTheDataIsNotAPdf()
        {
            var notAPdf = TestHelpers.CreateImage(10, 10);
            var empty = new byte[0];

            await Assert.ThrowsAsync<PdfProcessorException>(() => PdfProcessor.ProcessPdfAsync(notAPdf, new ImageActions()));
            await Assert.ThrowsAsync<PdfProcessorException>(() => PdfProcessor.ProcessPdfAsync(empty, new ImageActions()));
        }

        [Fact]
        public async Task ProcessPdfsAsync_ShouldKeepTheOrder()
        {
            // Arrange
            var colors = new[] { TestHelpers.Red, TestHelpers.Green, TestHelpers.Blue, TestHelpers.Yellow, TestHelpers.Blue, TestHelpers.Red };
            var pdfs = colors.Select(x => TestPdf.Create(x)).ToList();

            // Act
            var results = (await PdfProcessor.ProcessPdfsAsync(pdfs, new ImageActions() { Width = 20, Format = SKEncodedImageFormat.Png })).ToList();

            // Assert
            Assert.Equal(colors.Length, results.Count);
            for (var i = 0; i < colors.Length; i++)
            {
                TestHelpers.AssertColor(colors[i], TestHelpers.GetPixel(results[i], 10, 5));
            }
        }

        [Fact]
        public async Task ShouldRenderCorrectly_WhenManyPdfsAreRenderedAtOnce()
        {
            // Arrange - pdfium can only do one thing at a time, so these have to take turns
            var red = TestPdf.Create(TestHelpers.Red);
            var blue = TestPdf.CreateProtected("secret", TestHelpers.Blue);

            // Act
            var jobs = Enumerable.Range(0, 40).Select(i => i % 2 == 0
                ? RenderedColorAsync(red, new ImageActions() { Width = 60 + i })
                : RenderedColorAsync(blue, new ImageActions() { Width = 60 + i, PdfPassword = "secret" })).ToList();
            var colors = await Task.WhenAll(jobs);

            // Assert
            for (var i = 0; i < colors.Length; i++)
            {
                TestHelpers.AssertColor(i % 2 == 0 ? TestHelpers.Red : TestHelpers.Blue, colors[i]);
            }
        }

        [Fact]
        public async Task ShouldApplyImageActionsToThePage()
        {
            // Arrange
            var pdf = TestPdf.Create(TestHelpers.Red);

            // Act
            var circle = await PdfProcessor.Process(pdf).Width(100).Height(100).Mode(ImageMode.Zoom).Shape(ImageShape.Circle).ToImageAsync();
            var rotated = await PdfProcessor.Process(pdf).Rotate(90).ToImageAsync();
            var inverted = await PdfProcessor.Process(pdf).IsInverted().ToImageAsync();
            var cropped = await PdfProcessor.Process(pdf).Crop(0, 0, 50, 50).ToImageAsync();
            var fromActions = await new PdfActionQueryBuilder(pdf, new ImageActions() { Width = 40 }).ToImageAsync();
            var fromNothing = await new PdfActionQueryBuilder(pdf, null).ToImageAsync();
            var blocking = PdfProcessor.Process(pdf).Width(20).ToImage();

            // Assert
            Assert.Equal(Transparent, TestHelpers.GetPixel(circle, 0, 0));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(circle, 50, 50));
            Assert.Equal((TestPdf.PageHeight, TestPdf.PageWidth), Size(rotated));
            TestHelpers.AssertColor(new SKColor(0, 255, 255), TestHelpers.GetPixel(inverted, 100, 50));
            Assert.Equal((50, 50), Size(cropped));
            Assert.Equal((40, 20), Size(fromActions));
            Assert.Equal((TestPdf.PageWidth, TestPdf.PageHeight), Size(fromNothing));
            Assert.Equal((20, 10), Size(blocking));
        }

        [Fact]
        public async Task ShouldRejectMissingStreamsAndActions()
        {
            var pdf = TestPdf.Create(TestHelpers.Red);
            using var stream = new MemoryStream(pdf);

            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfProcessor.ProcessPdfAsync(stream, null!, new ImageActions()));
            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfProcessor.ProcessPdfAsync(pdf, null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfProcessor.Process(pdf).ToStreamAsync(null!));
        }

        [Fact]
        public async Task ShouldLimitTheRender_ToMaxRenderDimension()
        {
            // Arrange - a hundred times the size of the page is far more than is ever rendered
            var pdf = TestPdf.Create(TestHelpers.Green);

            // Act - the image that comes back is still the size that was asked for
            var result = await PdfProcessor.ProcessPdfAsync(pdf, new ImageActions() { Width = 20000, Height = 100, Mode = ImageMode.Stretch, Format = SKEncodedImageFormat.Png });

            // Assert
            Assert.Equal((20000, 100), Size(result));
            Assert.True(PdfProcessor.GetRenderScale(TestPdf.PageWidth, TestPdf.PageHeight, new ImageActions() { Width = 20000, Height = 100 }) * TestPdf.PageWidth <= PdfProcessor.MaxRenderDimension);
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(result, 19999, 50));
        }

        private static (int Width, int Height) Size(byte[] image)
        {
            var info = TestHelpers.GetImageInfo(image);
            return (info.Width, info.Height);
        }
    }
}
