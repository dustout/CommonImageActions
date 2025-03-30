using SkiaSharp;
using System;

namespace CommonImageActions.Core
{
    public class ImageActions
    {
        public int? Width { get; set; }

        public int? Height { get; set; }

        public int? Page { get; set; }

        public string PdfPassword { get; set; }

        public SKEncodedImageFormat? Format { get; set; }

        public ImageMode? Mode { get; set; }

        public bool HasAnyActions()
        {
            return Width.HasValue || Height.HasValue || Format.HasValue || Page.HasValue;
        }
    }
}
