using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CommonImageActions.Core
{
    public class ImageActionQueryBuilder : ImageActionsBuilder<ImageActionQueryBuilder>
    {
        private byte[] _imageData { get; set; }
        private bool _isVirtual { get; set; } = false;

        public ImageActionQueryBuilder(byte[] imageData, ImageActions actions) : base(actions)
        {
            _imageData = imageData;
        }

        public ImageActionQueryBuilder(byte[] imageData) : base(null)
        {
            _imageData = imageData;
        }

        public ImageActionQueryBuilder() : base(null)
        {
            _isVirtual = true;
        }

        public ImageActionQueryBuilder IsVirtual(bool isVirtual)
        {
            _isVirtual = isVirtual;
            return this;
        }

        public byte[] ToImage()
        {
            //GetResult rethrows the original exception instead of wrapping it in an AggregateException
            return ToImageAsync().GetAwaiter().GetResult();
        }

        public async Task<byte[]> ToImageAsync()
        {
            //ConfigureAwait(false) all the way down so blocking in ToImage can not deadlock a UI thread
            return await ImageProcessor.ProcessHelperAsync(_imageData, Actions, isVirtual: _isVirtual).ConfigureAwait(false);
        }

        /// <summary>
        /// Process the image and write the result to a stream.
        /// </summary>
        public async Task ToStreamAsync(Stream outputStream)
        {
            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            var result = await ToImageAsync().ConfigureAwait(false);
            await outputStream.WriteAsync(result, 0, result.Length).ConfigureAwait(false);
        }
    }
}
