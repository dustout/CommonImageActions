using SkiaSharp;

namespace CommonImageActions.Core.Tests
{
    /// <summary>
    /// Compares whole images against copies that were saved when the output was known to be
    /// right (the Snapshots folder). The other tests check sizes and a few pixels, these catch a
    /// change to how an image looks that those would miss.
    ///
    /// When a change to the output is intended, look at the new images and save them as the
    /// snapshots by running the tests once with the UPDATE_SNAPSHOTS environment variable set to 1.
    /// </summary>
    public class SnapshotTests
    {
        //how different a pixel has to be (0 to 255, on any channel) before it counts as different,
        //and how much of the image is allowed to be. Skia does not draw exactly the same on every
        //machine, the odd pixel along an edge can be a shade out
        private const int PixelTolerance = 24;
        private const double AllowedDifferentPixels = 0.005;
        private const double AllowedAverageDifference = 1.0;

        private static readonly Dictionary<string, Func<ImageActions>> Scenarios = new Dictionary<string, Func<ImageActions>>()
        {
            ["resize-width"] = () => new ImageActions() { Width = 200 },
            ["resize-stretch"] = () => new ImageActions() { Width = 200, Height = 200 },
            ["zoom-anchor-top"] = () => new ImageActions() { Width = 150, Height = 150, Mode = ImageMode.Zoom, Anchor = ImageAnchor.Top },
            ["zoom-focal-point"] = () => new ImageActions() { Width = 150, Height = 150, Mode = ImageMode.Zoom, FocalPointX = 0.85, FocalPointY = 0.5 },
            ["fit-background"] = () => new ImageActions() { Width = 200, Height = 200, Mode = ImageMode.Fit, BackgroundColor = "336699" },
            ["circle"] = () => new ImageActions() { Width = 150, Height = 150, Mode = ImageMode.Zoom, Shape = ImageShape.Circle },
            ["ellipse"] = () => new ImageActions() { Width = 200, Shape = ImageShape.Ellipse },
            ["rounded-corners"] = () => new ImageActions() { Width = 200, CornerRadius = 30 },
            ["crop"] = () => new ImageActions() { CropX = 100, CropY = 50, CropWidth = 200, CropHeight = 200, Width = 150 },
            ["rotate-90"] = () => new ImageActions() { Width = 150, Rotate = 90 },
            ["rotate-30-background"] = () => new ImageActions() { Width = 200, Rotate = 30, BackgroundColor = "ffffff" },
            ["flip-horizontal"] = () => new ImageActions() { Width = 200, FlipHorizontal = true },
            ["crop-flip-rotate"] = () => new ImageActions() { CropX = 0, CropY = 0, CropWidth = 200, CropHeight = 300, FlipVertical = true, Rotate = 270, Width = 200 },
            ["grayscale"] = () => new ImageActions() { Width = 200, IsGrayscale = true },
            ["sepia"] = () => new ImageActions() { Width = 200, IsSepia = true },
            ["invert"] = () => new ImageActions() { Width = 200, IsInverted = true },
            ["brightness-contrast-saturation"] = () => new ImageActions() { Width = 200, Brightness = 1.2, Contrast = 1.4, Saturation = 0.5 },
            ["blur"] = () => new ImageActions() { Width = 200, Blur = 4 },
            ["sharpen"] = () => new ImageActions() { Width = 200, Sharpen = 1 },
            ["pixelate"] = () => new ImageActions() { Width = 200, Pixelate = 10 },
            ["vignette"] = () => new ImageActions() { Width = 200, Vignette = 0.8 },
            ["nearest-neighbor-enlarged"] = () => new ImageActions() { CropX = 180, CropY = 130, CropWidth = 40, CropHeight = 40, Width = 200, Resampler = ImageResampler.NearestNeighbor },
            ["watermark"] = () => new ImageActions() { Width = 200, WatermarkImage = TestHelpers.CreateQuadrantImage(40, 40), WatermarkOpacity = 0.7 },
            ["everything-at-once"] = () => new ImageActions()
            {
                Width = 180,
                Height = 180,
                Mode = ImageMode.Zoom,
                Anchor = ImageAnchor.Left,
                Shape = ImageShape.RoundedRectangle,
                CornerRadius = 20,
                BackgroundColor = "222222",
                Rotate = 15,
                Contrast = 1.2,
                Vignette = 0.5,
                Sharpen = 0.5,
                WatermarkImage = TestHelpers.CreateQuadrantImage(40, 40),
                WatermarkAnchor = ImageAnchor.TopLeft
            },
        };

        public static IEnumerable<object[]> ScenarioNames => Scenarios.Keys.Select(x => new object[] { x });

        [Theory]
        [MemberData(nameof(ScenarioNames))]
        public async Task ShouldLookTheSameAsTheSnapshot(string name)
        {
            // Arrange
            var actions = Scenarios[name]();
            actions.Format = SKEncodedImageFormat.Png;

            // Act
            var actual = await ImageProcessor.ProcessImageAsync(CreateTestCard(), actions);

            // Assert
            var snapshotPath = Path.Combine(SnapshotFolder(), $"{name}.png");
            if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
            {
                Directory.CreateDirectory(SnapshotFolder());
                File.WriteAllBytes(snapshotPath, actual);
                return;
            }

            Assert.True(File.Exists(snapshotPath), $"There is no snapshot for '{name}'. Run the tests once with UPDATE_SNAPSHOTS=1 to create it, then check that {snapshotPath} looks right.");

            var difference = Compare(File.ReadAllBytes(snapshotPath), actual);
            if (difference != null)
            {
                //keep what was made so it can be looked at next to the snapshot
                var failureFolder = Path.Combine(AppContext.BaseDirectory, "SnapshotFailures");
                Directory.CreateDirectory(failureFolder);
                var actualPath = Path.Combine(failureFolder, $"{name}.png");
                File.WriteAllBytes(actualPath, actual);
                Assert.Fail($"'{name}' does not look like its snapshot: {difference} Compare {actualPath} with {snapshotPath}. If the change is intended, run the tests once with UPDATE_SNAPSHOTS=1.");
            }
        }

        [Fact]
        public void EverySnapshot_ShouldBelongToAScenario()
        {
            //a snapshot left behind by a scenario that was renamed or removed is never checked
            var snapshots = Directory.Exists(SnapshotFolder())
                ? Directory.GetFiles(SnapshotFolder(), "*.png").Select(x => Path.GetFileNameWithoutExtension(x)).ToList()
                : new List<string>();

            Assert.Empty(snapshots.Where(x => Scenarios.ContainsKey(x) == false));
        }

        [Fact]
        public void Compare_ShouldNoticeADifference_AndAllowForATinyOne()
        {
            // Arrange
            var original = CreateTestCard();
            using var bitmap = SKBitmap.Decode(original);

            //one pixel in the whole picture that is completely different
            using var onePixel = bitmap.Copy();
            onePixel.SetPixel(10, 10, new SKColor(0, 255, 0));

            //a block of the picture that is different
            using var block = bitmap.Copy();
            using (var canvas = new SKCanvas(block))
            {
                using var paint = new SKPaint { Color = new SKColor(0, 255, 0) };
                canvas.DrawRect(new SKRect(100, 100, 160, 160), paint);
            }

            // Act & Assert
            Assert.Null(Compare(original, original));
            Assert.Null(Compare(original, Encode(onePixel)));
            Assert.NotNull(Compare(original, Encode(block)));
            Assert.NotNull(Compare(original, TestHelpers.CreateImage(10, 10)));
        }

        /// <summary>
        /// Describes how two images differ, or null when they are close enough to be the same.
        /// </summary>
        private static string? Compare(byte[] expectedImage, byte[] actualImage)
        {
            using var expected = SKBitmap.Decode(expectedImage);
            using var actual = SKBitmap.Decode(actualImage);

            if (expected.Width != actual.Width || expected.Height != actual.Height)
            {
                return $"it is {actual.Width}x{actual.Height} and the snapshot is {expected.Width}x{expected.Height}.";
            }

            long totalDifference = 0;
            var differentPixels = 0;
            for (var y = 0; y < expected.Height; y++)
            {
                for (var x = 0; x < expected.Width; x++)
                {
                    var a = expected.GetPixel(x, y);
                    var b = actual.GetPixel(x, y);
                    var difference = Math.Max(
                        Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                        Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));

                    totalDifference += difference;
                    if (difference > PixelTolerance)
                    {
                        differentPixels++;
                    }
                }
            }

            var pixelCount = (double)expected.Width * expected.Height;
            var differentShare = differentPixels / pixelCount;
            var averageDifference = totalDifference / pixelCount;
            if (differentShare > AllowedDifferentPixels || averageDifference > AllowedAverageDifference)
            {
                return $"{differentShare:P2} of the pixels are different (up to {AllowedDifferentPixels:P2} is allowed) and the average difference is {averageDifference:0.00} out of 255 (up to {AllowedAverageDifference:0.00} is allowed).";
            }

            return null;
        }

        /// <summary>
        /// The picture every scenario starts from. It has smooth colour, hard edges, thin lines and
        /// a different colour in each corner, so that resizing, filters and turning it around all
        /// leave a mark that can be seen.
        /// </summary>
        private static byte[] CreateTestCard()
        {
            using var bitmap = new SKBitmap(400, 300);
            using (var canvas = new SKCanvas(bitmap))
            {
                //a smooth blend of colour across the whole picture
                using var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(400, 300),
                    new[] { new SKColor(250, 220, 60), new SKColor(230, 80, 90), new SKColor(40, 60, 160) },
                    null,
                    SKShaderTileMode.Clamp);
                using var background = new SKPaint { Shader = gradient };
                canvas.DrawRect(new SKRect(0, 0, 400, 300), background);

                //a block of colour in each corner
                using var paint = new SKPaint { IsAntialias = true };
                paint.Color = TestHelpers.Red;
                canvas.DrawRect(new SKRect(0, 0, 60, 60), paint);
                paint.Color = TestHelpers.Green;
                canvas.DrawRect(new SKRect(340, 0, 400, 60), paint);
                paint.Color = TestHelpers.Blue;
                canvas.DrawRect(new SKRect(0, 240, 60, 300), paint);
                paint.Color = TestHelpers.Yellow;
                canvas.DrawRect(new SKRect(340, 240, 400, 300), paint);

                //round shapes and thin lines
                paint.Color = new SKColor(255, 255, 255);
                canvas.DrawCircle(200, 150, 70, paint);
                paint.Color = new SKColor(20, 20, 20);
                canvas.DrawCircle(200, 150, 40, paint);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3;
                for (var x = 80; x <= 320; x += 30)
                {
                    canvas.DrawLine(x, 20, x + 40, 280, paint);
                }
            }

            return Encode(bitmap);
        }

        private static byte[] Encode(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>
        /// The Snapshots folder of this project, in the source and not where the tests are built to,
        /// so that a new snapshot is saved where it will be committed. It is found by walking up
        /// from where the tests are running to the folder the solution is in.
        /// </summary>
        private static string SnapshotFolder()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && File.Exists(Path.Combine(folder.FullName, "CommonImageActions.sln")) == false)
            {
                folder = folder.Parent;
            }

            Assert.True(folder != null, "The solution could not be found above the folder the tests are running in.");
            return Path.Combine(folder!.FullName, "CommonImageActions.Core.Tests", "Snapshots");
        }
    }
}
