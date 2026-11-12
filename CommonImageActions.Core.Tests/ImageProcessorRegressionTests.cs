using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorRegressionTests
    {
        //the smallest valid gif, a single transparent pixel. Skia can read gif but not write it
        private static readonly byte[] OnePixelGif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

        [Fact]
        public async Task ProcessImagesAsync_ShouldSizeAndFormatEachImageOnItsOwn()
        {
            // Arrange
            var squarePng = TestHelpers.CreateImage(128, 128, SKEncodedImageFormat.Png);
            var tallJpeg = TestHelpers.CreateImage(100, 300, SKEncodedImageFormat.Jpeg);
            var actions = new ImageActions()
            {
                Width = 50
            };

            // Act
            var result = (await ImageProcessor.ProcessImagesAsync(new[] { squarePng, tallJpeg }, actions)).ToArray();

            // Assert
            Assert.Equal((50, 50, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(result[0]));
            Assert.Equal((50, 150, SKEncodedImageFormat.Jpeg), TestHelpers.GetImageInfo(result[1]));
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldNotChangeTheActionsItIsGiven()
        {
            // Arrange
            var image = TestHelpers.CreateImage(128, 64, SKEncodedImageFormat.Jpeg);
            var actions = new ImageActions()
            {
                Width = 50,
                CornerRadius = 5
            };

            // Act
            await ImageProcessor.ProcessImageAsync(image, actions);

            // Assert
            Assert.Equal(50, actions.Width);
            Assert.Null(actions.Height);
            Assert.Null(actions.Format);
            Assert.Null(actions.Shape);
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldUseDisplayedSize_WhenExifRotatesAQuarterTurn()
        {
            // Arrange - stored as 2048x1536 with an orientation of rotate 90, so it displays as 1536x2048
            var rotated = TestHelpers.LoadTestImage("rotate90.jpg");

            // Act
            var original = await ImageProcessor.ProcessImageAsync(rotated, new ImageActions());
            var resized = await ImageProcessor.ProcessImageAsync(rotated, new ImageActions() { Width = 150 });
            var max = await ImageProcessor.ProcessImageAsync(rotated, new ImageActions() { Width = 300, Height = 300, Mode = ImageMode.Max });

            // Assert
            var originalInfo = TestHelpers.GetImageInfo(original);
            Assert.Equal((1536, 2048), (originalInfo.Width, originalInfo.Height));

            var resizedInfo = TestHelpers.GetImageInfo(resized);
            Assert.Equal((150, 200), (resizedInfo.Width, resizedInfo.Height));

            var maxInfo = TestHelpers.GetImageInfo(max);
            Assert.Equal((225, 300), (maxInfo.Width, maxInfo.Height));
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldDrawTextUpright_WhenExifRotatesTheImage()
        {
            // Arrange - an L has its horizontal bar at the bottom, so an upright L has more of
            // its colour in the bottom half of the image than in the top half
            TestFont.Register();
            var rotated = TestHelpers.LoadTestImage("rotate180.jpg");
            var actions = new ImageActions()
            {
                Width = 400,
                Height = 300,
                Text = "L",
                FontFamily = TestFont.FamilyName,
                TextColor = "ff00ff",
                Format = SKEncodedImageFormat.Png
            };

            // Act
            var result = await ImageProcessor.ProcessImageAsync(rotated, actions);

            // Assert
            using var bitmap = SKBitmap.Decode(result);
            var textColor = new SKColor(255, 0, 255);
            var topHalf = 0;
            var bottomHalf = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y) == textColor)
                    {
                        if (y < bitmap.Height / 2)
                        {
                            topHalf++;
                        }
                        else
                        {
                            bottomHalf++;
                        }
                    }
                }
            }

            //the font is built by the tests, so the text is drawn whatever fonts the machine has
            Assert.True(topHalf + bottomHalf > 0, "No text was drawn.");

            Assert.True(bottomHalf > topHalf, $"Text is upside down. Top half {topHalf} pixels, bottom half {bottomHalf} pixels.");
        }

        [Theory]
        [InlineData(SKEncodedImageFormat.Gif)]
        [InlineData(SKEncodedImageFormat.Bmp)]
        [InlineData(SKEncodedImageFormat.Ico)]
        public async Task ProcessImageAsync_ShouldFallBackToPng_WhenFormatCanNotBeWritten(SKEncodedImageFormat format)
        {
            // Arrange
            var image = TestHelpers.CreateImage(64, 64);
            var actions = new ImageActions()
            {
                Width = 32,
                Format = format
            };

            // Act
            var result = await ImageProcessor.ProcessImageAsync(image, actions);

            // Assert
            Assert.Equal((32, 32, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(result));
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldResizeAGif()
        {
            // Arrange
            var actions = new ImageActions()
            {
                Width = 10
            };

            // Act
            var result = await ImageProcessor.ProcessImageAsync(OnePixelGif, actions);

            // Assert
            Assert.Equal((10, 10, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(result));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public async Task ProcessImageAsync_ShouldThrowImageProcessorException_WhenSizeIsNotPositive(int width)
        {
            // Arrange
            var image = TestHelpers.CreateImage(64, 64);
            var actions = new ImageActions()
            {
                Width = width
            };

            // Act & Assert
            await Assert.ThrowsAsync<ImageProcessorException>(() => ImageProcessor.ProcessImageAsync(image, actions));
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldNeverCalculateASizeOfZero()
        {
            // Arrange - a height of 1 on a very wide image would calculate a width below one pixel
            var image = TestHelpers.CreateImage(10, 500);
            var actions = new ImageActions()
            {
                Width = 1
            };
            var wideImage = TestHelpers.CreateImage(500, 10);

            // Act
            var result = await ImageProcessor.ProcessImageAsync(image, actions);
            var wideResult = await ImageProcessor.ProcessImageAsync(wideImage, new ImageActions() { Width = 1 });

            // Assert
            var info = TestHelpers.GetImageInfo(result);
            Assert.Equal((1, 50), (info.Width, info.Height));

            var wideInfo = TestHelpers.GetImageInfo(wideResult);
            Assert.Equal((1, 1), (wideInfo.Width, wideInfo.Height));
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldThrowImageProcessorException_WhenDataIsNotAnImage()
        {
            // Arrange
            var notAnImage = new byte[] { 1, 2, 3, 4 };

            // Act & Assert
            await Assert.ThrowsAsync<ImageProcessorException>(() => ImageProcessor.ProcessImageAsync(notAnImage, new ImageActions()));
        }

        [Fact]
        public async Task ProcessImageAsync_ShouldThrowArgumentNullException_WhenActionsOrDataIsNull()
        {
            // Arrange
            var image = TestHelpers.CreateImage(64, 64);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => ImageProcessor.ProcessImageAsync(image, null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => ImageProcessor.ProcessImageAsync((byte[])null!, new ImageActions()));
        }

        [Theory]
        [InlineData(161, 400)]
        [InlineData(400, 161)]
        [InlineData(97, 300)]
        [InlineData(300, 97)]
        public async Task ProcessImageAsync_Zoom_ShouldCoverTheWholeImage(int sourceWidth, int sourceHeight)
        {
            // Arrange - these sizes used to land a fraction short and leave a transparent line on one edge
            var image = TestHelpers.CreateImage(sourceWidth, sourceHeight);
            var actions = new ImageActions()
            {
                Width = 100,
                Height = 100,
                Mode = ImageMode.Zoom
            };

            // Act
            var result = await ImageProcessor.ProcessImageAsync(image, actions);

            // Assert
            using var bitmap = SKBitmap.Decode(result);
            Assert.Equal(255, bitmap.GetPixel(0, 50).Alpha);
            Assert.Equal(255, bitmap.GetPixel(99, 50).Alpha);
            Assert.Equal(255, bitmap.GetPixel(50, 0).Alpha);
            Assert.Equal(255, bitmap.GetPixel(50, 99).Alpha);
        }

        [Fact]
        public void ToImage_ShouldNotDeadlock_WhenTheThreadHasASynchronizationContext()
        {
            // Arrange
            var image = TestHelpers.CreateImage(64, 64);
            byte[]? result = null;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new BlockedSynchronizationContext());
                result = ImageProcessor.Process(image).Width(32).ToImage();
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

        [Theory]
        [InlineData("John Doe", "JD")]
        [InlineData("DustinGamester", "DG")]
        [InlineData("DUSTIN GAMESTER", "DG")]
        [InlineData("dustin gamester", "DG")]
        [InlineData("Dustin_Gamester", "DG")]
        [InlineData("DG", "DG")]
        [InlineData("dustin", "D")]
        [InlineData("", "")]
        public void GetInitials_ShouldUseTheFirstLetterOfTheFirstTwoWords(string input, string expected)
        {
            Assert.Equal(expected, ImageProcessor.GetInitials(input));
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
