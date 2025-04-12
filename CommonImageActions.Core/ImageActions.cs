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
                Mode = defaults.Mode;
                Shape = defaults.Shape;
                CornerRadius = defaults.CornerRadius;
                Text = defaults.Text;
                AsInitials = defaults.AsInitials;
                TextColor = defaults.TextColor;
                ImageColor = defaults.ImageColor;
                ChooseImageColorFromTextValue = defaults.ChooseImageColorFromTextValue;
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

        public SKEncodedImageFormat? Format { get; set; }

        public ImageMode? Mode { get; set; }

        public bool HasAnyActions()
        {
            return Width.HasValue 
                || Height.HasValue 
                || Format.HasValue 
                || Page.HasValue 
                || Shape.HasValue 
                || Mode.HasValue 
                || ChooseImageColorFromTextValue.HasValue
                || CornerRadius.HasValue
                || string.IsNullOrEmpty(ImageColor) == false
                || string.IsNullOrEmpty(Text) == false;
        }
    }
}
