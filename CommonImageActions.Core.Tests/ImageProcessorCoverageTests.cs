using SkiaSharp;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace CommonImageActions.Core.Tests
{
    public class ImageProcessorCoverageTests
    {
        private static readonly SKColor White = new SKColor(255, 255, 255);
        private static readonly SKColor Black = new SKColor(0, 0, 0);
        private static readonly SKColor Magenta = new SKColor(255, 0, 255);
        private static readonly SKColor Transparent = new SKColor(0, 0, 0, 0);

        //jpeg does not keep colours exactly, so a colour read back from one is only close
        private const int JpegTolerance = 40;

        private static Task<byte[]> ProcessAsync(byte[] image, ImageActions actions)
        {
            //png keeps the colours exactly as they were drawn
            actions.Format ??= SKEncodedImageFormat.Png;
            return ImageProcessor.ProcessImageAsync(image, actions);
        }

        private static (int Width, int Height) Size(byte[] image)
        {
            var info = TestHelpers.GetImageInfo(image);
            return (info.Width, info.Height);
        }

        // ---------------------------------------------------------------- exif orientation

        /// <summary>
        /// The stored picture is 200x100 with red top left, green top right, blue bottom left and
        /// yellow bottom right. For each exif orientation this is where those colours have to be
        /// once the picture is the right way up, worked out from what the exif standard says.
        /// </summary>
        [Theory]
        [InlineData(1, 200, 100, "red", "green", "blue", "yellow")]     //already upright
        [InlineData(2, 200, 100, "green", "red", "yellow", "blue")]     //mirrored left to right
        [InlineData(3, 200, 100, "yellow", "blue", "green", "red")]     //upside down
        [InlineData(4, 200, 100, "blue", "yellow", "red", "green")]     //mirrored top to bottom
        [InlineData(5, 100, 200, "red", "blue", "green", "yellow")]     //mirrored across the diagonal
        [InlineData(6, 100, 200, "blue", "red", "yellow", "green")]     //needs a quarter turn clockwise
        [InlineData(7, 100, 200, "yellow", "green", "blue", "red")]     //mirrored across the other diagonal
        [InlineData(8, 100, 200, "green", "yellow", "red", "blue")]     //needs a quarter turn anti-clockwise
        public async Task ShouldTurnEveryExifOrientationTheRightWayUp(int orientation, int width, int height, string topLeft, string topRight, string bottomLeft, string bottomRight)
        {
            // Arrange
            var stored = TestHelpers.CreateQuadrantImage(200, 100, SKEncodedImageFormat.Jpeg);
            var photo = TestHelpers.WithExifOrientation(stored, orientation);

            // Act
            var result = await ProcessAsync(photo, new ImageActions());
            var details = ImageProcessor.Identify(photo);

            // Assert
            Assert.Equal((width, height), Size(result));
            Assert.Equal((width, height), (details.Width, details.Height));
            TestHelpers.AssertColor(Named(topLeft), TestHelpers.GetPixel(result, width / 4, height / 4), JpegTolerance);
            TestHelpers.AssertColor(Named(topRight), TestHelpers.GetPixel(result, width * 3 / 4, height / 4), JpegTolerance);
            TestHelpers.AssertColor(Named(bottomLeft), TestHelpers.GetPixel(result, width / 4, height * 3 / 4), JpegTolerance);
            TestHelpers.AssertColor(Named(bottomRight), TestHelpers.GetPixel(result, width * 3 / 4, height * 3 / 4), JpegTolerance);
        }

        [Theory]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(5)]
        public async Task ShouldResizeFromTheDisplayedShape_ForEveryQuarterTurnOrientation(int orientation)
        {
            // Arrange - stored 200x100, displayed 100x200
            var photo = TestHelpers.WithExifOrientation(TestHelpers.CreateQuadrantImage(200, 100, SKEncodedImageFormat.Jpeg), orientation);

            // Act
            var byWidth = await ProcessAsync(photo, new ImageActions() { Width = 50 });
            var byHeight = await ProcessAsync(photo, new ImageActions() { Height = 50 });
            var zoom = await ProcessAsync(photo, new ImageActions() { Width = 60, Height = 60, Mode = ImageMode.Zoom });

            // Assert
            Assert.Equal((50, 100), Size(byWidth));
            Assert.Equal((25, 50), Size(byHeight));
            Assert.Equal((60, 60), Size(zoom));
            Assert.Equal(255, TestHelpers.GetPixel(zoom, 0, 0).Alpha);
            Assert.Equal(255, TestHelpers.GetPixel(zoom, 59, 59).Alpha);
        }

        [Fact]
        public async Task ShouldCropFlipAndRotate_InThatOrder_AfterTheExifOrientation()
        {
            // Arrange - once upright this photo is 100x200 with blue top left, red top right,
            // yellow bottom left and green bottom right
            var photo = TestHelpers.WithExifOrientation(TestHelpers.CreateQuadrantImage(200, 100, SKEncodedImageFormat.Jpeg), 6);

            // Act
            var result = await ProcessAsync(photo, new ImageActions()
            {
                //the top half: blue on the left, red on the right
                CropX = 0,
                CropY = 0,
                CropWidth = 100,
                CropHeight = 100,

                //now red on the left, blue on the right
                FlipHorizontal = true,

                //a quarter turn clockwise puts the left side at the top
                Rotate = 90,

                Width = 50
            });

            // Assert
            Assert.Equal((50, 50), Size(result));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(result, 25, 12), JpegTolerance);
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(result, 25, 37), JpegTolerance);
        }

        private static SKColor Named(string name)
        {
            switch (name)
            {
                case "red": return TestHelpers.Red;
                case "green": return TestHelpers.Green;
                case "blue": return TestHelpers.Blue;
                default: return TestHelpers.Yellow;
            }
        }

        // ---------------------------------------------------------------- sizes and placement

        [Theory]
        [InlineData(400, 400, 400, 200)]
        [InlineData(100, 400, 100, 50)]
        [InlineData(400, 50, 100, 50)]
        public async Task Max_ShouldFitInsideTheBoxAndKeepTheShape(int width, int height, int expectedWidth, int expectedHeight)
        {
            var image = TestHelpers.CreateImage(200, 100);

            var result = await ProcessAsync(image, new ImageActions() { Width = width, Height = height, Mode = ImageMode.Max });

            Assert.Equal((expectedWidth, expectedHeight), Size(result));
        }

        [Fact]
        public async Task FocalPoint_ShouldWorkTopToBottomToo()
        {
            // Arrange - four times taller than it is wide: red and green at the top, blue and yellow at the bottom
            var image = TestHelpers.CreateQuadrantImage(100, 400);

            // Act
            var top = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, FocalPointX = 0.5, FocalPointY = 0 });
            var bottom = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, FocalPointX = 0.5, FocalPointY = 1 });
            var middle = await ProcessAsync(image, new ImageActions() { Width = 100, Height = 100, Mode = ImageMode.Zoom, FocalPointX = 0.5, FocalPointY = 0.5 });

            // Assert
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(top, 25, 50));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(bottom, 25, 50));

            //in the middle the join between the top and bottom colours runs across the centre
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(middle, 25, 10));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(middle, 25, 90));
        }

        [Theory]
        [InlineData(ImageAnchor.Left, 10, 100, 190, 100)]
        [InlineData(ImageAnchor.Right, 190, 100, 10, 100)]
        [InlineData(ImageAnchor.TopLeft, 10, 100, 190, 100)]
        [InlineData(ImageAnchor.BottomRight, 190, 100, 10, 100)]
        public async Task Anchor_ShouldPlaceATallImageInFit(ImageAnchor anchor, int filledX, int filledY, int emptyX, int emptyY)
        {
            // Arrange - fitted into a square this is a 50x200 strip
            var image = TestHelpers.CreateImage(100, 400);

            // Act
            var result = await ProcessAsync(image, new ImageActions() { Width = 200, Height = 200, Mode = ImageMode.Fit, Anchor = anchor });

            // Assert
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(result, filledX, filledY));
            Assert.Equal(Transparent, TestHelpers.GetPixel(result, emptyX, emptyY));
        }

        // ---------------------------------------------------------------- text, with a font that is always there

        [Theory]
        [InlineData(ImageAnchor.TopLeft, 0, 0)]
        [InlineData(ImageAnchor.Top, 1, 0)]
        [InlineData(ImageAnchor.TopRight, 2, 0)]
        [InlineData(ImageAnchor.Left, 0, 1)]
        [InlineData(ImageAnchor.Center, 1, 1)]
        [InlineData(ImageAnchor.Right, 2, 1)]
        [InlineData(ImageAnchor.BottomLeft, 0, 2)]
        [InlineData(ImageAnchor.Bottom, 1, 2)]
        [InlineData(ImageAnchor.BottomRight, 2, 2)]
        public async Task Text_ShouldBeDrawnAtEveryAnchor(ImageAnchor anchor, int column, int row)
        {
            // Arrange - the image is thought of as a three by three grid
            TestFont.Register();
            var image = TestHelpers.CreateImage(300, 300, color: White);

            // Act
            var result = await ProcessAsync(image, new ImageActions()
            {
                Text = "A",
                FontFamily = TestFont.FamilyName,
                FontSize = 60,
                TextColor = "ff00ff",
                TextAnchor = anchor
            });

            // Assert - the test font draws every letter as a block 60% of the font size wide and 70% high
            var bounds = FindColor(result, Magenta);
            Assert.False(bounds.IsEmpty, "No text was drawn.");
            Assert.InRange(bounds.Width, 34, 38);
            Assert.InRange(bounds.Height, 40, 44);
            Assert.Equal(column, bounds.MidX / 100);
            Assert.Equal(row, bounds.MidY / 100);

            //and it is kept off the edge
            Assert.True(bounds.Left > 0 && bounds.Top > 0 && bounds.Right < 300 && bounds.Bottom < 300);
        }

        [Fact]
        public async Task Text_ShouldBeSizedToFillTheImage_WhenNoFontSizeIsGiven()
        {
            // Arrange
            TestFont.Register();
            var image = TestHelpers.CreateImage(400, 100, color: White);

            // Act
            var shortText = await ProcessAsync(image, new ImageActions() { Text = "A", FontFamily = TestFont.FamilyName, TextColor = "ff00ff" });
            var longText = await ProcessAsync(image, new ImageActions() { Text = "AAAAAAAAAAAAAAAAAAAA", FontFamily = TestFont.FamilyName, TextColor = "ff00ff" });

            // Assert - a short text is as tall as the image allows, 85% of the height
            var shortBounds = FindColor(shortText, Magenta);
            Assert.InRange(shortBounds.Height, 56, 62);
            Assert.InRange(shortBounds.MidX, 195, 205);
            Assert.InRange(shortBounds.MidY, 40, 60);

            //a long text is shrunk until it is three quarters of the width
            var longBounds = FindColor(longText, Magenta);
            Assert.InRange(longBounds.Width, 270, 305);
            Assert.True(longBounds.Height < shortBounds.Height);
        }

        [Fact]
        public async Task Text_ShouldWrapOnWords_AndKeepEveryLineInsideTheImage()
        {
            // Arrange - each letter is 32 pixels wide at this size, so only one word fits on a line
            TestFont.Register();
            var image = TestHelpers.CreateImage(200, 300, color: White);
            var actions = new ImageActions()
            {
                Text = "AAAA AAAA AAAA",
                FontFamily = TestFont.FamilyName,
                FontSize = 40,
                TextColor = "ff00ff"
            };

            // Act
            var oneLine = await ProcessAsync(image, new ImageActions(actions));
            var wrapped = await ProcessAsync(image, new ImageActions(actions) { TextWrap = true });
            var twoParagraphs = await ProcessAsync(image, new ImageActions(actions) { Text = "AA\nAA", TextWrap = true });

            // Assert - not wrapped it is one line, however much of it does not fit
            var oneLineBounds = FindColor(oneLine, Magenta);
            Assert.InRange(oneLineBounds.Height, 26, 30);

            //wrapped it is three lines, one line of text at this size is 40 pixels
            var wrappedBounds = FindColor(wrapped, Magenta);
            Assert.InRange(wrappedBounds.Height, 26 + 80, 30 + 80);
            Assert.True(wrappedBounds.Left > 0 && wrappedBounds.Right < 200, "Wrapped text should fit inside the image.");
            Assert.InRange(wrappedBounds.MidY, 140, 160);

            //a line break in the text always starts a new line
            Assert.InRange(FindColor(twoParagraphs, Magenta).Height, 26 + 40, 30 + 40);
        }

        [Fact]
        public async Task Text_ShouldBeUpright_OnAPhotoThatExifTurnsUpsideDown()
        {
            // Arrange
            TestFont.Register();
            var photo = TestHelpers.WithExifOrientation(TestHelpers.CreateImage(300, 300, SKEncodedImageFormat.Jpeg, White), 3);

            // Act - the test font draws an L with its foot at the bottom right
            var result = await ProcessAsync(photo, new ImageActions()
            {
                Text = "L",
                FontFamily = TestFont.FamilyName,
                FontSize = 200,
                TextColor = "ff00ff"
            });

            // Assert - the upright is at the top left of the letter and the foot at the bottom right.
            // The top right of the letter is empty; upside down it would be the bottom left that was
            var bounds = FindColor(result, Magenta);
            Assert.False(bounds.IsEmpty, "No text was drawn.");
            Assert.Equal(Magenta, TestHelpers.GetPixel(result, bounds.Left + 10, bounds.Top + 10));
            Assert.Equal(Magenta, TestHelpers.GetPixel(result, bounds.Right - 10, bounds.Bottom - 10));
            Assert.NotEqual(Magenta, TestHelpers.GetPixel(result, bounds.Right - 10, bounds.Top + 10));
        }

        [Fact]
        public async Task Text_ShouldUseInitials_AndTheColourThatWasAskedFor()
        {
            // Arrange
            TestFont.Register();
            var image = TestHelpers.CreateImage(300, 100, color: White);

            // Act
            var initials = await ProcessAsync(image, new ImageActions() { Text = "John Ronald Doe", AsInitials = true, FontFamily = TestFont.FamilyName, FontSize = 50, TextColor = "ff00ff" });
            var full = await ProcessAsync(image, new ImageActions() { Text = "John", FontFamily = TestFont.FamilyName, FontSize = 50, TextColor = "ff00ff" });
            var defaultColor = await ProcessAsync(TestHelpers.CreateImage(300, 100, color: Black), new ImageActions() { Text = "A", FontFamily = TestFont.FamilyName, FontSize = 50 });
            var badColor = await ProcessAsync(TestHelpers.CreateImage(300, 100, color: Black), new ImageActions() { Text = "A", FontFamily = TestFont.FamilyName, FontSize = 50, TextColor = "not a colour" });

            // Assert - two letters are narrower than four, each letter takes up 40 pixels at this size
            Assert.InRange(FindColor(initials, Magenta).Width, 68, 72);
            Assert.InRange(FindColor(full, Magenta).Width, 148, 152);

            //text is white unless a colour is given, and when the colour can not be understood
            Assert.False(FindColor(defaultColor, White).IsEmpty);
            Assert.False(FindColor(badColor, White).IsEmpty);
        }

        [Fact]
        public async Task RegisterFont_ShouldAcceptAFontFile()
        {
            // Arrange
            var fontFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ttf");
            File.WriteAllBytes(fontFile, TestFont.Create());
            try
            {
                // Act
                ImageProcessor.RegisterFont("FontFromAFile", fontFile);
                var result = await ProcessAsync(TestHelpers.CreateImage(100, 100, color: White), new ImageActions()
                {
                    Text = "A",
                    FontFamily = "fontfromafile", //the name is not case sensitive
                    FontSize = 50,
                    TextColor = "ff00ff"
                });

                // Assert
                Assert.InRange(FindColor(result, Magenta).Width, 28, 32);
            }
            finally
            {
                File.Delete(fontFile);
            }
        }

        [Fact]
        public async Task Text_ShouldStillReturnAnImage_WhenTheFontIsNotInstalled()
        {
            var image = TestHelpers.CreateImage(100, 100, color: White);

            var result = await ProcessAsync(image, new ImageActions() { Text = "A", FontFamily = "A Font That Is Not Installed Anywhere" });

            Assert.Equal((100, 100), Size(result));
        }

        // ---------------------------------------------------------------- watermark

        [Theory]
        [InlineData(ImageAnchor.TopLeft, 0, 0)]
        [InlineData(ImageAnchor.Top, 1, 0)]
        [InlineData(ImageAnchor.TopRight, 2, 0)]
        [InlineData(ImageAnchor.Left, 0, 1)]
        [InlineData(ImageAnchor.Center, 1, 1)]
        [InlineData(ImageAnchor.Right, 2, 1)]
        [InlineData(ImageAnchor.BottomLeft, 0, 2)]
        [InlineData(ImageAnchor.Bottom, 1, 2)]
        [InlineData(ImageAnchor.BottomRight, 2, 2)]
        public async Task Watermark_ShouldBeDrawnAtEveryAnchor(ImageAnchor anchor, int column, int row)
        {
            // Arrange
            var image = TestHelpers.CreateImage(300, 300, color: White);
            var watermark = TestHelpers.CreateImage(20, 20, color: TestHelpers.Blue);

            // Act - a tenth of the width is 30 pixels, with 9 pixels (3%) kept clear of the edge
            var result = await ProcessAsync(image, new ImageActions() { WatermarkImage = watermark, WatermarkAnchor = anchor, WatermarkScale = 0.1 });

            // Assert
            var bounds = FindColor(result, TestHelpers.Blue);
            Assert.InRange(bounds.Width, 28, 30);
            Assert.InRange(bounds.Height, 28, 30);
            Assert.Equal(column, bounds.MidX / 100);
            Assert.Equal(row, bounds.MidY / 100);
            Assert.True(bounds.Left >= 9 && bounds.Top >= 9 && bounds.Right <= 291 && bounds.Bottom <= 291, $"The watermark at {bounds} is too close to the edge.");
        }

        [Fact]
        public async Task Watermark_ShouldKeepItsShape_AndNeverBeTallerThanTheImage()
        {
            // Arrange - a watermark that is much taller than it is wide, on an image that is short
            var image = TestHelpers.CreateImage(400, 100, color: White);
            var tallWatermark = TestHelpers.CreateImage(20, 200, color: TestHelpers.Blue);

            // Act - half the width would be 200 wide and 2000 high
            var result = await ProcessAsync(image, new ImageActions() { WatermarkImage = tallWatermark, WatermarkScale = 0.5, WatermarkMargin = 10 });

            // Assert - it is cut down to the 80 pixels of height there is room for, and stays ten times taller than wide
            var bounds = FindColor(result, TestHelpers.Blue);
            Assert.InRange(bounds.Height, 78, 80);
            Assert.InRange(bounds.Width, 7, 8);
        }

        [Fact]
        public async Task Watermark_ShouldGoOverTheTopOfText()
        {
            // Arrange
            TestFont.Register();
            var image = TestHelpers.CreateImage(100, 100, color: White);
            var watermark = TestHelpers.CreateImage(10, 10, color: TestHelpers.Blue);

            // Act - text that covers the whole image, and a watermark in the middle of it
            var result = await ProcessAsync(image, new ImageActions()
            {
                Text = "A",
                FontFamily = TestFont.FamilyName,
                FontSize = 200,
                TextColor = "ff00ff",
                WatermarkImage = watermark,
                WatermarkAnchor = ImageAnchor.Center,
                WatermarkScale = 0.2
            });

            // Assert
            Assert.Equal(TestHelpers.Blue, TestHelpers.GetPixel(result, 50, 50));
            Assert.Equal(Magenta, TestHelpers.GetPixel(result, 50, 25));
        }

        // ---------------------------------------------------------------- formats that are read

        [Fact]
        public async Task ShouldReadWebp_AndWriteItBackAsWebp()
        {
            var webp = TestHelpers.CreateImage(60, 40, SKEncodedImageFormat.Webp, TestHelpers.Blue);

            var result = await ImageProcessor.ProcessImageAsync(webp, new ImageActions() { Width = 30 });

            Assert.Equal((30, 20, SKEncodedImageFormat.Webp), TestHelpers.GetImageInfo(result));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(result, 15, 10));
        }

        [Fact]
        public async Task ShouldReadBmpAndIco_AndWriteThemAsPng()
        {
            // Arrange - skia can read these two but not write them
            var bmp = TestHelpers.CreateBmp(60, 40, TestHelpers.Green);
            var ico = TestHelpers.CreateIco(32, TestHelpers.Blue);

            // Act
            var fromBmp = await ImageProcessor.ProcessImageAsync(bmp, new ImageActions() { Width = 30 });
            var fromIco = await ImageProcessor.ProcessImageAsync(ico, new ImageActions() { Width = 16 });

            // Assert
            Assert.Equal((30, 20, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(fromBmp));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(fromBmp, 15, 10));
            Assert.Equal((16, 16, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(fromIco));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(fromIco, 8, 8));

            Assert.Equal(SKEncodedImageFormat.Bmp, ImageProcessor.Identify(bmp).Format);
            Assert.Equal(SKEncodedImageFormat.Ico, ImageProcessor.Identify(ico).Format);
        }

        [Fact]
        public async Task ShouldUseTheFirstFrame_OfAnAnimatedGif()
        {
            // Arrange - two frames, the first red and the second blue
            var gif = TestHelpers.CreateAnimatedGif();

            // Act
            var details = ImageProcessor.Identify(gif);
            var result = await ImageProcessor.ProcessImageAsync(gif, new ImageActions() { Width = 20 });

            // Assert
            Assert.Equal(2, details.FrameCount);
            Assert.Equal(SKEncodedImageFormat.Gif, details.Format);
            Assert.Equal((20, 20, SKEncodedImageFormat.Png), TestHelpers.GetImageInfo(result));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(result, 10, 10));
        }

        [Fact]
        public async Task ShouldReadAGrayscaleJpeg()
        {
            // Arrange - a jpeg with one channel instead of three
            using var bitmap = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Gray8, SKAlphaType.Opaque));
            bitmap.Erase(new SKColor(120, 120, 120));
            using var pixmap = bitmap.PeekPixels();
            using var data = pixmap.Encode(SKEncodedImageFormat.Jpeg, 95);
            var jpeg = data.ToArray();

            // Act
            var result = await ProcessAsync(jpeg, new ImageActions() { Width = 20 });
            var tinted = await ProcessAsync(jpeg, new ImageActions() { Width = 20, IsSepia = true });

            // Assert
            Assert.Equal((20, 20), Size(result));
            TestHelpers.AssertColor(new SKColor(120, 120, 120), TestHelpers.GetPixel(result, 10, 10), tolerance: 6);

            //and colour can be added to it
            var sepia = TestHelpers.GetPixel(tinted, 10, 10);
            Assert.True(sepia.Red > sepia.Blue + 20, $"Sepia should have added colour but the pixel was {sepia}.");
        }

        [Fact]
        public async Task TransparentPixels_ShouldBeBlackInAJpeg_UnlessThereIsABackground()
        {
            // Arrange - a png that is transparent all over
            var transparent = TestHelpers.CreateImage(40, 40, SKEncodedImageFormat.Png, Transparent);

            // Act
            var png = await ProcessAsync(transparent, new ImageActions() { Width = 20 });
            var jpeg = await ImageProcessor.ProcessImageAsync(transparent, new ImageActions() { Width = 20, Format = SKEncodedImageFormat.Jpeg });
            var jpegWithBackground = await ImageProcessor.ProcessImageAsync(transparent, new ImageActions() { Width = 20, Format = SKEncodedImageFormat.Jpeg, BackgroundColor = "ffffff" });

            // Assert - a jpeg has no transparency
            Assert.Equal(Transparent, TestHelpers.GetPixel(png, 10, 10));
            TestHelpers.AssertColor(Black, TestHelpers.GetPixel(jpeg, 10, 10), tolerance: 6);
            TestHelpers.AssertColor(White, TestHelpers.GetPixel(jpegWithBackground, 10, 10), tolerance: 6);
        }

        // ---------------------------------------------------------------- metadata

        [Fact]
        public async Task KeepMetadata_ShouldCopyEveryKindOfMetadata_AndFixTheOrientationInEitherByteOrder()
        {
            // Arrange - exif written the other way round (big endian) with a thumbnail list after it,
            // plus the two other kinds of metadata a photo can carry
            var bigEndianExif = new byte[]
            {
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
                (byte)'M', (byte)'M', 0x00, 0x2A,
                0x00, 0x00, 0x00, 0x08, //the tags start 8 bytes in
                0x00, 0x01, //one tag
                0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00, //orientation: quarter turn
                0x00, 0x00, 0x00, 0x1A //another list of tags follows (where a thumbnail would be)
            };
            var xmp = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>made by a test</x:xmpmeta>");
            var iptc = Encoding.ASCII.GetBytes("Photoshop 3.0\0caption written by a test");
            var comment = Encoding.ASCII.GetBytes("a comment that is not metadata");

            var photo = TestHelpers.CreateQuadrantImage(200, 100, SKEncodedImageFormat.Jpeg);
            photo = InsertSegment(photo, 0xFE, comment);
            photo = InsertSegment(photo, 0xED, iptc);
            photo = InsertSegment(photo, 0xE1, xmp);
            photo = InsertSegment(photo, 0xE1, bigEndianExif);
            Assert.Equal(SKEncodedOrigin.RightTop, ImageProcessor.Identify(photo).Orientation);

            // Act
            var result = await ImageProcessor.ProcessImageAsync(photo, new ImageActions() { Width = 50, KeepMetadata = true });

            // Assert - the picture was turned, and all three kinds of metadata came across
            var details = ImageProcessor.Identify(result);
            Assert.Equal((50, 100), (details.Width, details.Height));
            Assert.Equal(SKEncodedOrigin.TopLeft, details.Orientation);
            Assert.True(Contains(result, "Exif"));
            Assert.True(Contains(result, "made by a test"));
            Assert.True(Contains(result, "caption written by a test"));

            //anything else in the file is left behind
            Assert.False(Contains(result, "a comment that is not metadata"));
        }

        [Fact]
        public async Task KeepMetadata_ShouldReturnTheImage_WhenTheMetadataIsDamagedOrMissing()
        {
            // Arrange - exif that says its tags are somewhere far past the end of the data
            var damagedExif = new byte[]
            {
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
                (byte)'I', (byte)'I', 0x2A, 0x00,
                0xFF, 0xFF, 0x00, 0x00
            };
            var notExif = Encoding.ASCII.GetBytes("XXXX\0\0 this is not exif at all");
            var plain = TestHelpers.CreateImage(60, 40, SKEncodedImageFormat.Jpeg);
            var damaged = InsertSegment(InsertSegment(plain, 0xE1, notExif), 0xE1, damagedExif);
            var png = TestHelpers.CreateImage(60, 40, SKEncodedImageFormat.Png);

            // Act
            var fromPlain = await ImageProcessor.ProcessImageAsync(plain, new ImageActions() { Width = 30, KeepMetadata = true });
            var fromDamaged = await ImageProcessor.ProcessImageAsync(damaged, new ImageActions() { Width = 30, KeepMetadata = true });
            var fromPng = await ImageProcessor.ProcessImageAsync(png, new ImageActions() { Width = 30, KeepMetadata = true, Format = SKEncodedImageFormat.Jpeg });

            // Assert
            Assert.Equal((30, 20, SKEncodedImageFormat.Jpeg), TestHelpers.GetImageInfo(fromPlain));
            Assert.Equal((30, 20, SKEncodedImageFormat.Jpeg), TestHelpers.GetImageInfo(fromDamaged));
            Assert.Equal((30, 20, SKEncodedImageFormat.Jpeg), TestHelpers.GetImageInfo(fromPng));
            Assert.False(Contains(fromPlain, "Exif"));
        }

        /// <summary>
        /// Add a segment (a marker, a length and some data) to a jpeg, straight after its first two bytes.
        /// </summary>
        private static byte[] InsertSegment(byte[] jpeg, byte marker, byte[] payload)
        {
            var length = payload.Length + 2;
            var segment = new byte[] { 0xFF, marker, (byte)(length >> 8), (byte)length }.Concat(payload).ToArray();
            return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
        }

        private static bool Contains(byte[] data, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
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

        // ---------------------------------------------------------------- the fluent builder

        /// <summary>
        /// A builder that lets a test look at the actions it has put together.
        /// </summary>
        private class InspectableBuilder : ImageActionsBuilder<InspectableBuilder>
        {
            public InspectableBuilder() : base(null)
            {
            }

            public ImageActions Result => Actions;
        }

        [Fact]
        public void FluentBuilder_ShouldSetEveryOption()
        {
            // Arrange
            var watermark = TestHelpers.CreateImage(4, 4);

            // Act
            var actions = new InspectableBuilder()
                .Width(1).Height(2).Mode(ImageMode.Fit).Anchor(ImageAnchor.Top).FocalPoint(0.25, 0.75)
                .AllowUpscale(false).Resampler(ImageResampler.Mipmap).Crop(3, 4, 5, 6).Rotate(7)
                .FlipHorizontal().FlipVertical().Shape(ImageShape.Ellipse).CornerRadius(8).BackgroundColor("010203")
                .Text("text").AsInitials(true).TextColor("040506").FontFamily("font").FontSize(9)
                .TextAnchor(ImageAnchor.Bottom).TextWrap().ImageColor("070809").ChooseImageColorFromTextValue(true)
                .IsGrayscale(true).IsSepia().IsInverted().Brightness(1.1).Contrast(1.2).Saturation(1.3)
                .Blur(1.4).Sharpen().Pixelate(10).Vignette()
                .Watermark(watermark, ImageAnchor.TopLeft, 0.5, 0.6, 11)
                .KeepMetadata().Format(SKEncodedImageFormat.Webp).Quality(12).PdfPassword("password").Page(13)
                .Result;

            // Assert
            Assert.Equal((1, 2), (actions.Width, actions.Height));
            Assert.Equal(ImageMode.Fit, actions.Mode);
            Assert.Equal(ImageAnchor.Top, actions.Anchor);
            Assert.Equal((0.25, 0.75), (actions.FocalPointX, actions.FocalPointY));
            Assert.False(actions.AllowUpscale);
            Assert.Equal(ImageResampler.Mipmap, actions.Resampler);
            Assert.Equal((3, 4, 5, 6), (actions.CropX, actions.CropY, actions.CropWidth, actions.CropHeight));
            Assert.Equal(7, actions.Rotate);
            Assert.True(actions.FlipHorizontal);
            Assert.True(actions.FlipVertical);
            Assert.Equal(ImageShape.Ellipse, actions.Shape);
            Assert.Equal(8, actions.CornerRadius);
            Assert.Equal("010203", actions.BackgroundColor);
            Assert.Equal("text", actions.Text);
            Assert.True(actions.AsInitials);
            Assert.Equal("040506", actions.TextColor);
            Assert.Equal("font", actions.FontFamily);
            Assert.Equal(9, actions.FontSize);
            Assert.Equal(ImageAnchor.Bottom, actions.TextAnchor);
            Assert.True(actions.TextWrap);
            Assert.Equal("070809", actions.ImageColor);
            Assert.True(actions.ChooseImageColorFromTextValue);
            Assert.True(actions.IsGrayscale);
            Assert.True(actions.IsSepia);
            Assert.True(actions.IsInverted);
            Assert.Equal(1.1, actions.Brightness);
            Assert.Equal(1.2, actions.Contrast);
            Assert.Equal(1.3, actions.Saturation);
            Assert.Equal(1.4, actions.Blur);
            Assert.Equal(1, actions.Sharpen);
            Assert.Equal(10, actions.Pixelate);
            Assert.Equal(0.5, actions.Vignette);
            Assert.Same(watermark, actions.WatermarkImage);
            Assert.Equal(ImageAnchor.TopLeft, actions.WatermarkAnchor);
            Assert.Equal(0.5, actions.WatermarkOpacity);
            Assert.Equal(0.6, actions.WatermarkScale);
            Assert.Equal(11, actions.WatermarkMargin);
            Assert.True(actions.KeepMetadata);
            Assert.Equal(SKEncodedImageFormat.Webp, actions.Format);
            Assert.Equal(12, actions.Quality);
            Assert.Equal("password", actions.PdfPassword);
            Assert.Equal(13, actions.Page);

            //the page can be given as text too
            Assert.Equal(14, new InspectableBuilder().Page("14").Result.Page);
        }

        [Fact]
        public void FluentBuilder_ShouldHaveAMethodForEveryImageAction()
        {
            // Arrange - call every method the builder has with a value for each of its arguments
            var builder = new InspectableBuilder();
            var methods = typeof(ImageActionsBuilder<InspectableBuilder>)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(x => x.ReturnType == typeof(InspectableBuilder))
                .ToList();

            // Act
            foreach (var method in methods)
            {
                //the page number given as text has to be a number, the sample text is not
                var arguments = method.GetParameters()
                    .Select(x => method.Name == "Page" && x.ParameterType == typeof(string) ? "3" : TestHelpers.CreateValue(x.ParameterType))
                    .ToArray();
                method.Invoke(builder, arguments);
            }

            // Assert - an option that is added to ImageActions without a fluent method fails here
            Assert.NotEmpty(methods);
            foreach (var property in typeof(ImageActions).GetProperties().Where(x => x.CanRead && x.CanWrite))
            {
                Assert.True(property.GetValue(builder.Result) != null, $"There is no fluent method that sets {property.Name}.");
            }
        }

        [Fact]
        public async Task FluentBuilder_ShouldStartFromActions_AndMakeVirtualImages()
        {
            // Arrange
            var image = TestHelpers.CreateImage(100, 50);
            var defaults = new ImageActions() { Width = 40 };

            // Act
            var fromDefaults = await new ImageActionQueryBuilder(image, defaults).Format(SKEncodedImageFormat.Png).ToImageAsync();
            var fromNothing = await new ImageActionQueryBuilder(image, null).ToImageAsync();
            var virtualImage = await new ImageActionQueryBuilder().Width(30).Height(20).ImageColor("0000ff").ToImageAsync();
            var notVirtual = new ImageActionQueryBuilder().IsVirtual(false);
            var blocking = ImageProcessor.Process(image).Width(10).ToImage();

            // Assert
            Assert.Equal((40, 20), Size(fromDefaults));
            Assert.Equal((100, 50), Size(fromNothing));
            Assert.Equal((30, 20), Size(virtualImage));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(virtualImage, 15, 10));
            Assert.Equal((10, 5), Size(blocking));

            //with no image and not virtual there is nothing to make an image from
            await Assert.ThrowsAsync<ArgumentNullException>(() => notVirtual.ToImageAsync());
            await Assert.ThrowsAsync<ArgumentNullException>(() => ImageProcessor.Process(image).ToStreamAsync(null!));
        }

        // ---------------------------------------------------------------- the other ways in

        [Fact]
        public async Task ShouldRejectMissingStreams()
        {
            var image = TestHelpers.CreateImage(10, 10);
            using var stream = new MemoryStream(image);

            await Assert.ThrowsAsync<ArgumentNullException>(() => ImageProcessor.ProcessImageAsync((Stream)null!, new ImageActions()));
            await Assert.ThrowsAsync<ArgumentNullException>(() => ImageProcessor.ProcessImageAsync(stream, null!, new ImageActions()));
            Assert.Throws<ArgumentNullException>(() => ImageProcessor.Identify((byte[])null!));
            Assert.Throws<ArgumentNullException>(() => ImageProcessor.Identify((Stream)null!));
            Assert.Throws<ArgumentNullException>(() => ImageProcessor.RegisterFont(null!, new byte[1]));
        }

        [Fact]
        public async Task VirtualImage_ShouldUseTheColourGiven_AndBlackWhenItCanNotBeUnderstood()
        {
            // Act
            var blue = await ImageProcessor.ProcessVirtualImageAsync(new ImageActions() { ImageColor = "0000ff", Width = 20, Height = 20 });
            var notAColor = await ImageProcessor.ProcessVirtualImageAsync(new ImageActions() { ImageColor = "zzzzzz", Width = 20, Height = 20 });
            var nothingSet = await ImageProcessor.ProcessVirtualImageAsync(new ImageActions() { Width = 20, Height = 20 });
            var fromText = await ImageProcessor.ProcessVirtualImageAsync(new ImageActions() { Text = "Dustin", ChooseImageColorFromTextValue = true, FontFamily = "A Font That Is Not Installed Anywhere", Width = 20, Height = 20 });

            // Assert
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(blue, 10, 10));
            TestHelpers.AssertColor(Black, TestHelpers.GetPixel(notAColor, 10, 10));
            TestHelpers.AssertColor(Black, TestHelpers.GetPixel(nothingSet, 10, 10));

            //a colour picked from the text is one of the colours in the list
            var expected = ImageProcessor.BackgroundColours[(int)(ImageProcessor.CalculateHash("Dustin") % (ulong)ImageProcessor.BackgroundColours.Count)];
            Assert.True(SKColor.TryParse($"#{expected}", out var expectedColor));
            TestHelpers.AssertColor(expectedColor, TestHelpers.GetPixel(fromText, 1, 1));
        }

        [Fact]
        public async Task VirtualImage_ShouldBeAnySize_EvenWhenImagesAreNotToBeEnlarged()
        {
            //there is no original to be larger than
            var result = await ImageProcessor.ProcessVirtualImageAsync(new ImageActions() { ImageColor = "0000ff", Width = 500, Height = 300, AllowUpscale = false });

            Assert.Equal((500, 300), Size(result));
        }

        [Fact]
        public async Task Shapes_ShouldCutTheCornersOff()
        {
            // Arrange
            var image = TestHelpers.CreateImage(100, 60);

            // Act
            var ellipse = await ProcessAsync(image, new ImageActions() { Shape = ImageShape.Ellipse });
            var rounded = await ProcessAsync(image, new ImageActions() { Shape = ImageShape.RoundedRectangle });
            var cornerOnly = await ProcessAsync(image, new ImageActions() { CornerRadius = 30 });
            var circle = await ProcessAsync(image, new ImageActions() { Shape = ImageShape.Circle });

            // Assert - the very corner is gone and the middle is still there
            foreach (var result in new[] { ellipse, rounded, cornerOnly, circle })
            {
                Assert.Equal(Transparent, TestHelpers.GetPixel(result, 0, 0));
                TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(result, 50, 30));
            }

            //an ellipse reaches the middle of each side, a circle is only as wide as the image is high
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(ellipse, 2, 30));
            Assert.Equal(Transparent, TestHelpers.GetPixel(circle, 2, 30));

            //a bigger corner radius cuts off more
            Assert.Equal(Transparent, TestHelpers.GetPixel(cornerOnly, 5, 5));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(rounded, 5, 5));
        }

        [Fact]
        public async Task Crop_ShouldRejectASizeThatIsNotPositive()
        {
            var image = TestHelpers.CreateImage(100, 60);
            var actions = new ImageActions() { CropX = 0, CropY = 0, CropWidth = 0, CropHeight = 10 };

            await Assert.ThrowsAsync<ImageProcessorException>(() => ImageProcessor.ProcessImageAsync(image, actions));
        }

        [Theory]
        [InlineData(ImageResampler.NearestNeighbor)]
        [InlineData(ImageResampler.Bilinear)]
        [InlineData(ImageResampler.Mipmap)]
        [InlineData(ImageResampler.Bicubic)]
        public async Task EveryResampler_ShouldResizeTheImage(ImageResampler resampler)
        {
            var image = TestHelpers.CreateQuadrantImage(200, 100);

            var result = await ProcessAsync(image, new ImageActions() { Width = 50, Resampler = resampler });

            //away from the joins every resampler gives the same colours
            Assert.Equal((50, 25), Size(result));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(result, 10, 5));
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(result, 40, 20));
        }

        [Fact]
        public async Task PixelateAndBlur_ShouldWorkTogether_AndOnAnExifRotatedPhoto()
        {
            // Arrange
            var photo = TestHelpers.WithExifOrientation(TestHelpers.CreateQuadrantImage(200, 100, SKEncodedImageFormat.Jpeg), 6);

            // Act
            var result = await ProcessAsync(photo, new ImageActions() { Pixelate = 10, Blur = 2, Sharpen = 0.5, Vignette = 0.2 });

            // Assert - still the upright shape, with the colours where they should be and solid to the edge
            Assert.Equal((100, 200), Size(result));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(result, 25, 50), JpegTolerance);
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(result, 75, 150), JpegTolerance);
            Assert.Equal(255, TestHelpers.GetPixel(result, 0, 0).Alpha);
            Assert.Equal(255, TestHelpers.GetPixel(result, 99, 199).Alpha);
        }

        // ---------------------------------------------------------------- many at once, large, and memory

        [Fact]
        public async Task ProcessImagesAsync_ShouldKeepTheOrder_OfManyDifferentImages()
        {
            // Arrange - forty images that are each a different width
            var images = Enumerable.Range(1, 40).Select(x => TestHelpers.CreateImage(x * 10, 20)).ToList();

            // Act
            var results = (await ImageProcessor.ProcessImagesAsync(images, new ImageActions() { Height = 10 })).ToList();

            // Assert
            Assert.Equal(40, results.Count);
            for (var i = 0; i < results.Count; i++)
            {
                Assert.Equal(((i + 1) * 5, 10), Size(results[i]));
            }
        }

        [Fact]
        public async Task ProcessImagesAsync_ShouldFail_WhenOneOfTheImagesCanNotBeRead()
        {
            var images = new[] { TestHelpers.CreateImage(10, 10), new byte[] { 1, 2, 3 }, TestHelpers.CreateImage(10, 10) };

            await Assert.ThrowsAsync<ImageProcessorException>(() => ImageProcessor.ProcessImagesAsync(images, new ImageActions() { Width = 5 }));
        }

        [Fact]
        public async Task ShouldProcessALargeImage()
        {
            // Arrange - 24 megapixels, the size of a photo from a good camera
            var large = TestHelpers.CreateQuadrantImage(6000, 4000, SKEncodedImageFormat.Jpeg);

            // Act
            var thumbnail = await ImageProcessor.ProcessImageAsync(large, new ImageActions() { Width = 300 });
            var details = ImageProcessor.Identify(large);

            // Assert
            Assert.Equal((6000, 4000), (details.Width, details.Height));
            Assert.Equal((300, 200, SKEncodedImageFormat.Jpeg), TestHelpers.GetImageInfo(thumbnail));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(thumbnail, 75, 50), JpegTolerance);
            TestHelpers.AssertColor(TestHelpers.Yellow, TestHelpers.GetPixel(thumbnail, 225, 150), JpegTolerance);
        }

        [Fact]
        public async Task ShouldNotKeepHoldOfMemory_WhenManyImagesAreProcessed()
        {
            // Arrange - each of these takes about 12 megabytes of memory while it is being worked on,
            // and that memory is outside of what the .net garbage collector looks after
            var photo = TestHelpers.CreateQuadrantImage(2000, 1500, SKEncodedImageFormat.Jpeg);
            var actions = new ImageActions() { Width = 400, Blur = 1, Text = "A" };

            //let everything that is only set up once be set up
            for (var i = 0; i < 5; i++)
            {
                await ImageProcessor.ProcessImageAsync(photo, actions);
            }

            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var before = process.PrivateMemorySize64;

            // Act
            for (var i = 0; i < 150; i++)
            {
                await ImageProcessor.ProcessImageAsync(photo, actions);
            }

            // Assert - if each image was left behind this would have grown by around 1800 megabytes.
            // The limit is generous because other tests are using memory at the same time
            process.Refresh();
            var growth = (process.PrivateMemorySize64 - before) / (1024 * 1024);
            Assert.True(growth < 600, $"Memory grew by {growth} megabytes while processing 150 images.");
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
