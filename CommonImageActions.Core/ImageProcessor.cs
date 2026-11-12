using Microsoft.Maui.Graphics;
using SkiaSharp;
using System;
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

                    using var newBitmap = new SKBitmap(100, 100);
                    using var canvas = new SKCanvas(newBitmap);
                    canvas.Clear(virtualImageColor);
                    using var newImage = SKImage.FromBitmap(newBitmap);
                    encodedImage = EncodeSkiaImage(newImage, actions);
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

                    using var originalBitmap = SKBitmap.Decode(codec);
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
            using (encodedImage)
            {
                return encodedImage.ToArray();
            }
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
            var isOddRotation = origin == SKEncodedOrigin.LeftTop
                || origin == SKEncodedOrigin.RightTop
                || origin == SKEncodedOrigin.RightBottom
                || origin == SKEncodedOrigin.LeftBottom;
            var sourceWidth = isOddRotation ? newImage.Height : newImage.Width;
            var sourceHeight = isOddRotation ? newImage.Width : newImage.Height;

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

            var width = actions.Width.Value;
            var height = actions.Height.Value;

            // Record the drawing commands at the new dimensions. Every SkiaSharp object below holds
            // native memory the GC cannot see, so each one is disposed deterministically; the
            // previous full-size scratch SKBitmap allocated here was never drawn to and is gone.
            using var recorder = new SKPictureRecorder();
            var rect = GetSKRectByWidthAndHeight(0, 0, width, height);
            var canvas = recorder.BeginRecording(rect);

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
                FilterQuality = SKFilterQuality.High,
            };

            using var grayscaleFilter = (actions.IsGrayscale.HasValue && actions.IsGrayscale == true)
                ? SKColorFilter.CreateColorMatrix(new float[]
                {
                    0.3f, 0.59f, 0.11f, 0, 0,
                    0.3f, 0.59f, 0.11f, 0, 0,
                    0.3f, 0.59f, 0.11f, 0, 0,
                    0,    0,    0,    1, 0
                })
                : null;

            if (grayscaleFilter != null)
            {
                imagePaint.ColorFilter = grayscaleFilter;
            }

            //work out where on the canvas the image goes, as it will be displayed (after exif orientation)
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
                    destinationRect = GetScaledRect(sourceWidth, sourceHeight, width, height, fillCanvas: false);
                    break;

                //zoom in and fill canvas while maintaing aspect ratio
                case ImageMode.Zoom:
                    destinationRect = GetScaledRect(sourceWidth, sourceHeight, width, height, fillCanvas: true);
                    break;
            }

            //write to the canvas, using the exif data to turn the image the right way up
            DrawImageWithOrientation(canvas, newImage, destinationRect, origin, imagePaint);

            //if there is text to draw then do it
            if (!string.IsNullOrEmpty(actions.Text))
            {
                var textToPrint = actions.Text;

                if (actions.AsInitials.HasValue && actions.AsInitials.Value == true)
                {
                    textToPrint = GetInitials(actions.Text);
                }

                using var myTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Black, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                var myFontSize = (int)(height * 0.85);

                // Set up paint for text
                using var paint = new SKPaint
                {
                    Typeface = myTypeface,
                    IsAntialias = true,
                    TextSize = myFontSize,
                    Color = SKColors.Black, // Default text color
                    TextAlign = SKTextAlign.Center
                };

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

                // Calculate text position
                var x = width / 2f;
                var y = (height / 2f) - ((paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2);

                canvas.DrawText(textToPrint, x, y, paint);
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

        /// <summary>
        /// Scale the source to the canvas while keeping its aspect ratio, centered on the canvas.
        /// When filling, the image covers the whole canvas and overflows on one axis (zoom).
        /// Otherwise the whole image is visible and one axis has empty space (fit).
        /// </summary>
        private static SKRect GetScaledRect(int sourceWidth, int sourceHeight, int width, int height, bool fillCanvas)
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

            var offsetX = (width - scaledWidth) / 2f;
            var offsetY = (height - scaledHeight) / 2f;
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
