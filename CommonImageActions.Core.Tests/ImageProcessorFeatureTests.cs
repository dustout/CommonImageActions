using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorFeatureTests
    {
        private static readonly SKColor Transparent = new SKColor(0, 0, 0, 0);
        private static readonly SKColor White = new SKColor(255, 255, 255);
        private static readonly SKColor Magenta = new SKColor(255, 0, 255);

        private static Task<byte[]> ProcessAsync(byte[] image, ImageActions actions)
        {
            //png keeps the colours exactly as they were drawn
            actions.Format ??= SKEncodedImageFormat.Png;
            return ImageProcessor.ProcessImageAsync(image, actions);
        }

        [Fact]
        public async Task Anchor_ShouldChooseWhichSideZoomKeeps()
        {
            // Arrange - four times wider than it is tall, so a square zoom can only show a quarter of it
            var image = TestHelpers.CreateQuadrantImage(400, 100);

            // Act
            var left = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, Anchor = ImageAnchor.Left });
            var right = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, Anchor = ImageAnchor.Right });

            // Assert
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(left, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(left, 50, 75));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(right, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(right, 50, 75));
        }

        [Fact]
        public async Task FocalPoint_ShouldKeepThePointOfInterestInView()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(400, 100);

            // Act
            var left = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, FocalPointX = 0.1, FocalPointY = 0.5 });
            var right = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, FocalPointX = 0.9, FocalPointY = 0.5 });

            // Assert
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(left, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(right, 50, 25));
        }

        [Fact]
        public async Task Anchor_ShouldChooseWhereFitPlacesTheImage()
        {
            // Arrange - fitted into a square this is a 200x50 strip
            var image = TestHelpers.CreateQuadrantImage(400, 100);

            // Act
            var center = await ProcessAsync(image, new ImageActions() { Width = 200, Height = 200, Mode = ImageMode.Fit });
            var top = await ProcessAsync(image, new ImageActions() { Width = 200, Height = 200, Mode = ImageMode.Fit, Anchor = ImageAnchor.Top });

            // Assert
            Assert.Equal(Transparent, TestHelpers.GetPixel(center, 50, 10));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(center, 50, 85));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(top, 50, 10));
            Assert.Equal(Transparent, TestHelpers.GetPixel(top, 50, 190));
        }

        [Fact]
        public async Task BackgroundColor_ShouldFillTheSpaceAroundTheImage()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(400, 100);

            // Act
            var fit = await ProcessAsync(image, new ImageActions() { Width = 200, Height = 200, Mode = ImageMode.Fit, BackgroundColor = "0000ff" });
            var circleJpeg = await ImageProcessor.ProcessImageAsync(image, new ImageActions() { Width = 100, Height = 100, Shape = ImageShape.Circle, BackgroundColor = "ffffff", Format = SKEncodedImageFormat.Jpeg });

            // Assert - the corner of a circle in a jpeg is black unless there is a background
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(fit, 50, 10));
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(circleJpeg, 2, 2), tolerance: 12);
        }

        [Fact]
        public async Task AllowUpscale_ShouldKeepTheImageAtItsOwnSize_WhenFalse()
        {
            // Arrange
            var image = TestHelpers.CreateImage(100, 50);

            // Act
            var enlarged = await ProcessAsync(image, new ImageActions() { Width = 400 });
            var width = await ProcessAsync(image, new ImageActions() { Width = 400, AllowUpscale = false });
            var zoom = await ProcessAsync(image, new ImageActions() { Width = 400, Height = 400, Mode = ImageMode.Zoom, AllowUpscale = false });
            var max = await ProcessAsync(image, new ImageActions() { Width = 400, Height = 400, Mode = ImageMode.Max, AllowUpscale = false });
            var smaller = await ProcessAsync(image, new ImageActions() { Width = 50, AllowUpscale = false });

            // Assert
            Assert.Equal((400, 200), Size(enlarged));
            Assert.Equal((100, 50), Size(width));
            Assert.Equal((50, 50), Size(zoom));
            Assert.Equal((100, 50), Size(max));
            Assert.Equal((50, 25), Size(smaller));
        }

        [Fact]
        public async Task Crop_ShouldUseOnlyThatPartOfTheImage()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(200, 100);

            // Act - the top right block
            var cropped = await ProcessAsync(image, new ImageActions() { CropX = 100, CropY = 0, CropWidth = 100, CropHeight = 50 });
            var resized = await ProcessAsync(image, new ImageActions() { CropX = 100, CropY = 0, CropWidth = 100, CropHeight = 50, Width = 20 });
            var outside = await ProcessAsync(image, new ImageActions() { CropX = 150, CropY = 75, CropWidth = 500, CropHeight = 500 });

            // Assert
            Assert.Equal((100, 50), Size(cropped));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(cropped, 5, 5));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(cropped, 95, 45));

            Assert.Equal((20, 10), Size(resized));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(resized, 10, 5));

            //a crop that runs off the image is cut back to the image
            Assert.Equal((50, 25), Size(outside));
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(outside, 25, 12));
        }

        [Fact]
        public async Task Rotate_ShouldTurnTheImageClockwise()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(200, 100);

            // Act
            var quarter = await ProcessAsync(image, new ImageActions() { Rotate = 90 });
            var half = await ProcessAsync(image, new ImageActions() { Rotate = 180 });
            var negative = await ProcessAsync(image, new ImageActions() { Rotate = -90 });

            // Assert - a quarter turn swaps the width and height, and the left side is now the top
            Assert.Equal((100, 200), Size(quarter));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(quarter, 25, 50));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(quarter, 75, 50));
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(quarter, 25, 150));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(quarter, 75, 150));

            Assert.Equal((200, 100), Size(half));
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(half, 50, 25));

            Assert.Equal((100, 200), Size(negative));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(negative, 25, 50));
        }

        [Fact]
        public async Task Rotate_ShouldMakeRoomForAnyAngle()
        {
            // Arrange
            var image = TestHelpers.CreateImage(200, 100);

            // Act
            var rotated = await ProcessAsync(image, new ImageActions() { Rotate = 45 });
            var withBackground = await ProcessAsync(image, new ImageActions() { Rotate = 45, BackgroundColor = "ffffff" });

            // Assert - 200x100 turned 45 degrees needs a 212x212 box, with nothing in the corners
            Assert.Equal((212, 212), Size(rotated));
            Assert.Equal(Transparent, TestHelpers.GetPixel(rotated, 2, 2));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(rotated, 106, 106));
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(withBackground, 2, 2));
        }

        [Fact]
        public async Task Flip_ShouldMirrorTheImage()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(200, 100);

            // Act
            var horizontal = await ProcessAsync(image, new ImageActions() { FlipHorizontal = true });
            var vertical = await ProcessAsync(image, new ImageActions() { FlipVertical = true });

            // Assert
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(horizontal, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(horizontal, 150, 25));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(vertical, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(vertical, 50, 75));
        }

        [Fact]
        public async Task ColorFilters_ShouldChangeTheColours()
        {
            // Arrange
            var image = TestHelpers.CreateImage(20, 20, color: new SKColor(200, 100, 50));
            var gray = TestHelpers.CreateImage(20, 20, color: new SKColor(100, 100, 100));

            // Act
            var inverted = TestHelpers.GetPixel(await ProcessAsync(image, new ImageActions() { IsInverted = true }), 10, 10);
            var darker = TestHelpers.GetPixel(await ProcessAsync(image, new ImageActions() { Brightness = 0.5 }), 10, 10);
            var noColor = TestHelpers.GetPixel(await ProcessAsync(image, new ImageActions() { Saturation = 0 }), 10, 10);
            var sepia = TestHelpers.GetPixel(await ProcessAsync(gray, new ImageActions() { IsSepia = true }), 10, 10);
            var contrast = TestHelpers.GetPixel(await ProcessAsync(gray, new ImageActions() { Contrast = 2 }), 10, 10);
            var unchanged = TestHelpers.GetPixel(await ProcessAsync(image, new ImageActions() { Brightness = 1, Contrast = 1, Saturation = 1 }), 10, 10);

            // Assert
            TestHelpers.AssertColor(new SKColor(55, 155, 205), inverted);
            TestHelpers.AssertColor(new SKColor(100, 50, 25), darker);
            Assert.True(noColor.Red == noColor.Green && noColor.Green == noColor.Blue, $"Saturation 0 should be gray but was {noColor}.");
            Assert.True(sepia.Red > sepia.Green && sepia.Green > sepia.Blue, $"Sepia should be a warm brown but was {sepia}.");

            //contrast pushes away from mid gray (127), so a darker gray gets darker
            TestHelpers.AssertColor(new SKColor(72, 72, 72), contrast);
            TestHelpers.AssertColor(new SKColor(200, 100, 50), unchanged);
        }

        [Fact]
        public async Task ColorFilters_ShouldCombine()
        {
            // Arrange
            var image = TestHelpers.CreateImage(20, 20, color: new SKColor(200, 100, 50));

            // Act - grayscale then inverted
            var result = TestHelpers.GetPixel(await ProcessAsync(image, new ImageActions() { IsGrayscale = true, IsInverted = true }), 10, 10);

            // Assert - gray of that colour is about 125, inverted is about 130
            Assert.True(result.Red == result.Green && result.Green == result.Blue, $"Expected gray but was {result}.");
            Assert.InRange(result.Red, 125, 135);
        }

        [Fact]
        public async Task Blur_ShouldSoftenWithoutFadingTheEdges()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(200, 100);

            // Act
            var blurred = await ProcessAsync(image, new ImageActions() { Blur = 5 });

            // Assert - colours bleed into each other where the blocks meet
            var boundary = TestHelpers.GetPixel(blurred, 100, 25);
            Assert.True(boundary.Red > 60 && boundary.Green > 60, $"The boundary should be a mix of red and green but was {boundary}.");

            //and the outside edge is still solid, a blur mixes in transparency from beyond the edge if it is not handled
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(blurred, 0, 0));
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(blurred, 199, 99));
            Assert.Equal((200, 100), Size(blurred));
        }

        [Fact]
        public async Task Sharpen_ShouldIncreaseTheDifferenceAtEdges()
        {
            // Arrange - a soft edge between dark and light gray
            using var bitmap = new SKBitmap(40, 10);
            for (var x = 0; x < 40; x++)
            {
                var level = (byte)(x < 18 ? 80 : x > 21 ? 160 : 80 + ((x - 17) * 16));
                for (var y = 0; y < 10; y++)
                {
                    bitmap.SetPixel(x, y, new SKColor(level, level, level));
                }
            }
            using var source = SKImage.FromBitmap(bitmap);
            using var data = source.Encode(SKEncodedImageFormat.Png, 100);
            var image = data.ToArray();

            // Act
            var sharpened = await ProcessAsync(image, new ImageActions() { Sharpen = 1 });

            // Assert - just before the edge gets darker and just after gets lighter, flat areas are left alone
            Assert.True(TestHelpers.GetPixel(sharpened, 17, 5).Red < 80);
            Assert.True(TestHelpers.GetPixel(sharpened, 22, 5).Red > 160);
            TestHelpers.AssertColor(new SKColor(80, 80, 80), TestHelpers.GetPixel(sharpened, 5, 5));
            TestHelpers.AssertColor(new SKColor(80, 80, 80), TestHelpers.GetPixel(sharpened, 0, 0));
        }

        [Fact]
        public async Task Pixelate_ShouldDrawTheImageInBlocks()
        {
            // Arrange - a left to right gradient, so every column is a different shade
            using var bitmap = new SKBitmap(100, 20);
            for (var x = 0; x < 100; x++)
            {
                for (var y = 0; y < 20; y++)
                {
                    bitmap.SetPixel(x, y, new SKColor((byte)(x * 2), 0, 0));
                }
            }
            using var source = SKImage.FromBitmap(bitmap);
            using var data = source.Encode(SKEncodedImageFormat.Png, 100);

            // Act
            var pixelated = await ProcessAsync(data.ToArray(), new ImageActions() { Pixelate = 10 });

            // Assert - inside a block the shade is the same, the next block is different
            Assert.Equal((100, 20), Size(pixelated));
            Assert.Equal(TestHelpers.GetPixel(pixelated, 1, 5), TestHelpers.GetPixel(pixelated, 8, 5));
            Assert.Equal(TestHelpers.GetPixel(pixelated, 41, 5), TestHelpers.GetPixel(pixelated, 48, 15));
            Assert.NotEqual(TestHelpers.GetPixel(pixelated, 5, 5), TestHelpers.GetPixel(pixelated, 15, 5));
        }

        [Fact]
        public async Task Vignette_ShouldDarkenTheCornersOnly()
        {
            // Arrange
            var image = TestHelpers.CreateImage(100, 100, color: White);

            // Act
            var result = await ProcessAsync(image, new ImageActions() { Vignette = 1 });

            // Assert
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(result, 50, 50));
            Assert.True(TestHelpers.GetPixel(result, 1, 1).Red < 60);
        }

        [Fact]
        public async Task Quality_ShouldChangeTheSizeOfAJpeg()
        {
            // Arrange
            var photo = Properties.Resources.testJpg;

            // Act
            var low = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 300, Quality = 20 });
            var high = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 300, Quality = 95 });
            var lossyWebp = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 300, Quality = 50, Format = SKEncodedImageFormat.Webp });
            var losslessWebp = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 300, Format = SKEncodedImageFormat.Webp });

            // Assert
            Assert.True(low.Length < high.Length / 2, $"Quality 20 was {low.Length} bytes and quality 95 was {high.Length} bytes.");
            Assert.True(lossyWebp.Length < losslessWebp.Length / 2);
            Assert.Equal(SKEncodedImageFormat.Jpeg, TestHelpers.GetImageInfo(low).Format);
        }

        [Fact]
        public async Task Resampler_NearestNeighbor_ShouldNotBlendPixels()
        {
            // Arrange - four pixels made 50 times larger
            var image = TestHelpers.CreateQuadrantImage(2, 2);

            // Act
            var nearest = await ProcessAsync(image, new ImageActions() { Width = 100, Resampler = ImageResampler.NearestNeighbor });
            var smooth = await ProcessAsync(image, new ImageActions() { Width = 100 });

            // Assert - right up against the join the colour is still pure, the default blends across it
            Assert.Equal(TestHelpers.Red, TestHelpers.GetPixel(nearest, 48, 25));
            Assert.Equal(TestHelpers.Green, TestHelpers.GetPixel(nearest, 51, 25));
            Assert.NotEqual(TestHelpers.Red, TestHelpers.GetPixel(smooth, 48, 25));
        }

        [Fact]
        public async Task Watermark_ShouldBeDrawnOverTheImage()
        {
            // Arrange
            var image = TestHelpers.CreateImage(200, 200, color: White);
            var watermark = TestHelpers.CreateImage(20, 20, color: TestHelpers.Blue);

            // Act
            var bottomRight = await ProcessAsync(image, new ImageActions() { WatermarkImage = watermark });
            var topLeft = await ProcessAsync(image, new ImageActions() { WatermarkImage = watermark, WatermarkAnchor = ImageAnchor.TopLeft, WatermarkScale = 0.5, WatermarkMargin = 0 });
            var faint = await ProcessAsync(image, new ImageActions() { WatermarkImage = watermark, WatermarkOpacity = 0.5 });

            // Assert - a quarter of the width by default, in the bottom right with a small margin
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(bottomRight, 170, 170));
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(bottomRight, 130, 130));
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(bottomRight, 198, 198));

            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(topLeft, 1, 1));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(topLeft, 95, 95));
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(topLeft, 105, 105));

            TestHelpers.AssertColor(new SKColor(127, 127, 255), TestHelpers.GetPixel(faint, 170, 170));
        }

        [Fact]
        public async Task Watermark_ShouldThrowImageProcessorException_WhenItIsNotAnImage()
        {
            var image = TestHelpers.CreateImage(50, 50);
            var actions = new ImageActions() { WatermarkImage = new byte[] { 1, 2, 3 } };

            await Assert.ThrowsAsync<ImageProcessorException>(() => ImageProcessor.ProcessImageAsync(image, actions));
        }

        [Fact]
        public async Task Text_ShouldBePlacedByItsAnchor()
        {
            // Arrange
            var image = TestHelpers.CreateImage(200, 200, color: White);

            // Act
            var topLeft = await ProcessAsync(image, new ImageActions() { Text = "X", TextColor = "ff00ff", FontSize = 40, TextAnchor = ImageAnchor.TopLeft });
            var bottomRight = await ProcessAsync(image, new ImageActions() { Text = "X", TextColor = "ff00ff", FontSize = 40, TextAnchor = ImageAnchor.BottomRight });

            // Assert
            var topLeftBounds = FindColor(topLeft, Magenta);
            var bottomRightBounds = FindColor(bottomRight, Magenta);

            //a machine with no fonts installed draws no text at all, there is nothing to check then
            if (topLeftBounds.IsEmpty)
            {
                return;
            }

            Assert.True(topLeftBounds.Right < 100 && topLeftBounds.Bottom < 100, $"Text should be in the top left but was at {topLeftBounds}.");
            Assert.True(bottomRightBounds.Left > 100 && bottomRightBounds.Top > 100, $"Text should be in the bottom right but was at {bottomRightBounds}.");
        }

        [Fact]
        public async Task Text_ShouldWrapOntoMoreLines_WhenItIsTooWide()
        {
            // Arrange
            var image = TestHelpers.CreateImage(200, 200, color: White);
            var text = "one two three four five six seven eight";

            // Act
            var oneLine = await ProcessAsync(image, new ImageActions() { Text = text, TextColor = "ff00ff", FontSize = 20 });
            var wrapped = await ProcessAsync(image, new ImageActions() { Text = text, TextColor = "ff00ff", FontSize = 20, TextWrap = true });

            // Assert
            var oneLineBounds = FindColor(oneLine, Magenta);
            var wrappedBounds = FindColor(wrapped, Magenta);
            if (oneLineBounds.IsEmpty)
            {
                return;
            }

            Assert.True(wrappedBounds.Height > oneLineBounds.Height * 2, $"Wrapped text was {wrappedBounds.Height} pixels tall and one line was {oneLineBounds.Height}.");
            Assert.True(wrappedBounds.Left > 0 && wrappedBounds.Right < 199, "Wrapped text should fit inside the image.");
        }

        [Fact]
        public void RegisterFont_ShouldRejectDataThatIsNotAFont()
        {
            Assert.Throws<ImageProcessorException>(() => ImageProcessor.RegisterFont("NotAFont", new byte[] { 1, 2, 3, 4 }));
            Assert.Throws<ArgumentNullException>(() => ImageProcessor.RegisterFont("NoData", (byte[])null!));
        }

        [Fact]
        public async Task KeepMetadata_ShouldCopyExifAndResetTheOrientation()
        {
            // Arrange - this photo has exif data that says it needs a quarter turn
            var photo = TestHelpers.LoadTestImage("rotate90.jpg");

            // Act
            var without = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 150 });
            var with = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 150, KeepMetadata = true });

            // Assert
            Assert.False(ContainsText(without, "Exif"));
            Assert.True(ContainsText(with, "Exif"));

            //the pixels were turned, so the copied orientation has to say there is nothing left to do
            var details = ImageProcessor.Identify(with);
            Assert.Equal(SKEncodedOrigin.TopLeft, details.Orientation);
            Assert.Equal((150, 200), (details.Width, details.Height));
            Assert.True(TestHelpers.IsImage(with));
        }

        [Fact]
        public async Task KeepMetadata_ShouldBeIgnored_WhenTheResultIsNotAJpeg()
        {
            var photo = TestHelpers.LoadTestImage("rotate90.jpg");

            var png = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 150, KeepMetadata = true, Format = SKEncodedImageFormat.Png });

            Assert.Equal(SKEncodedImageFormat.Png, TestHelpers.GetImageInfo(png).Format);
            Assert.True(TestHelpers.IsImage(png));
        }

        [Fact]
        public void Identify_ShouldReadTheDisplayedSizeWithoutDecoding()
        {
            // Arrange
            var photo = TestHelpers.LoadTestImage("rotate90.jpg");

            // Act
            var details = ImageProcessor.Identify(photo);
            using var stream = new MemoryStream(TestHelpers.CreateImage(30, 20));
            var fromStream = ImageProcessor.Identify(stream);

            // Assert - stored as 2048x1536, displayed as 1536x2048
            Assert.Equal((1536, 2048), (details.Width, details.Height));
            Assert.Equal(SKEncodedImageFormat.Jpeg, details.Format);
            Assert.Equal(SKEncodedOrigin.RightTop, details.Orientation);
            Assert.Equal(1, details.FrameCount);

            Assert.Equal((30, 20), (fromStream.Width, fromStream.Height));
            Assert.Equal(SKEncodedImageFormat.Png, fromStream.Format);

            Assert.Throws<ImageProcessorException>(() => ImageProcessor.Identify(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public async Task ShouldWriteTheResultToAStream()
        {
            // Arrange
            var image = TestHelpers.CreateImage(100, 50);
            using var input = new MemoryStream(image);
            using var output = new MemoryStream();
            using var fluentOutput = new MemoryStream();

            // Act
            await ImageProcessor.ProcessImageAsync(input, output, new ImageActions() { Width = 40 });
            await ImageProcessor.Process(image).Width(30).ToStreamAsync(fluentOutput);

            // Assert
            Assert.Equal((40, 20), Size(output.ToArray()));
            Assert.Equal((30, 15), Size(fluentOutput.ToArray()));
        }

        [Fact]
        public async Task ShouldConvertAWideColourProfileToStandardColours()
        {
            // Arrange - an image saved in the Display P3 colour space, as a phone camera does.
            // The same orange is stored as different numbers in P3 than it is in sRGB
            var orange = new SKColor(200, 100, 50);
            using var displayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
            using var bitmap = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Opaque, displayP3));
            bitmap.Erase(orange);
            using var pixmap = bitmap.PeekPixels();
            using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100);
            var image = data.ToArray();

            // Act
            var result = await ProcessAsync(image, new ImageActions() { Width = 16 });

            // Assert - the stored numbers are not the orange, the result should be
            Assert.NotEqual(orange, TestHelpers.GetPixel(image, 5, 5));
            TestHelpers.AssertColor(orange, TestHelpers.GetPixel(result, 5, 5), tolerance: 2);
        }

        [Fact]
        public async Task FluentBuilder_ShouldSetTheNewOptions()
        {
            // Arrange
            var image = TestHelpers.CreateQuadrantImage(200, 100);

            // Act
            var result = await ImageProcessor.Process(image)
                .Crop(0, 0, 100, 50)
                .Rotate(90)
                .FlipHorizontal()
                .Width(25)
                .BackgroundColor("ffffff")
                .Brightness(1)
                .Quality(80)
                .Resampler(ImageResampler.Bilinear)
                .AllowUpscale(false)
                .Format(SKEncodedImageFormat.Png)
                .ToImageAsync();

            // Assert - the red block, turned on its side
            Assert.Equal((25, 50), Size(result));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(result, 12, 25));
        }

        private static (int Width, int Height) Size(byte[] image)
        {
            var info = TestHelpers.GetImageInfo(image);
            return (info.Width, info.Height);
        }

        private static bool ContainsText(byte[] data, string text)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(text);
            for (var i = 0; i <= data.Length - bytes.Length; i++)
            {
                var j = 0;
                while (j < bytes.Length && data[i + j] == bytes[j])
                {
                    j++;
                }

                if (j == bytes.Length)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The box around every pixel of a colour, empty when the colour is not in the image.
        /// </summary>
        private static SKRectI FindColor(byte[] image, SKColor color)
        {
            using var bitmap = SKBitmap.Decode(image);
            var bounds = SKRectI.Empty;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y) == color)
                    {
                        bounds = bounds.IsEmpty
                            ? new SKRectI(x, y, x + 1, y + 1)
                            : SKRectI.Union(bounds, new SKRectI(x, y, x + 1, y + 1));
                    }
                }
            }

            return bounds;
        }
    }
}
