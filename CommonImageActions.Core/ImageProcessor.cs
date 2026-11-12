using Microsoft.Maui.Graphics;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CommonImageActions.Core
{
    public static class ImageProcessor
    {
        public static int JpegQuality = 90;
        public static int GifQuality = 90;
        public static int CornerRadius = 10;

        public static List<string> BackgroundColours = new List<string> { "495057", "f03e3e", "d6336c", "ae3ec9", "7048e8", "4263eb", "1c7ed6", "1098ad", "0ca678", "37b24d", "74b816", "f59f00", "f76707" };

        //fonts added with RegisterFont, by the name they are asked for in ImageActions.FontFamily
        private static readonly ConcurrentDictionary<string, SKTypeface> RegisteredFonts = new ConcurrentDictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Make a font file (ttf, otf) available to draw text with, without installing it on the
        /// machine. Use the family name given here as <see cref="ImageActions.FontFamily"/>.
        /// </summary>
        public static void RegisterFont(string familyName, byte[] fontData)
        {
            if (string.IsNullOrEmpty(familyName))
            {
                throw new ArgumentNullException(nameof(familyName));
            }

            if (fontData == null)
            {
                throw new ArgumentNullException(nameof(fontData));
            }

            //the typeface reads from the data for as long as it is used, so it is given its own copy
            var typeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
            if (typeface == null)
            {
                throw new ImageProcessorException("The font could not be read.");
            }

            RegisteredFonts[familyName] = typeface;
        }

        /// <summary>
        /// Make a font file (ttf, otf) available to draw text with, without installing it on the machine.
        /// </summary>
        public static void RegisterFont(string familyName, string fontFilePath)
        {
            RegisterFont(familyName, File.ReadAllBytes(fontFilePath));
        }

        /// <summary>
        /// Read the size and format of an image from its header, without decoding the pixels.
        /// </summary>
        public static ImageDetails Identify(byte[] imageData)
        {
            if (imageData == null)
            {
                throw new ArgumentNullException(nameof(imageData));
            }

            using var stream = new MemoryStream(imageData);
            return Identify(stream);
        }

        /// <summary>
        /// Read the size and format of an image from its header, without decoding the pixels.
        /// </summary>
        public static ImageDetails Identify(Stream imageStream)
        {
            if (imageStream == null)
            {
                throw new ArgumentNullException(nameof(imageStream));
            }

            using var codec = SKCodec.Create(imageStream);
            if (codec == null)
            {
                throw new ImageProcessorException("The image could not be decoded. The data is corrupt or the format is not supported.");
            }

            var isOddRotation = IsQuarterTurn(codec.EncodedOrigin);
            return new ImageDetails()
            {
                Width = isOddRotation ? codec.Info.Height : codec.Info.Width,
                Height = isOddRotation ? codec.Info.Width : codec.Info.Height,
                Format = codec.EncodedFormat,
                Orientation = codec.EncodedOrigin,
                FrameCount = Math.Max(1, codec.FrameCount)
            };
        }

        public static async Task<IEnumerable<byte[]>> ProcessImagesAsync(IEnumerable<byte[]> imagesData, ImageActions actions)
        {
            var returnList = new List<byte[]>();

            var activeJobs = new List<Task<byte[]>>();
            foreach (var imageData in imagesData)
            {
                var job = ProcessImageAsync(imageData, actions);
                activeJobs.Add(job);
            }

            await Task.WhenAll(activeJobs).ConfigureAwait(false);

            //by going through it this way we keep the order the same as it came in
            foreach (var completeJob in activeJobs)
            {
                returnList.Add(completeJob.Result);
            }

            return returnList;
        }

        public static async Task<byte[]> ProcessImageAsync(byte[] imageData, ImageActions actions)
        {
            return await ProcessHelperAsync(imageData, actions).ConfigureAwait(false);
        }

        public async static Task<byte[]> ProcessImageAsync(Stream imageStream, ImageActions actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions), "Image actions can not be null");
            }

            if (imageStream == null)
            {
                throw new ArgumentNullException(nameof(imageStream));
            }

            //copy stream into memory asyncronously
            byte[] imageData = null;
            using (var ms = new MemoryStream())
            {
                await imageStream.CopyToAsync(ms).ConfigureAwait(false);
                imageData = ms.ToArray();
            }

            return await ProcessHelperAsync(imageData, actions).ConfigureAwait(false);
        }

        /// <summary>
        /// Process an image from one stream and write the result to another.
        /// </summary>
        public async static Task ProcessImageAsync(Stream imageStream, Stream outputStream, ImageActions actions)
        {
            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            var result = await ProcessImageAsync(imageStream, actions).ConfigureAwait(false);
            await outputStream.WriteAsync(result, 0, result.Length).ConfigureAwait(false);
        }

        public static ImageActionQueryBuilder Process(byte[] imageData)
        {
            return new ImageActionQueryBuilder(imageData);
        }

        public static async Task<byte[]> ProcessVirtualImageAsync(ImageActions actions)
        {
            return await ProcessHelperAsync(null, actions, isVirtual: true).ConfigureAwait(false);
        }

        internal static async Task<byte[]> ProcessHelperAsync(byte[] imageData, ImageActions actions, bool isVirtual = false)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions), "Image actions can not be null");
            }

            //if virtual image color is set then treat whole process as virtual
            if (string.IsNullOrEmpty(actions.ImageColor) == false)
            {
                isVirtual = true;
            }
            else if (actions.ChooseImageColorFromTextValue.HasValue && actions.ChooseImageColorFromTextValue.Value == true)
            {
                isVirtual = true;
            }

            if (isVirtual == false && imageData == null)
            {
                throw new ArgumentNullException(nameof(imageData));
            }

            //placeholder for final image
            SKData encodedImage = null;

            await Task.Run(() =>
            {
                if (isVirtual)
                {
                    SKColor virtualImageColor;

                    //set the text color
                    if (!string.IsNullOrEmpty(actions.ImageColor))
                    {
                        //try hex
                        if (SKColor.TryParse($"#{actions.ImageColor}", out var newColorFromHex))
                        {
                            virtualImageColor = newColorFromHex;
                        }
                        //fall back to black if it fails
                        else
                        {
                            virtualImageColor = SKColors.Black;
                        }
                    }
                    else if (actions.ChooseImageColorFromTextValue.HasValue
                        && actions.ChooseImageColorFromTextValue.Value == true
                        && string.IsNullOrEmpty(actions.Text) == false
                        && BackgroundColours != null
                        && BackgroundColours.Count > 0)
                    {
                        var hashValue = CalculateHash(actions.Text);
                        var backgroundIndex = (int)(hashValue % (UInt64)BackgroundColours.Count);
                        var backgroundColor = BackgroundColours[backgroundIndex];
                        if (backgroundColor != null)
                        {
                            backgroundColor = $"#{backgroundColor}";
                        }
                        if (SKColor.TryParse(backgroundColor, out var newColor))
                        {
                            virtualImageColor = newColor;
                        }
                        else
                        {
                            virtualImageColor = SKColors.Black;
                        }
                    }
                    else
                    {
                        virtualImageColor = SKColors.Black;
                    }

                    //a virtual image is a block of colour with no original size, so it can be any size
                    var virtualActions = new ImageActions(actions)
                    {
                        AllowUpscale = null
                    };

                    using var newBitmap = new SKBitmap(100, 100);
                    using var canvas = new SKCanvas(newBitmap);
                    canvas.Clear(virtualImageColor);
                    using var newImage = SKImage.FromBitmap(newBitmap);
                    encodedImage = EncodeSkiaImage(newImage, virtualActions);
                }
                else
                {
                    using var stream = new MemoryStream(imageData);
                    using var codec = SKCodec.Create(stream);

                    //a null codec means skia does not recognise the data as an image
                    if (codec == null)
                    {
                        throw new ImageProcessorException("The image could not be decoded. The data is corrupt or the format is not supported.");
                    }

                    using var originalBitmap = DecodeBitmap(codec);
                    if (originalBitmap == null)
                    {
                        throw new ImageProcessorException("The image could not be decoded. The data is corrupt or the format is not supported.");
                    }

                    using var newImage = SKImage.FromBitmap(originalBitmap);

                    encodedImage = EncodeSkiaImage(newImage, actions, codec);
                }

                if (encodedImage == null)
                {
                    throw new ImageProcessorException("Error processing image");
                }
            }).ConfigureAwait(false);

            // SKData wraps a native buffer; copy it out and release it right away rather than
            // leaving it for the finalizer (native memory does not pressure the GC).
            byte[] result;
            using (encodedImage)
            {
                result = encodedImage.ToArray();
            }

            //skia does not write metadata, so when it is wanted it is copied across from the original
            if (isVirtual == false && actions.KeepMetadata.HasValue && actions.KeepMetadata.Value == true)
            {
                result = JpegMetadata.CopyMetadata(imageData, result);
            }

            return result;
        }

        /// <summary>
        /// Decode the pixels and convert them to the standard sRGB colour space. A photo saved with
        /// a wider colour profile (for example Display P3 from a phone) would otherwise have its
        /// numbers read as if they were sRGB and come out with washed out colours.
        /// </summary>
        private static SKBitmap DecodeBitmap(SKCodec codec)
        {
            var info = codec.Info;
            if (info.AlphaType == SKAlphaType.Unpremul)
            {
                info.AlphaType = SKAlphaType.Premul;
            }

            //CreateSrgb hands back a shared instance, so it is not disposed here
            var bitmap = SKBitmap.Decode(codec, info.WithColorSpace(SKColorSpace.CreateSrgb()));

            //if the conversion is not possible for this image then decode it the plain way
            return bitmap ?? SKBitmap.Decode(codec);
        }

        public static SKData EncodeSkiaImage(SKImage newImage, ImageActions imageActions, SKCodec codec = null)
        {
            //make sure image was loaded successfully
            if (newImage == null)
            {
                throw new ImageProcessorException("Error processing image");
            }

            if (imageActions == null)
            {
                throw new ArgumentNullException(nameof(imageActions), "Image actions can not be null");
            }

            //work on a copy so the caller's actions are never changed. The same actions are often
            //shared between images (ProcessImagesAsync, default actions) and writing the calculated
            //size and format of one image back would apply it to every image that follows
            var actions = new ImageActions(imageActions);

            //the exif orientation says how the stored pixels need to be turned to be displayed.
            //for a quarter turn the displayed width and height are swapped from what is stored
            var origin = codec != null ? codec.EncodedOrigin : SKEncodedOrigin.TopLeft;
            var isOddRotation = IsQuarterTurn(origin);
            var orientedWidth = isOddRotation ? newImage.Height : newImage.Width;
            var orientedHeight = isOddRotation ? newImage.Width : newImage.Height;

            //the part of the displayed image that is used, the whole image unless a crop was asked for
            var crop = GetCropRect(actions, orientedWidth, orientedHeight);
            var hasCrop = crop.Width != orientedWidth || crop.Height != orientedHeight;

            //a rotation makes the picture take up a different amount of room, work out the box it needs
            var rotation = NormalizeDegrees(actions.Rotate);
            GetRotatedSize(crop.Width, crop.Height, rotation, out var rotatedWidth, out var rotatedHeight);
            var flipHorizontal = actions.FlipHorizontal.HasValue && actions.FlipHorizontal.Value;
            var flipVertical = actions.FlipVertical.HasValue && actions.FlipVertical.Value;

            //from here on the source is the picture after orientation, crop and rotation
            var sourceWidth = Math.Max(1, (int)Math.Round(rotatedWidth));
            var sourceHeight = Math.Max(1, (int)Math.Round(rotatedHeight));

            //a requested size has to be something that can be drawn
            if ((actions.Width.HasValue && actions.Width.Value <= 0)
                || (actions.Height.HasValue && actions.Height.Value <= 0))
            {
                throw new ImageProcessorException("Width and Height must be greater than zero");
            }

            // when only width is set, calculate the height
            if (actions.Width.HasValue &&
                actions.Height.HasValue == false)
            {
                var aspectRatio = (double)sourceHeight / sourceWidth;
                actions.Height = Math.Max(1, (int)(actions.Width.Value * aspectRatio));
            }

            // when only height is set, calculate the width
            else if (actions.Height.HasValue &&
                actions.Width.HasValue == false)
            {
                var aspectRatio = (double)sourceWidth / sourceHeight;
                actions.Width = Math.Max(1, (int)(actions.Height.Value * aspectRatio));
            }

            // when neither width nor height is set, use the original image dimensions
            else if (actions.Width.HasValue == false &&
                actions.Height.HasValue == false)
            {
                actions.Width = sourceWidth;
                actions.Height = sourceHeight;
            }

            //the image actions width and height should always be set from here on, have a sanity check just in case
            if (actions.Width.HasValue == false || actions.Height.HasValue == false)
            {
                throw new ImageProcessorException("Width and Height could not be calculated");
            }

            // if the mode is max then constrain dimensions to requested width and height
            if (actions.Mode == ImageMode.Max)
            {
                var aspectRatio = (double)sourceWidth / sourceHeight;
                if (actions.Width.Value / aspectRatio <= actions.Height.Value)
                {
                    actions.Height = Math.Max(1, (int)(actions.Width.Value / aspectRatio));
                }
                else
                {
                    actions.Width = Math.Max(1, (int)(actions.Height.Value * aspectRatio));
                }
            }

            //when the image is not allowed to be enlarged, bring the requested size down to fit it
            if (actions.AllowUpscale.HasValue && actions.AllowUpscale.Value == false)
            {
                PreventUpscale(actions, sourceWidth, sourceHeight);
            }

            var width = actions.Width.Value;
            var height = actions.Height.Value;

            // Record the drawing commands at the new dimensions. Every SkiaSharp object below holds
            // native memory the GC cannot see, so each one is disposed deterministically; the
            // previous full-size scratch SKBitmap allocated here was never drawn to and is gone.
            using var recorder = new SKPictureRecorder();
            var rect = GetSKRectByWidthAndHeight(0, 0, width, height);
            var canvas = recorder.BeginRecording(rect);

            //the background goes down before the shape is cut out, so it also fills the corners
            //outside of a circle or rounded rectangle
            if (!string.IsNullOrEmpty(actions.BackgroundColor)
                && SKColor.TryParse($"#{actions.BackgroundColor}", out var backgroundColor))
            {
                canvas.Clear(backgroundColor);
            }

            //if no shape specified, but a corner radius is then set shape to rounded rectangle
            if (actions.Shape.HasValue == false && actions.CornerRadius.HasValue)
            {
                actions.Shape = ImageShape.RoundedRectangle;
            }

            //if a shape is specified then clip the canvas to that shape
            if (actions.Shape.HasValue)
            {
                if (actions.Shape == ImageShape.Circle)
                {
                    var radius = Math.Min(width, height) / 2;
                    var centerX = width / 2;
                    var centerY = height / 2;

                    using var a = new SKPath();
                    a.AddCircle(centerX, centerY, radius);
                    canvas.ClipPath(a, antialias: true);
                }
                else if (actions.Shape == ImageShape.Ellipse)
                {
                    using var a = new SKPath();
                    var r = GetSKRectByWidthAndHeight(0, 0, width, height);
                    a.AddOval(r);
                    canvas.ClipPath(a, antialias: true);
                }
                else if (actions.Shape == ImageShape.RoundedRectangle)
                {
                    using var a = new SKPath();
                    var r = GetSKRectByWidthAndHeight(0, 0, width, height);
                    if (actions.CornerRadius.HasValue)
                    {

                        a.AddRoundRect(r, actions.CornerRadius.Value, actions.CornerRadius.Value);
                    }
                    else
                    {
                        a.AddRoundRect(r, CornerRadius, CornerRadius);
                    }
                    canvas.ClipPath(a, antialias: true);
                }
            }

            using var imagePaint = new SKPaint
            {
                FilterQuality = GetFilterQuality(actions.Resampler),
            };

            //grayscale, sepia, brightness and the other colour changes are combined into one filter
            var colorMatrix = GetColorMatrix(actions);
            using var colorFilter = colorMatrix != null ? SKColorFilter.CreateColorMatrix(colorMatrix) : null;
            if (colorFilter != null)
            {
                imagePaint.ColorFilter = colorFilter;
            }

            //work out where on the canvas the image goes, as it will be displayed (after exif orientation)
            GetAnchorPosition(actions.Anchor ?? ImageAnchor.Center, out var anchorX, out var anchorY);
            SKRect destinationRect;
            switch (actions.Mode)
            {
                //depend on canvas size
                default:
                case null:
                case ImageMode.Stretch:
                case ImageMode.Max:
                    destinationRect = GetSKRectByWidthAndHeight(0, 0, width, height);
                    break;

                //fit within canvas
                case ImageMode.Fit:
                    destinationRect = GetScaledRect(sourceWidth, sourceHeight, width, height, false, anchorX, anchorY, null, null);
                    break;

                //zoom in and fill canvas while maintaing aspect ratio
                case ImageMode.Zoom:
                    destinationRect = GetScaledRect(sourceWidth, sourceHeight, width, height, true, anchorX, anchorY, actions.FocalPointX, actions.FocalPointY);
                    break;
            }

            //draws the source picture (oriented, cropped, flipped and rotated) into a rectangle
            void DrawSource(SKCanvas target, SKRect destination)
            {
                //nothing extra to do for most images, so draw it directly
                if (hasCrop == false && rotation == 0 && flipHorizontal == false && flipVertical == false)
                {
                    DrawImageWithOrientation(target, newImage, destination, origin, imagePaint);
                    return;
                }

                //otherwise work outwards from the middle of the destination. These are applied
                //to the picture in reverse order: crop, then flip, then rotate, then scale into place
                target.Save();
                target.Translate(destination.MidX, destination.MidY);
                target.Scale(destination.Width / (float)rotatedWidth, destination.Height / (float)rotatedHeight);
                if (rotation != 0)
                {
                    target.RotateDegrees((float)rotation);
                }
                if (flipHorizontal || flipVertical)
                {
                    target.Scale(flipHorizontal ? -1 : 1, flipVertical ? -1 : 1);
                }
                if (hasCrop)
                {
                    target.ClipRect(GetSKRectByWidthAndHeight(-crop.Width / 2f, -crop.Height / 2f, crop.Width, crop.Height), antialias: true);
                }

                //place the whole image so that the middle of the crop is at the middle
                var imageRect = GetSKRectByWidthAndHeight(-(crop.Left + (crop.Width / 2f)), -(crop.Top + (crop.Height / 2f)), orientedWidth, orientedHeight);
                DrawImageWithOrientation(target, newImage, imageRect, origin, imagePaint);
                target.Restore();
            }

            //draws the picture as it will appear, pixelated when that was asked for
            void DrawPicture(SKCanvas target)
            {
                if (actions.Pixelate.HasValue && actions.Pixelate.Value > 1)
                {
                    //draw the picture small, then blow that up without any smoothing so each pixel becomes a block
                    var blockSize = actions.Pixelate.Value;
                    var smallWidth = Math.Max(1, (int)Math.Ceiling(width / (double)blockSize));
                    var smallHeight = Math.Max(1, (int)Math.Ceiling(height / (double)blockSize));
                    using var smallSurface = SKSurface.Create(new SKImageInfo(smallWidth, smallHeight, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
                    if (smallSurface == null)
                    {
                        throw new ImageProcessorException("Error processing image");
                    }

                    smallSurface.Canvas.Clear(SKColors.Transparent);
                    smallSurface.Canvas.Scale(1f / blockSize, 1f / blockSize);
                    DrawSource(smallSurface.Canvas, destinationRect);

                    using var smallImage = smallSurface.Snapshot();
                    using var blockPaint = new SKPaint
                    {
                        FilterQuality = SKFilterQuality.None
                    };
                    target.DrawImage(smallImage, GetSKRectByWidthAndHeight(0, 0, smallWidth * blockSize, smallHeight * blockSize), paint: blockPaint);
                }
                else
                {
                    DrawSource(target, destinationRect);
                }
            }

            //write to the canvas, using the exif data to turn the image the right way up
            using var blurFilter = (actions.Blur.HasValue && actions.Blur.Value > 0)
                ? SKImageFilter.CreateBlur((float)actions.Blur.Value, (float)actions.Blur.Value)
                : null;
            using var sharpenFilter = (actions.Sharpen.HasValue && actions.Sharpen.Value > 0)
                ? CreateSharpenFilter((float)actions.Sharpen.Value, blurFilter)
                : null;
            var effectFilter = sharpenFilter ?? blurFilter;
            if (effectFilter != null)
            {
                //blur and sharpen work on the finished pixels, so the picture is drawn to its own
                //surface first. It is then painted through the filter as a pattern that repeats its
                //edge pixels, onto an area larger than the canvas. Without that the filter would
                //mix in the nothing that is beyond the edge and the border would fade out
                using var flatSurface = SKSurface.Create(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
                if (flatSurface == null)
                {
                    throw new ImageProcessorException("Error processing image");
                }

                flatSurface.Canvas.Clear(SKColors.Transparent);
                DrawPicture(flatSurface.Canvas);

                using var flatImage = flatSurface.Snapshot();
                using var edgeRepeatingShader = flatImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
                using var effectPaint = new SKPaint
                {
                    Shader = edgeRepeatingShader,
                    ImageFilter = effectFilter
                };

                var reach = (float)Math.Ceiling((actions.Blur ?? 0) * 3) + 2;
                canvas.DrawRect(new SKRect(-reach, -reach, width + reach, height + reach), effectPaint);
            }
            else
            {
                DrawPicture(canvas);
            }

            //darken towards the corners
            if (actions.Vignette.HasValue && actions.Vignette.Value > 0)
            {
                var strength = (byte)(Math.Min(1, actions.Vignette.Value) * 255);
                var center = new SKPoint(width / 2f, height / 2f);
                var cornerDistance = (float)Math.Sqrt((width * width) + (height * height)) / 2f;
                var colors = new[] { SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(0), SKColors.Black.WithAlpha(strength) };
                var positions = new[] { 0f, 0.45f, 1f };
                using var vignetteShader = SKShader.CreateRadialGradient(center, cornerDistance, colors, positions, SKShaderTileMode.Clamp);
                using var vignettePaint = new SKPaint
                {
                    Shader = vignetteShader
                };
                canvas.DrawRect(rect, vignettePaint);
            }

            //if there is text to draw then do it
            if (!string.IsNullOrEmpty(actions.Text))
            {
                DrawText(canvas, actions, width, height);
            }

            //the watermark goes on last so that it is on top of everything
            if (actions.WatermarkImage != null && actions.WatermarkImage.Length > 0)
            {
                DrawWatermark(canvas, actions, width, height);
            }

            //set export format
            var exportImageType = SKEncodedImageFormat.Png;
            if (actions.Format.HasValue) //user specified overrides default
            {
                exportImageType = actions.Format.Value;
            }
            else if (codec != null) //otherwise export as what it started as
            {
                exportImageType = codec.EncodedFormat;
            }

            //set encoding quality
            var quality = 100;
            if (exportImageType == SKEncodedImageFormat.Jpeg)
            {
                quality = JpegQuality;
            }
            else if (exportImageType == SKEncodedImageFormat.Gif)
            {
                quality = GifQuality;
            }

            //a quality on the request overrides the defaults
            if (actions.Quality.HasValue)
            {
                quality = Math.Max(1, Math.Min(100, actions.Quality.Value));
            }

            using var picture = recorder.EndRecording();
            var ouputSize = new SKSizeI(width, height);
            using var outputImage = SKImage.FromPicture(picture, ouputSize);
            if (outputImage == null)
            {
                throw new ImageProcessorException("Error processing image");
            }

            var encodedImage = outputImage.Encode(exportImageType, quality);

            //skia can read more formats than it can write (it writes jpeg, png and webp), and returns
            //nothing for the rest. Fall back to png so a gif or bmp can still be resized
            if (encodedImage == null && exportImageType != SKEncodedImageFormat.Png)
            {
                encodedImage = outputImage.Encode(SKEncodedImageFormat.Png, 100);
            }

            if (encodedImage == null)
            {
                throw new ImageProcessorException("Error encoding image");
            }

            return encodedImage;
        }

        public static SKRect GetSKRectByWidthAndHeight(float left, float top, float width, float height)
        {
            return new SKRect(left, top, width + left, top + height);
        }

        private static bool IsQuarterTurn(SKEncodedOrigin origin)
        {
            return origin == SKEncodedOrigin.LeftTop
                || origin == SKEncodedOrigin.RightTop
                || origin == SKEncodedOrigin.RightBottom
                || origin == SKEncodedOrigin.LeftBottom;
        }

        private static SKFilterQuality GetFilterQuality(ImageResampler? resampler)
        {
            switch (resampler)
            {
                case ImageResampler.NearestNeighbor:
                    return SKFilterQuality.None;

                case ImageResampler.Bilinear:
                    return SKFilterQuality.Low;

                case ImageResampler.Mipmap:
                    return SKFilterQuality.Medium;

                default:
                case null:
                case ImageResampler.Bicubic:
                    return SKFilterQuality.High;
            }
        }

        /// <summary>
        /// The part of the displayed image to use. A crop that reaches outside of the image is
        /// brought back inside of it.
        /// </summary>
        private static SKRectI GetCropRect(ImageActions actions, int imageWidth, int imageHeight)
        {
            if (actions.HasCrop() == false)
            {
                return new SKRectI(0, 0, imageWidth, imageHeight);
            }

            if (actions.CropWidth.Value <= 0 || actions.CropHeight.Value <= 0)
            {
                throw new ImageProcessorException("Crop width and height must be greater than zero");
            }

            var left = Math.Max(0, Math.Min(actions.CropX.Value, imageWidth - 1));
            var top = Math.Max(0, Math.Min(actions.CropY.Value, imageHeight - 1));
            var cropWidth = Math.Min(actions.CropWidth.Value, imageWidth - left);
            var cropHeight = Math.Min(actions.CropHeight.Value, imageHeight - top);
            return new SKRectI(left, top, left + cropWidth, top + cropHeight);
        }

        /// <summary>
        /// Bring any angle into the range 0 to 360.
        /// </summary>
        private static double NormalizeDegrees(double? degrees)
        {
            if (degrees.HasValue == false || double.IsNaN(degrees.Value) || double.IsInfinity(degrees.Value))
            {
                return 0;
            }

            var normalized = degrees.Value % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized;
        }

        /// <summary>
        /// The size of the box that a rectangle fills once it has been rotated.
        /// </summary>
        private static void GetRotatedSize(double width, double height, double degrees, out double rotatedWidth, out double rotatedHeight)
        {
            //quarter turns are exact, working them out with sin and cos would be a hair off
            if (degrees % 180 == 0)
            {
                rotatedWidth = width;
                rotatedHeight = height;
            }
            else if (degrees % 90 == 0)
            {
                rotatedWidth = height;
                rotatedHeight = width;
            }
            else
            {
                var radians = degrees * Math.PI / 180;
                var sin = Math.Abs(Math.Sin(radians));
                var cos = Math.Abs(Math.Cos(radians));
                rotatedWidth = (width * cos) + (height * sin);
                rotatedHeight = (width * sin) + (height * cos);
            }
        }

        /// <summary>
        /// Reduce the requested size so that the source is never drawn larger than it is.
        /// </summary>
        private static void PreventUpscale(ImageActions actions, int sourceWidth, int sourceHeight)
        {
            var width = actions.Width.Value;
            var height = actions.Height.Value;

            switch (actions.Mode)
            {
                //each side is stretched on its own, so each side is limited on its own
                default:
                case null:
                case ImageMode.Stretch:
                    actions.Width = Math.Min(width, sourceWidth);
                    actions.Height = Math.Min(height, sourceHeight);
                    break;

                //max has already been made the same shape as the source
                case ImageMode.Max:
                    if (width > sourceWidth || height > sourceHeight)
                    {
                        actions.Width = sourceWidth;
                        actions.Height = sourceHeight;
                    }
                    break;

                //keep the shape that was asked for and shrink it until the source is at its own size
                case ImageMode.Fit:
                case ImageMode.Zoom:
                    var widthScale = (double)width / sourceWidth;
                    var heightScale = (double)height / sourceHeight;
                    var scale = actions.Mode == ImageMode.Zoom
                        ? Math.Max(widthScale, heightScale)
                        : Math.Min(widthScale, heightScale);
                    if (scale > 1)
                    {
                        actions.Width = Math.Max(1, (int)Math.Round(width / scale));
                        actions.Height = Math.Max(1, (int)Math.Round(height / scale));
                    }
                    break;
            }
        }

        /// <summary>
        /// Turn an anchor into how far across (0 left, 1 right) and how far down (0 top, 1 bottom) it is.
        /// </summary>
        private static void GetAnchorPosition(ImageAnchor anchor, out float x, out float y)
        {
            x = 0.5f;
            y = 0.5f;

            if (anchor == ImageAnchor.Left || anchor == ImageAnchor.TopLeft || anchor == ImageAnchor.BottomLeft)
            {
                x = 0;
            }
            else if (anchor == ImageAnchor.Right || anchor == ImageAnchor.TopRight || anchor == ImageAnchor.BottomRight)
            {
                x = 1;
            }

            if (anchor == ImageAnchor.Top || anchor == ImageAnchor.TopLeft || anchor == ImageAnchor.TopRight)
            {
                y = 0;
            }
            else if (anchor == ImageAnchor.Bottom || anchor == ImageAnchor.BottomLeft || anchor == ImageAnchor.BottomRight)
            {
                y = 1;
            }
        }

        /// <summary>
        /// Scale the source to the canvas while keeping its aspect ratio.
        /// When filling, the image covers the whole canvas and overflows on one axis (zoom).
        /// Otherwise the whole image is visible and one axis has empty space (fit).
        /// The anchor decides which side the overflow or the empty space is on, and a focal point
        /// (zoom only) moves the image so that point is as close to the middle as it can be.
        /// </summary>
        private static SKRect GetScaledRect(int sourceWidth, int sourceHeight, int width, int height, bool fillCanvas, float anchorX, float anchorY, double? focalPointX, double? focalPointY)
        {
            var widthScale = (double)width / sourceWidth;
            var heightScale = (double)height / sourceHeight;
            var useWidthScale = fillCanvas ? widthScale >= heightScale : widthScale <= heightScale;

            //the side that sets the scale is exactly the canvas size, calculating it from the scale
            //instead can land a fraction short and leave a transparent line down the edge
            float scaledWidth;
            float scaledHeight;
            if (useWidthScale)
            {
                scaledWidth = width;
                scaledHeight = Math.Max(1, (float)Math.Round(sourceHeight * widthScale));
            }
            else
            {
                scaledHeight = height;
                scaledWidth = Math.Max(1, (float)Math.Round(sourceWidth * heightScale));
            }

            var offsetX = (width - scaledWidth) * anchorX;
            var offsetY = (height - scaledHeight) * anchorY;

            if (fillCanvas && focalPointX.HasValue)
            {
                var focalX = (float)Math.Max(0, Math.Min(1, focalPointX.Value));
                offsetX = Math.Max(width - scaledWidth, Math.Min(0, (width / 2f) - (focalX * scaledWidth)));
            }

            if (fillCanvas && focalPointY.HasValue)
            {
                var focalY = (float)Math.Max(0, Math.Min(1, focalPointY.Value));
                offsetY = Math.Max(height - scaledHeight, Math.Min(0, (height / 2f) - (focalY * scaledHeight)));
            }

            return GetSKRectByWidthAndHeight(offsetX, offsetY, scaledWidth, scaledHeight);
        }

        /// <summary>
        /// Draw the stored image into the destination rectangle, turned and mirrored according to its
        /// exif orientation. The canvas is restored afterwards so nothing drawn later is affected.
        /// </summary>
        private static void DrawImageWithOrientation(SKCanvas canvas, SKImage image, SKRect destinationRect, SKEncodedOrigin origin, SKPaint paint)
        {
            var w = destinationRect.Width;
            var h = destinationRect.Height;

            //maps the stored pixels onto the displayed image, w and h being the displayed size
            SKMatrix matrix;
            var isOddRotation = false;
            switch (origin)
            {
                default:
                case SKEncodedOrigin.TopLeft:
                    matrix = new SKMatrix(1, 0, 0, 0, 1, 0, 0, 0, 1);
                    break;

                //mirrored left to right
                case SKEncodedOrigin.TopRight:
                    matrix = new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1);
                    break;

                //rotated 180
                case SKEncodedOrigin.BottomRight:
                    matrix = new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1);
                    break;

                //mirrored top to bottom
                case SKEncodedOrigin.BottomLeft:
                    matrix = new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1);
                    break;

                //mirrored across the top left to bottom right diagonal
                case SKEncodedOrigin.LeftTop:
                    matrix = new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1);
                    isOddRotation = true;
                    break;

                //rotated 90
                case SKEncodedOrigin.RightTop:
                    matrix = new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1);
                    isOddRotation = true;
                    break;

                //mirrored across the top right to bottom left diagonal
                case SKEncodedOrigin.RightBottom:
                    matrix = new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1);
                    isOddRotation = true;
                    break;

                //rotated 270
                case SKEncodedOrigin.LeftBottom:
                    matrix = new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1);
                    isOddRotation = true;
                    break;
            }

            //the stored image is drawn on its side for a quarter turn, so its width and height swap
            var storedRect = isOddRotation
                ? GetSKRectByWidthAndHeight(0, 0, h, w)
                : GetSKRectByWidthAndHeight(0, 0, w, h);

            canvas.Save();
            canvas.Translate(destinationRect.Left, destinationRect.Top);
            canvas.Concat(ref matrix);
            canvas.DrawImage(image, storedRect, paint: paint);
            canvas.Restore();
        }

        /// <summary>
        /// Build the colour matrix for every colour change that was asked for, or null when there
        /// are none. Each change is its own matrix and they are multiplied together into one.
        /// A matrix is 4 rows (red, green, blue, alpha) of 5 values: how much of the red, green,
        /// blue and alpha of the original to use, then an amount to add (1 is full).
        /// </summary>
        private static float[] GetColorMatrix(ImageActions actions)
        {
            float[] matrix = null;

            if (actions.Saturation.HasValue)
            {
                var s = (float)Math.Max(0, actions.Saturation.Value);
                var r = 0.213f * (1 - s);
                var g = 0.715f * (1 - s);
                var b = 0.072f * (1 - s);
                matrix = CombineColorMatrix(matrix, new float[]
                {
                    r + s, g,     b,     0, 0,
                    r,     g + s, b,     0, 0,
                    r,     g,     b + s, 0, 0,
                    0,     0,     0,     1, 0
                });
            }

            if (actions.IsGrayscale.HasValue && actions.IsGrayscale.Value == true)
            {
                matrix = CombineColorMatrix(matrix, new float[]
                {
                    0.3f, 0.59f, 0.11f, 0, 0,
                    0.3f, 0.59f, 0.11f, 0, 0,
                    0.3f, 0.59f, 0.11f, 0, 0,
                    0,    0,    0,    1, 0
                });
            }

            if (actions.IsSepia.HasValue && actions.IsSepia.Value == true)
            {
                matrix = CombineColorMatrix(matrix, new float[]
                {
                    0.393f, 0.769f, 0.189f, 0, 0,
                    0.349f, 0.686f, 0.168f, 0, 0,
                    0.272f, 0.534f, 0.131f, 0, 0,
                    0,      0,      0,      1, 0
                });
            }

            if (actions.Brightness.HasValue)
            {
                var b = (float)Math.Max(0, actions.Brightness.Value);
                matrix = CombineColorMatrix(matrix, new float[]
                {
                    b, 0, 0, 0, 0,
                    0, b, 0, 0, 0,
                    0, 0, b, 0, 0,
                    0, 0, 0, 1, 0
                });
            }

            if (actions.Contrast.HasValue)
            {
                //scale away from mid gray
                var c = (float)Math.Max(0, actions.Contrast.Value);
                var t = (1 - c) / 2f;
                matrix = CombineColorMatrix(matrix, new float[]
                {
                    c, 0, 0, 0, t,
                    0, c, 0, 0, t,
                    0, 0, c, 0, t,
                    0, 0, 0, 1, 0
                });
            }

            if (actions.IsInverted.HasValue && actions.IsInverted.Value == true)
            {
                matrix = CombineColorMatrix(matrix, new float[]
                {
                    -1, 0,  0,  0, 1,
                    0,  -1, 0,  0, 1,
                    0,  0,  -1, 0, 1,
                    0,  0,  0,  1, 0
                });
            }

            return matrix;
        }

        /// <summary>
        /// One matrix that has the same effect as applying the first and then the second.
        /// </summary>
        private static float[] CombineColorMatrix(float[] first, float[] second)
        {
            if (first == null)
            {
                return second;
            }

            var result = new float[20];
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 5; column++)
                {
                    float value = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        value += second[(row * 5) + k] * first[(k * 5) + column];
                    }

                    //the amount to add from the second matrix is not scaled by the first
                    if (column == 4)
                    {
                        value += second[(row * 5) + 4];
                    }

                    result[(row * 5) + column] = value;
                }
            }

            return result;
        }

        private static SKImageFilter CreateSharpenFilter(float amount, SKImageFilter input)
        {
            //each pixel is pushed away from the average of its four neighbours
            var kernel = new float[]
            {
                0,       -amount,          0,
                -amount, 1 + (4 * amount), -amount,
                0,       -amount,          0
            };

            return SKImageFilter.CreateMatrixConvolution(new SKSizeI(3, 3), kernel, 1f, 0f, new SKPointI(1, 1), SKShaderTileMode.Clamp, true, input);
        }

        private static void DrawText(SKCanvas canvas, ImageActions actions, int width, int height)
        {
            var textToPrint = actions.Text;

            if (actions.AsInitials.HasValue && actions.AsInitials.Value == true)
            {
                textToPrint = GetInitials(actions.Text);
            }

            //a registered font is shared and kept, a system font is looked up for this image and released
            var fontFamily = string.IsNullOrEmpty(actions.FontFamily) ? "Arial" : actions.FontFamily;
            RegisteredFonts.TryGetValue(fontFamily, out var registeredTypeface);
            using var systemTypeface = registeredTypeface == null
                ? SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Black, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                : null;

            var myFontSize = (int)(height * 0.85);

            // Set up paint for text
            using var paint = new SKPaint
            {
                Typeface = registeredTypeface ?? systemTypeface,
                IsAntialias = true,
                TextSize = myFontSize,
                Color = SKColors.Black, // Default text color
                TextAlign = SKTextAlign.Center
            };

            var lines = new List<string> { textToPrint };
            if (actions.FontSize.HasValue && actions.FontSize.Value > 0)
            {
                //a size was asked for so use it as it is, and wrap onto more lines if that was asked for too
                paint.TextSize = actions.FontSize.Value;
                if (actions.TextWrap.HasValue && actions.TextWrap.Value == true)
                {
                    lines = WrapText(textToPrint, paint, width * 0.9f);
                }
            }
            else
            {
                //calculate string size where height is image height to get scale of text
                var textSize = paint.MeasureText(textToPrint);

                //specify the max width that is wanted
                var maxWidth = width * 0.75;

                // it needs to fit in the image, so if it is too narrow then we need to shrink down the font
                if (textSize > maxWidth)
                {
                    myFontSize = (int)((maxWidth / textSize) * myFontSize);
                }

                // Calculate maximum font size to fit text within the image
                paint.TextSize = myFontSize;
            }

            //set the text color
            if (!string.IsNullOrEmpty(actions.TextColor))
            {
                //try hex
                if (SKColor.TryParse($"#{actions.TextColor}", out var newColorFromHex))
                {
                    paint.Color = newColorFromHex;
                }
                //fall back to white if it fails
                else
                {
                    paint.Color = SKColors.White;
                }
            }
            else
            {
                paint.Color = SKColors.White;
            }

            // Calculate text position, text in the middle goes edge to edge and anything else is kept off the edge
            var anchor = actions.TextAnchor ?? ImageAnchor.Center;
            GetAnchorPosition(anchor, out var anchorX, out var anchorY);
            var margin = anchor == ImageAnchor.Center ? 0 : Math.Max(2f, Math.Min(width, height) * 0.04f);

            float x;
            if (anchorX == 0)
            {
                paint.TextAlign = SKTextAlign.Left;
                x = margin;
            }
            else if (anchorX == 1)
            {
                paint.TextAlign = SKTextAlign.Right;
                x = width - margin;
            }
            else
            {
                paint.TextAlign = SKTextAlign.Center;
                x = width / 2f;
            }

            var metrics = paint.FontMetrics;
            var lineHeight = metrics.Descent - metrics.Ascent;
            var lineSpacing = lineHeight + metrics.Leading;
            var blockHeight = lineHeight + ((lines.Count - 1) * lineSpacing);

            float top;
            if (anchorY == 0)
            {
                top = margin;
            }
            else if (anchorY == 1)
            {
                top = height - margin - blockHeight;
            }
            else
            {
                top = (height - blockHeight) / 2f;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                //text is drawn from its baseline, which is the ascent below the top of the line
                var y = top - metrics.Ascent + (i * lineSpacing);
                canvas.DrawText(lines[i], x, y, paint);
            }
        }

        /// <summary>
        /// Break text into lines that are no wider than the max width, breaking between words.
        /// </summary>
        private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = string.Empty;
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : $"{line} {word}";
                    if (line.Length > 0 && paint.MeasureText(candidate) > maxWidth)
                    {
                        lines.Add(line);
                        line = word;
                    }
                    else
                    {
                        line = candidate;
                    }
                }

                lines.Add(line);
            }

            return lines;
        }

        private static void DrawWatermark(SKCanvas canvas, ImageActions actions, int width, int height)
        {
            using var watermark = SKImage.FromEncodedData(actions.WatermarkImage);
            if (watermark == null)
            {
                throw new ImageProcessorException("The watermark image could not be decoded.");
            }

            //the watermark is sized as a share of the result, and never taller than the result
            var scale = actions.WatermarkScale.HasValue ? Math.Max(0.01, Math.Min(1, actions.WatermarkScale.Value)) : 0.25;
            var margin = actions.WatermarkMargin.HasValue
                ? Math.Max(0, actions.WatermarkMargin.Value)
                : (float)Math.Round(Math.Min(width, height) * 0.03);

            var watermarkWidth = (float)(width * scale);
            var watermarkHeight = watermarkWidth * watermark.Height / watermark.Width;
            var maxHeight = Math.Max(1, height - (2 * margin));
            if (watermarkHeight > maxHeight)
            {
                watermarkWidth = watermarkWidth * maxHeight / watermarkHeight;
                watermarkHeight = maxHeight;
            }

            GetAnchorPosition(actions.WatermarkAnchor ?? ImageAnchor.BottomRight, out var anchorX, out var anchorY);
            var x = margin + ((width - (2 * margin) - watermarkWidth) * anchorX);
            var y = margin + ((height - (2 * margin) - watermarkHeight) * anchorY);

            var opacity = actions.WatermarkOpacity.HasValue ? Math.Max(0, Math.Min(1, actions.WatermarkOpacity.Value)) : 1;
            using var paint = new SKPaint
            {
                FilterQuality = SKFilterQuality.High,
                Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255))
            };

            canvas.DrawImage(watermark, GetSKRectByWidthAndHeight(x, y, watermarkWidth, watermarkHeight), paint: paint);
        }

        public static UInt64 CalculateHash(string read)
        {
            UInt64 hashedValue = 3074457345618258791ul;
            for (int i = 0; i < read.Length; i++)
            {
                hashedValue += read[i];
                hashedValue *= 3074457345618258799ul;
            }
            return hashedValue;
        }

        public static string GetInitials(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            //when the words are already separated use them as they are, so DUSTIN GAMESTER is DG
            var words = SplitIntoWords(input, @"[_\s]+");

            //otherwise fall back to splitting a single word on its capital letters, so DustinGamester is DG
            if (words.Count < 2)
            {
                words = SplitIntoWords(input, @"(?<!^)(?=[A-Z])|[_\s]+");
            }

            // Extract the first letter of each word
            var initials = string.Empty;
            foreach (var word in words)
            {
                initials += word[0];
                if (initials.Length == 2)
                {
                    break;
                }
            }

            return initials.ToUpper();
        }

        private static List<string> SplitIntoWords(string input, string pattern)
        {
            var words = new List<string>();
            foreach (var word in Regex.Split(input, pattern))
            {
                if (!string.IsNullOrEmpty(word))
                {
                    words.Add(word);
                }
            }
            return words;
        }
    }
}
