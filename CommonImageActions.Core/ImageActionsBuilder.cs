using System;

namespace CommonImageActions.Core
{
    /// <summary>
    /// The fluent methods that set image actions. They live here once so that the image builder
    /// and the pdf builder always offer the same options.
    /// </summary>
    /// <typeparam name="TBuilder">The builder that inherits this class, so calls can be chained.</typeparam>
    public abstract class ImageActionsBuilder<TBuilder> where TBuilder : ImageActionsBuilder<TBuilder>
    {
        protected ImageActions Actions { get; set; }

        protected ImageActionsBuilder(ImageActions actions)
        {
            Actions = actions ?? new ImageActions();
        }

        private TBuilder This => (TBuilder)this;

        public TBuilder Width(int width)
        {
            Actions.Width = width;
            return This;
        }

        public TBuilder Height(int height)
        {
            Actions.Height = height;
            return This;
        }

        public TBuilder Mode(ImageMode mode)
        {
            Actions.Mode = mode;
            return This;
        }

        public TBuilder Anchor(ImageAnchor anchor)
        {
            Actions.Anchor = anchor;
            return This;
        }

        /// <summary>
        /// The point of interest in the image, each from 0 to 1, that zoom keeps in view.
        /// </summary>
        public TBuilder FocalPoint(double x, double y)
        {
            Actions.FocalPointX = x;
            Actions.FocalPointY = y;
            return This;
        }

        public TBuilder AllowUpscale(bool allowUpscale)
        {
            Actions.AllowUpscale = allowUpscale;
            return This;
        }

        public TBuilder Resampler(ImageResampler resampler)
        {
            Actions.Resampler = resampler;
            return This;
        }

        /// <summary>
        /// Use only part of the original image, given in pixels of the original.
        /// </summary>
        public TBuilder Crop(int x, int y, int width, int height)
        {
            Actions.CropX = x;
            Actions.CropY = y;
            Actions.CropWidth = width;
            Actions.CropHeight = height;
            return This;
        }

        public TBuilder Rotate(double degrees)
        {
            Actions.Rotate = degrees;
            return This;
        }

        public TBuilder FlipHorizontal(bool flipHorizontal = true)
        {
            Actions.FlipHorizontal = flipHorizontal;
            return This;
        }

        public TBuilder FlipVertical(bool flipVertical = true)
        {
            Actions.FlipVertical = flipVertical;
            return This;
        }

        public TBuilder Shape(ImageShape shape)
        {
            Actions.Shape = shape;
            return This;
        }

        public TBuilder CornerRadius(int cornerRadius)
        {
            Actions.CornerRadius = cornerRadius;
            return This;
        }

        public TBuilder BackgroundColor(string backgroundColor)
        {
            Actions.BackgroundColor = backgroundColor;
            return This;
        }

        public TBuilder Text(string text)
        {
            Actions.Text = text;
            return This;
        }

        public TBuilder AsInitials(bool asInitials)
        {
            Actions.AsInitials = asInitials;
            return This;
        }

        public TBuilder TextColor(string textColor)
        {
            Actions.TextColor = textColor;
            return This;
        }

        public TBuilder FontFamily(string fontFamily)
        {
            Actions.FontFamily = fontFamily;
            return This;
        }

        public TBuilder FontSize(int fontSize)
        {
            Actions.FontSize = fontSize;
            return This;
        }

        public TBuilder TextAnchor(ImageAnchor textAnchor)
        {
            Actions.TextAnchor = textAnchor;
            return This;
        }

        public TBuilder TextWrap(bool textWrap = true)
        {
            Actions.TextWrap = textWrap;
            return This;
        }

        public TBuilder ImageColor(string imageColor)
        {
            Actions.ImageColor = imageColor;
            return This;
        }

        public TBuilder ChooseImageColorFromTextValue(bool chooseImageColorFromTextValue)
        {
            Actions.ChooseImageColorFromTextValue = chooseImageColorFromTextValue;
            return This;
        }

        public TBuilder IsGrayscale(bool isGrayscale)
        {
            Actions.IsGrayscale = isGrayscale;
            return This;
        }

        public TBuilder IsSepia(bool isSepia = true)
        {
            Actions.IsSepia = isSepia;
            return This;
        }

        public TBuilder IsInverted(bool isInverted = true)
        {
            Actions.IsInverted = isInverted;
            return This;
        }

        public TBuilder Brightness(double brightness)
        {
            Actions.Brightness = brightness;
            return This;
        }

        public TBuilder Contrast(double contrast)
        {
            Actions.Contrast = contrast;
            return This;
        }

        public TBuilder Saturation(double saturation)
        {
            Actions.Saturation = saturation;
            return This;
        }

        public TBuilder Blur(double blur)
        {
            Actions.Blur = blur;
            return This;
        }

        public TBuilder Sharpen(double sharpen = 1)
        {
            Actions.Sharpen = sharpen;
            return This;
        }

        public TBuilder Pixelate(int blockSize)
        {
            Actions.Pixelate = blockSize;
            return This;
        }

        public TBuilder Vignette(double vignette = 0.5)
        {
            Actions.Vignette = vignette;
            return This;
        }

        /// <summary>
        /// Draw another image, such as a logo, over the top of the result.
        /// </summary>
        public TBuilder Watermark(byte[] watermarkImage, ImageAnchor anchor = ImageAnchor.BottomRight, double opacity = 1, double scale = 0.25, int? margin = null)
        {
            Actions.WatermarkImage = watermarkImage;
            Actions.WatermarkAnchor = anchor;
            Actions.WatermarkOpacity = opacity;
            Actions.WatermarkScale = scale;
            Actions.WatermarkMargin = margin;
            return This;
        }

        public TBuilder KeepMetadata(bool keepMetadata = true)
        {
            Actions.KeepMetadata = keepMetadata;
            return This;
        }

        public TBuilder Format(SkiaSharp.SKEncodedImageFormat format)
        {
            Actions.Format = format;
            return This;
        }

        public TBuilder Quality(int quality)
        {
            Actions.Quality = quality;
            return This;
        }

        public TBuilder PdfPassword(string pdfPassword)
        {
            Actions.PdfPassword = pdfPassword;
            return This;
        }

        public TBuilder Page(string page)
        {
            Actions.Page = int.Parse(page);
            return This;
        }

        public TBuilder Page(int page)
        {
            Actions.Page = page;
            return This;
        }
    }
}
