using CommonImageActions.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CommonImageActions.Pdf
{
    public class PdfActionQueryBuilder 
    {
        private ImageActions _actions { get; set; }
        private byte[] _imageData { get; set; }

        public PdfActionQueryBuilder(byte[] imageData, ImageActions actions)
        {
            _imageData = imageData;

            if (actions == null)
            {
                _actions = new ImageActions();
            }
            else
            {
                _actions = actions;
            }
        }

        public PdfActionQueryBuilder(byte[] imageData)
        {
            _imageData = imageData;
            _actions = new ImageActions();
        }

        public PdfActionQueryBuilder Width(int width)
        {
            _actions.Width = width;
            return this;
        }

        public PdfActionQueryBuilder Shape(ImageShape shape)
        {
            _actions.Shape = shape;
            return this;
        }

        public PdfActionQueryBuilder CornerRadius(int cornerRadius)
        {
            _actions.CornerRadius = cornerRadius;
            return this;
        }

        public PdfActionQueryBuilder Text(string text)
        {
            _actions.Text = text;
            return this;
        }

        public PdfActionQueryBuilder AsInitials(bool asInitials)
        {
            _actions.AsInitials = asInitials;
            return this;
        }

        public PdfActionQueryBuilder TextColor(string textColor)
        {
            _actions.TextColor = textColor;
            return this;
        }

        public PdfActionQueryBuilder ImageColor(string imageColor)
        {
            _actions.ImageColor = imageColor;
            return this;
        }

        public PdfActionQueryBuilder ChooseImageColorFromTextValue(bool chooseImageColorFromTextValue)
        {
            _actions.ChooseImageColorFromTextValue = chooseImageColorFromTextValue;
            return this;
        }

        public PdfActionQueryBuilder Format(SkiaSharp.SKEncodedImageFormat format)
        {
            _actions.Format = format;
            return this;
        }

        public PdfActionQueryBuilder Mode(ImageMode mode)
        {
            _actions.Mode = mode;
            return this;
        }

        public PdfActionQueryBuilder PdfPassword(string pdfPassword)
        {
            _actions.PdfPassword = pdfPassword;
            return this;
        }

        public PdfActionQueryBuilder Page(string page)
        {
            _actions.Page = int.Parse(page);
            return this;
        }

        public PdfActionQueryBuilder Height(int height)
        {
            _actions.Height = height;
            return this;
        }

        public PdfActionQueryBuilder Page(int page)
        {
            _actions.Page = page;
            return this;
        }

        public byte[] ToImage()
        {
            return ToImageAsync().Result;
        }

        public async Task<byte[]> ToImageAsync()
        {
            return await PdfProcessor.ProcessHelperAsync(_imageData, _actions);
        }

    }
}
