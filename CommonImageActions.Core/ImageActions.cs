using SkiaSharp;

namespace CommonImageActions.Core
{
    public class ImageActions
    {
        public ImageActions()
        {

        }

        public ImageActions(ImageActions defaults)
        {
            if (defaults != null)
            {
                Width = defaults.Width;
                Height = defaults.Height;
                Page = defaults.Page;
                PdfPassword = defaults.PdfPassword;
                Format = defaults.Format;
                Quality = defaults.Quality;
                Mode = defaults.Mode;
                Anchor = defaults.Anchor;
                FocalPointX = defaults.FocalPointX;
                FocalPointY = defaults.FocalPointY;
                AllowUpscale = defaults.AllowUpscale;
                Resampler = defaults.Resampler;
                CropX = defaults.CropX;
                CropY = defaults.CropY;
                CropWidth = defaults.CropWidth;
                CropHeight = defaults.CropHeight;
                Rotate = defaults.Rotate;
                FlipHorizontal = defaults.FlipHorizontal;
                FlipVertical = defaults.FlipVertical;
                Shape = defaults.Shape;
                CornerRadius = defaults.CornerRadius;
                BackgroundColor = defaults.BackgroundColor;
                Text = defaults.Text;
                AsInitials = defaults.AsInitials;
                TextColor = defaults.TextColor;
                FontFamily = defaults.FontFamily;
                FontSize = defaults.FontSize;
                TextAnchor = defaults.TextAnchor;
                TextWrap = defaults.TextWrap;
                ImageColor = defaults.ImageColor;
                ChooseImageColorFromTextValue = defaults.ChooseImageColorFromTextValue;
                IsGrayscale = defaults.IsGrayscale;
                IsSepia = defaults.IsSepia;
                IsInverted = defaults.IsInverted;
                Brightness = defaults.Brightness;
                Contrast = defaults.Contrast;
                Saturation = defaults.Saturation;
                Blur = defaults.Blur;
                Sharpen = defaults.Sharpen;
                Pixelate = defaults.Pixelate;
                Vignette = defaults.Vignette;
                WatermarkImage = defaults.WatermarkImage;
                WatermarkAnchor = defaults.WatermarkAnchor;
                WatermarkOpacity = defaults.WatermarkOpacity;
                WatermarkScale = defaults.WatermarkScale;
                WatermarkMargin = defaults.WatermarkMargin;
                KeepMetadata = defaults.KeepMetadata;
            }
        }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public int? Page { get; set; }

        public ImageShape? Shape { get; set; }

        public int? CornerRadius { get; set; }

        public string PdfPassword { get; set; }

        public string Text { get; set; }

        public bool? AsInitials { get; set; }

        public string TextColor { get; set; }

        public string ImageColor { get; set; }

        public bool? ChooseImageColorFromTextValue { get; set; }

        public bool? IsGrayscale { get; set; }

        public SKEncodedImageFormat? Format { get; set; }

        public ImageMode? Mode { get; set; }

        /// <summary>
        /// Encoding quality from 1 to 100 for jpeg and webp. When not set jpeg uses
        /// <see cref="ImageProcessor.JpegQuality"/> and webp is lossless.
        /// </summary>
        public int? Quality { get; set; }

        /// <summary>
        /// Which part of the image is kept when zoom crops it, and where the image sits when it is
        /// fitted into a larger space. Center when not set.
        /// </summary>
        public ImageAnchor? Anchor { get; set; }

        /// <summary>
        /// The point of interest in the image from 0 (left) to 1 (right). When zoom crops the image
        /// this point is kept as close to the middle as possible. Takes priority over Anchor.
        /// </summary>
        public double? FocalPointX { get; set; }

        /// <summary>
        /// The point of interest in the image from 0 (top) to 1 (bottom).
        /// </summary>
        public double? FocalPointY { get; set; }

        /// <summary>
        /// When false an image is never made larger than its original size, the requested width
        /// and height are reduced instead. Images are enlarged when not set.
        /// </summary>
        public bool? AllowUpscale { get; set; }

        /// <summary>
        /// How pixels are calculated when resizing. Bicubic when not set.
        /// </summary>
        public ImageResampler? Resampler { get; set; }

        /// <summary>
        /// Left edge, in pixels of the original image, of the part of the image to use. The crop
        /// is taken before any rotation or resizing. All four crop values are needed.
        /// </summary>
        public int? CropX { get; set; }

        public int? CropY { get; set; }

        public int? CropWidth { get; set; }

        public int? CropHeight { get; set; }

        /// <summary>
        /// Degrees to rotate the image clockwise. Any angle can be used; the image is made
        /// large enough to hold the rotated picture, with the corners left empty.
        /// </summary>
        public double? Rotate { get; set; }

        /// <summary>
        /// Mirror the image left to right. Flips are applied before rotation.
        /// </summary>
        public bool? FlipHorizontal { get; set; }

        /// <summary>
        /// Mirror the image top to bottom.
        /// </summary>
        public bool? FlipVertical { get; set; }

        /// <summary>
        /// Colour, as hex without the #, painted behind the image. It fills the space left by fit,
        /// by a shape, by rotation and by transparent pixels, which are black in a jpeg otherwise.
        /// </summary>
        public string BackgroundColor { get; set; }

        /// <summary>
        /// Font to draw text with. Either the family name of an installed font or a name given
        /// to <see cref="ImageProcessor.RegisterFont(string, byte[])"/>. Arial when not set.
        /// </summary>
        public string FontFamily { get; set; }

        /// <summary>
        /// Text size in pixels. When not set the text is sized to fill the image.
        /// </summary>
        public int? FontSize { get; set; }

        /// <summary>
        /// Where the text is placed. Center when not set.
        /// </summary>
        public ImageAnchor? TextAnchor { get; set; }

        /// <summary>
        /// When true, and a font size is set, text that is too wide is wrapped onto more lines.
        /// </summary>
        public bool? TextWrap { get; set; }

        public bool? IsSepia { get; set; }

        /// <summary>
        /// Invert the colours, like a photo negative.
        /// </summary>
        public bool? IsInverted { get; set; }

        /// <summary>
        /// 1 is unchanged, below 1 is darker, above 1 is brighter.
        /// </summary>
        public double? Brightness { get; set; }

        /// <summary>
        /// 1 is unchanged, below 1 is flatter, above 1 has more contrast.
        /// </summary>
        public double? Contrast { get; set; }

        /// <summary>
        /// 1 is unchanged, 0 is grayscale, above 1 is more vivid.
        /// </summary>
        public double? Saturation { get; set; }

        /// <summary>
        /// Gaussian blur radius (sigma) in pixels of the final image.
        /// </summary>
        public double? Blur { get; set; }

        /// <summary>
        /// Sharpening strength. 1 is a moderate sharpen.
        /// </summary>
        public double? Sharpen { get; set; }

        /// <summary>
        /// Size in pixels of the blocks the final image is broken into.
        /// </summary>
        public int? Pixelate { get; set; }

        /// <summary>
        /// Darken the corners. 0 is none, 1 is black corners.
        /// </summary>
        public double? Vignette { get; set; }

        /// <summary>
        /// An encoded image (png, jpeg, ...) to draw over the top of the result, such as a logo.
        /// </summary>
        public byte[] WatermarkImage { get; set; }

        /// <summary>
        /// Where the watermark is placed. BottomRight when not set.
        /// </summary>
        public ImageAnchor? WatermarkAnchor { get; set; }

        /// <summary>
        /// 0 is invisible, 1 is solid. Solid when not set.
        /// </summary>
        public double? WatermarkOpacity { get; set; }

        /// <summary>
        /// Width of the watermark as a fraction of the width of the result. 0.25 when not set.
        /// </summary>
        public double? WatermarkScale { get; set; }

        /// <summary>
        /// Space in pixels between the watermark and the edge. 3% of the shorter side when not set.
        /// </summary>
        public int? WatermarkMargin { get; set; }

        /// <summary>
        /// Copy the exif, xmp and iptc metadata of the original into the result. Only possible
        /// when both the original and the result are jpeg. Metadata is removed when not set.
        /// </summary>
        public bool? KeepMetadata { get; set; }

        public bool HasCrop()
        {
            return CropX.HasValue && CropY.HasValue && CropWidth.HasValue && CropHeight.HasValue;
        }

        public bool HasAnyActions()
        {
            return Width.HasValue
                || Height.HasValue
                || Format.HasValue
                || Quality.HasValue
                || Page.HasValue
                || Shape.HasValue
                || Mode.HasValue
                || ChooseImageColorFromTextValue.HasValue
                || CornerRadius.HasValue
                || IsGrayscale.HasValue
                || IsSepia.HasValue
                || IsInverted.HasValue
                || Brightness.HasValue
                || Contrast.HasValue
                || Saturation.HasValue
                || Blur.HasValue
                || Sharpen.HasValue
                || Pixelate.HasValue
                || Vignette.HasValue
                || Rotate.HasValue
                || FlipHorizontal.HasValue
                || FlipVertical.HasValue
                || HasCrop()
                || (WatermarkImage != null && WatermarkImage.Length > 0)
                || string.IsNullOrEmpty(BackgroundColor) == false
                || string.IsNullOrEmpty(ImageColor) == false
                || string.IsNullOrEmpty(Text) == false;
        }
    }
}
