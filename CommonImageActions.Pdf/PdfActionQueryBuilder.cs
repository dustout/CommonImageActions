using CommonImageActions.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CommonImageActions.Pdf
{
    public class PdfActionQueryBuilder : ImageActionsBuilder<PdfActionQueryBuilder>
    {
        private byte[] _imageData { get; set; }

        public PdfActionQueryBuilder(byte[] imageData, ImageActions actions) : base(actions)
        {
            _imageData = imageData;
        }

        public PdfActionQueryBuilder(byte[] imageData) : base(new ImageActions())
        {
            _imageData = imageData;
        }

        public byte[] ToImage()
        {
            //GetResult rethrows the original exception instead of wrapping it in an AggregateException
            return ToImageAsync().GetAwaiter().GetResult();
        }

        public async Task<byte[]> ToImageAsync()
        {
            //ConfigureAwait(false) all the way down so blocking in ToImage can not deadlock a UI thread
            return await PdfProcessor.ProcessHelperAsync(_imageData, Actions).ConfigureAwait(false);
        }

        /// <summary>
        /// Render the page and write the resulting image to a stream.
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
