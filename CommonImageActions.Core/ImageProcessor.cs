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

            await Task.WhenAll(activeJobs);

            //by going through it this way we keep the order the same as it came in
            foreach (var completeJob in activeJobs)
            {
                returnList.Add(completeJob.Result);
            }

            return returnList;
        }

        public static async Task<byte[]> ProcessImageAsync(byte[] imageData, ImageActions actions)
        {
            return await ProcessHelperAsync(imageData, actions);
        }

        public async static Task<byte[]> ProcessImageAsync(Stream imageStream, ImageActions actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException("Image actions can not be null");
            }

            //copy stream into memory asyncronously
            byte[] imageData = null;
            using (var ms = new MemoryStream())
            {
                await imageStream.CopyToAsync(ms);
                imageData = ms.ToArray();
            }

            return await ProcessHelperAsync(imageData, actions);
        }

        public static ImageActionQueryBuilder Process(byte[] imageData)
        {
            return new ImageActionQueryBuilder(imageData);
        }

        public static async Task<byte[]> ProcessVirtualImageAsync(ImageActions actions)
        {
            return await ProcessHelperAsync(null, actions, isVirtual: true);
        }

        internal static async Task<byte[]> ProcessHelperAsync(byte[] imageData, ImageActions actions, bool isVirtual = false)
        {
            //placeholder for final image
            SKData encodedImage = null;

            await Task.Run(() =>
            {
                //if virtual image color is set then treat whole process as virtual
                if (string.IsNullOrEmpty(actions.ImageColor) == false)
                {
                    isVirtual = true;
                }
                else if (actions.ChooseImageColorFromTextValue.HasValue && actions.ChooseImageColorFromTextValue.Value == true)
                {
                    isVirtual = true;
                }

                if (isVirtual)
                {
                    SKColor virtualImageColor;

                    //set the text color
                    if (!string.IsNullOrEmpty(actions.ImageColor))
                    {
                        //try regular
                        if (SKColor.TryParse(actions.ImageColor, out var newColor))
                        {
                            virtualImageColor = newColor;
                        }
                        //try hex
                        else if (SKColor.TryParse($"#{actions.ImageColor}", out var newColorFromHex))
                        {
                            virtualImageColor = newColorFromHex;
                        }
                        //fall back to white if they both fail
                        else
                        {
                            virtualImageColor = SKColors.Black;
                        }
                    }
                    else if (actions.ChooseImageColorFromTextValue.HasValue
                        && actions.ChooseImageColorFromTextValue.Value == true
                        && string.IsNullOrEmpty(actions.Text) == false)
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

                    var newBitmap = new SKBitmap(100, 100);
                    using var canvas = new SKCanvas(newBitmap);
                    canvas.Clear(virtualImageColor);
                    using var newImage = SKImage.FromBitmap(newBitmap);
                    encodedImage = EncodeSkiaImage(newImage, actions);
                }
                else
                {
                    using var stream = new MemoryStream(imageData);
                    using var codec = SKCodec.Create(stream);
                    using var originalBitmap = SKBitmap.Decode(codec);
                    using var newImage = SKImage.FromBitmap(originalBitmap);

                    encodedImage = EncodeSkiaImage(newImage, actions, codec);
                }

                if (encodedImage == null)
                {
                    throw new ImageProcessorException("Error processing image");
                }
            });

            return encodedImage.ToArray();
        }

        public static SKData EncodeSkiaImage(SKImage newImage, ImageActions imageActions, SKCodec codec = null)
        {
            //make sure image was loaded successfully
            if (newImage == null)
            {
                throw new ImageProcessorException("Error processing image");
            }

            // when only width is set, calculate the height
            if (imageActions.Width.HasValue &&
                imageActions.Height.HasValue == false)
            {
                var aspectRatio = (double)newImage.Height / newImage.Width;
                imageActions.Height = (int)(imageActions.Width.Value * aspectRatio);
            }

            // when only height is set, calculate the width
            if (imageActions.Height.HasValue &&
                imageActions.Width.HasValue == false)
            {
                var aspectRatio = (double)newImage.Width / newImage.Height;
                imageActions.Width = (int)(imageActions.Height.Value * aspectRatio);
            }

            // when neither width nor height is set, use the original image dimensions
            if (imageActions.Width.HasValue == false &&
                imageActions.Height.HasValue == false)
            {
                imageActions.Width = (int)newImage.Width;
                imageActions.Height = (int)newImage.Height;
            }

            //the image actions width and height should always be set from here on, have a sanity check just in case
            if (imageActions.Width.HasValue == false || imageActions.Height.HasValue == false)
            {
                throw new ImageProcessorException("Width and Height could not be calculated");
            }

            // if the mode is max then constrain dimensions to requested width and height
            if (imageActions.Mode == ImageMode.Max)
            {
                var aspectRatio = (double)newImage.Width / newImage.Height;
                if (imageActions.Width.Value / aspectRatio <= imageActions.Height.Value)
                {
                    imageActions.Height = (int)(imageActions.Width.Value / aspectRatio);
                }
                else
                {
                    imageActions.Width = (int)(imageActions.Height.Value * aspectRatio);
                }
            }

            // Create a new bitmap with the new dimensions
            var skBmp = new SKBitmap(imageActions.Width.Value, imageActions.Height.Value);
            var recorder = new SKPictureRecorder();
            var rect = new SKRect(0, 0, imageActions.Width.Value, imageActions.Height.Value);
            var canvas = recorder.BeginRecording(rect);

            //if no shape specified, but a corner radius is then set shape to rounded rectangle
            if (imageActions.Shape.HasValue == false && imageActions.CornerRadius.HasValue)
            {
                imageActions.Shape = ImageShape.RoundedRectangle;
            }

            //if a shape is specified then clip the canvas to that shape
            if (imageActions.Shape.HasValue)
            {
                var paint = new SKPaint
                {
                    IsAntialias = true,
                    BlendMode = SKBlendMode.SrcIn
                };

                if (imageActions.Shape == ImageShape.Circle)
                {
                    var radius = Math.Min(imageActions.Width.Value, imageActions.Height.Value) / 2;
                    var centerX = imageActions.Width.Value / 2;
                    var centerY = imageActions.Height.Value / 2;

                    var a = new SKPath();
                    a.AddCircle(centerX, centerY, radius);
                    canvas.ClipPath(a);
                }
                else if (imageActions.Shape == ImageShape.Ellipse)
                {
                    var a = new SKPath();
                    var centerX = imageActions.Width.Value / 2;
                    var centerY = imageActions.Height.Value / 2;
                    var r = new SKRect(0, 0, imageActions.Width.Value, imageActions.Height.Value);
                    a.AddOval(r);
                    canvas.ClipPath(a);
                }
                else if (imageActions.Shape == ImageShape.RoundedRectangle)
                {
                    var a = new SKPath();
                    var r = new SKRect(0, 0, imageActions.Width.Value, imageActions.Height.Value);
                    if (imageActions.CornerRadius.HasValue)
                    {

                        a.AddRoundRect(r, imageActions.CornerRadius.Value, imageActions.CornerRadius.Value);
                    }
                    else
                    {
                        a.AddRoundRect(r, CornerRadius, CornerRadius);
                    }
                    canvas.ClipPath(a);
                }
            }

            bool isOddRotation = false;

            //use the exif data to rotate the image
            if (codec != null)
            {
                var orientation = codec.EncodedOrigin;
                switch (orientation)
                {
                    default:
                    case SKEncodedOrigin.Default:
                        break;

                    case SKEncodedOrigin.TopRight:
                        canvas.RotateDegrees(180, imageActions.Width.Value / 2, imageActions.Height.Value / 2);
                        break;

                    case SKEncodedOrigin.BottomRight:
                        canvas.RotateDegrees(180, imageActions.Width.Value / 2, imageActions.Height.Value / 2);
                        break;

                    case SKEncodedOrigin.BottomLeft:
                        break;

                    case SKEncodedOrigin.LeftTop:
                        canvas.RotateDegrees(90, imageActions.Width.Value / 2, imageActions.Height.Value / 2);
                        isOddRotation = true;
                        break;

                    case SKEncodedOrigin.RightTop:
                        canvas.RotateDegrees(90, imageActions.Width.Value / 2, imageActions.Height.Value / 2);
                        isOddRotation = true;
                        break;

                    case SKEncodedOrigin.RightBottom:
                        canvas.RotateDegrees(270, imageActions.Width.Value / 2, imageActions.Height.Value / 2);
                        isOddRotation = true;
                        break;

                    case SKEncodedOrigin.LeftBottom:
                        canvas.RotateDegrees(270, imageActions.Width.Value / 2, imageActions.Height.Value / 2);
                        isOddRotation = true;
                        break;
                }
            }

            //calculate the offset
            var rotationOffsetX = 0f;
            var rotationOffsetY = 0f;
            if (isOddRotation)
            {
                rotationOffsetY = (imageActions.Height.Value - imageActions.Width.Value) / 2;
                rotationOffsetX = rotationOffsetY * -1;
            }

            var imagePaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };

            //write to the canvas
            switch (imageActions.Mode)
            {
                //depend on canvas size
                default:
                case null:
                case ImageMode.Stretch:
                case ImageMode.Max:
                    if (isOddRotation)
                    {
                        var drawRect = new SKRect(rotationOffsetX, rotationOffsetY, imageActions.Height.Value, imageActions.Width.Value);
                        canvas.DrawImage(newImage, drawRect, paint:imagePaint);
                    }
                    else
                    {
                        var drawRect = new SKRect(0, 0, imageActions.Width.Value, imageActions.Height.Value);
                        canvas.DrawImage(newImage, drawRect, paint: imagePaint);
                    }
                    break;

                //fit within canvas
                case ImageMode.Fit:
                    var fitScale = Math.Min((double)imageActions.Width.Value / newImage.Width, (double)imageActions.Height.Value / newImage.Height);
                    if (isOddRotation)
                    {
                        fitScale = Math.Min((double)imageActions.Height.Value / newImage.Width, (double)imageActions.Width.Value / newImage.Height);
                    }
                    var fitScaledWidth = (int)(newImage.Width * fitScale);
                    var fitScaledHeight = (int)(newImage.Height * fitScale);
                    var fitOffsetX = (imageActions.Width.Value - fitScaledWidth) / 2;
                    var fitOffsetY = (imageActions.Height.Value - fitScaledHeight) / 2;
                    var drawRect2 = new SKRect(fitOffsetX, fitOffsetY, fitScaledWidth, fitScaledHeight);

                    canvas.DrawImage(newImage, drawRect2, paint: imagePaint);
                    break;

                //zoom in and fill canvas while maintaing aspect ratio
                case ImageMode.Zoom:
                    var scale = Math.Max((double)imageActions.Width.Value / newImage.Width, (double)imageActions.Height.Value / newImage.Height);
                    if (isOddRotation)
                    {
                        scale = Math.Max((double)imageActions.Height.Value / newImage.Width, (double)imageActions.Width.Value / newImage.Height);
                    }
                    var scaledWidth = (int)(newImage.Width * scale);
                    var scaledHeight = (int)(newImage.Height * scale);
                    var offsetX = (imageActions.Width.Value - scaledWidth) / 2;
                    var offsetY = (imageActions.Height.Value - scaledHeight) / 2;
                    var drawRect3 = new SKRect(offsetX, offsetY, scaledWidth, scaledHeight);
                    canvas.DrawImage(newImage, drawRect3, paint: imagePaint);
                    break;
            }

            //if there is text to draw then do it
            if (!string.IsNullOrEmpty(imageActions.Text))
            {
                var textToPrint = imageActions.Text;

                if (imageActions.AsInitials.HasValue && imageActions.AsInitials.Value == true)
                {
                    textToPrint = GetInitials(imageActions.Text);
                }

                var myTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
                var myFontSize = (int)(imageActions.Height.Value * 0.85);

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
                var maxWidth = imageActions.Width.Value * 0.75;

                // it needs to fit in the image, so if it is too narrow then we need to shrink down the font
                if (textSize > maxWidth)
                {
                    myFontSize = (int)((maxWidth / textSize) * myFontSize);
                }

                // Calculate maximum font size to fit text within the image
                paint.TextSize = myFontSize;

                //set the text color
                if (!string.IsNullOrEmpty(imageActions.TextColor))
                {
                    //try regular
                    if (SKColor.TryParse(imageActions.TextColor, out var newColor))
                    {
                        paint.Color = newColor;
                    }
                    //try hex
                    else if (SKColor.TryParse($"#{imageActions.TextColor}", out var newColorFromHex))
                    {
                        paint.Color = newColorFromHex;
                    }
                    //fall back to white if they both fail
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
                var x = imageActions.Width.Value / 2f;
                var y = (imageActions.Height.Value / 2f) - ((paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2);

                canvas.DrawText(textToPrint, x, y, paint);
            }

            //set export format
            var exportImageType = SKEncodedImageFormat.Png;
            if (imageActions.Format.HasValue) //user specified overrides default
            {
                exportImageType = imageActions.Format.Value;
            }
            else if (codec != null) //otherwise export as what it started as
            {
                exportImageType = codec.EncodedFormat;
            }

            //set requested format so export know what to do
            imageActions.Format = exportImageType;

            //set encoding quality
            SKData encodedImage = null;
            var picture = recorder.EndRecording();
            var ouputSize = new SKSizeI(imageActions.Width.Value, imageActions.Height.Value);
            var outputImage = SKImage.FromPicture(picture, ouputSize);
            switch (exportImageType)
            {
                default:
                    encodedImage = outputImage.Encode(exportImageType, 100);
                    break;

                case SKEncodedImageFormat.Jpeg:
                    encodedImage = outputImage.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                    break;

                case SKEncodedImageFormat.Gif:
                    encodedImage = outputImage.Encode(SKEncodedImageFormat.Gif, GifQuality);
                    break;
            }

            return encodedImage;
        }

        public static float GetMaxFontSize(double sectorSize, SKTypeface typeface, string text, float degreeOfCertainty = 1f, float maxFont = 100f)
        {
            var max = maxFont; // The upper bound. We know the font size is below this value
            var min = 0f; // The lower bound, We know the font size is equal to or above this value
            var last = -1f; // The last calculated value.
            float value;
            while (true)
            {
                value = min + ((max - min) / 2); // Find the half way point between Max and Min
                using (SKFont ft = new SKFont(typeface, value))
                using (SKPaint paint = new SKPaint(ft))
                {
                    if (paint.MeasureText(text) > sectorSize) // Measure the string size at this font size
                    {
                        // The text size is too large
                        // therefore the max possible size is below value
                        last = value;
                        max = value;
                    }
                    else
                    {
                        // The text fits within the area
                        // therefore the min size is above or equal to value
                        min = value;

                        // Check if this value is within our degree of certainty
                        if (Math.Abs(last - value) <= degreeOfCertainty)
                            return last; // Value is within certainty range, we found the best font size!

                        //This font difference is not within our degree of certainty
                        last = value;
                    }
                }
            }
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
            // Use a regular expression to split the input into words
            var words = Regex.Split(input, @"(?<!^)(?=[A-Z])|[_\s]+");

            // Extract the first letter of each word
            var initials = string.Empty;
            foreach (var word in words)
            {
                if (!string.IsNullOrEmpty(word))
                {
                    initials += word[0];
                    if (initials.Length == 2)
                    {
                        break;
                    }
                }
            }

            return initials.ToUpper();
        }
    }
}
