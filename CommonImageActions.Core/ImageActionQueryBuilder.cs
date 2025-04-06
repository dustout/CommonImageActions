using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CommonImageActions.Core
{
    public class ImageActionQueryBuilder
    {
        private ImageActions _actions { get; set; }
        private byte[] _imageData { get; set; }
        private bool _isVirtual { get; set; } = false;

        public ImageActionQueryBuilder(byte[] imageData, ImageActions actions)
        {
            _imageData = imageData;

            if(actions == null)
            {
                _actions = new ImageActions();
            }
            else
            {
                _actions = actions;
            }
        }

        public ImageActionQueryBuilder(byte[] imageData)
        {
            _imageData = imageData;
            _actions = new ImageActions();
        }

        public ImageActionQueryBuilder()
        {
            _isVirtual = true;
            _actions = new ImageActions();
        }

        public ImageActionQueryBuilder Width (int width)
        {
            _actions.Width = width;
            return this;
        }

        public ImageActionQueryBuilder IsVirtual(bool isVirtual)
        {
            _isVirtual = isVirtual;
            return this;
        }

        public ImageActionQueryBuilder Shape(ImageShape shape)
        {
            _actions.Shape = shape;
            return this;
        }

        public ImageActionQueryBuilder CornerRadius(int cornerRadius)
        {
            _actions.CornerRadius = cornerRadius;
            return this;
        }

        public ImageActionQueryBuilder Text(string text)
        {
            _actions.Text = text;
            return this;
        }

        public ImageActionQueryBuilder AsInitials(bool asInitials)
        {
            _actions.AsInitials = asInitials;
            return this;
        }

        public ImageActionQueryBuilder TextColor(string textColor)
        {
            _actions.TextColor = textColor;
            return this;
        }

        public ImageActionQueryBuilder ImageColor(string imageColor)
        {
            _actions.ImageColor = imageColor;
            return this;
        }

        public ImageActionQueryBuilder ChooseImageColorFromTextValue(bool chooseImageColorFromTextValue)
        {
            _actions.ChooseImageColorFromTextValue = chooseImageColorFromTextValue;
            return this;
        }

        public ImageActionQueryBuilder Format(SkiaSharp.SKEncodedImageFormat format)
        {
            _actions.Format = format;
            return this;
        }

        public ImageActionQueryBuilder Mode(ImageMode mode)
        {
            _actions.Mode = mode;
            return this;
        }

        public ImageActionQueryBuilder PdfPassword(string pdfPassword)
        {
            _actions.PdfPassword = pdfPassword;
            return this;
        }

        public ImageActionQueryBuilder Page(string page)
        {
            _actions.Page = int.Parse(page);
            return this;
        }

        public ImageActionQueryBuilder Height(int height)
        {
            _actions.Height = height;
            return this;
        }

        public ImageActionQueryBuilder Page(int page)
        {
            _actions.Page = page;
            return this;
        }

        public byte[] ToImage()
        {
            return ToImageAsync().Result;
        }

        public async Task<byte[]> ToImageAsync() { 
            return await ImageProcessor.ProcessHelperAsync(_imageData, _actions, isVirtual: _isVirtual);
        }
    }
}
