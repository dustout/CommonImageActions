using CommonImageActions.Core;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionSettings
    {
        private string _pathToWatch = "";
        public string PathToWatch
        {
            get
            {
                return this._pathToWatch;
            }

            set
            {
                if (value == "/" || value == null)
                {
                    this._pathToWatch = String.Empty;
                }
                else
                {
                    this._pathToWatch = $"/{value.TrimStart('/').TrimEnd('/')}";
                }
            }
        }

        public bool IsVirtual { get; set; }

        public string RemoteFileServerUrl { get; set; }

        public bool UseFileNameAsText { get; set; }

        public bool UseDiskCache { get; set; }

        public string DiskCacheLocation { get; set; }

        /// <summary>
        /// How long a file in the disk cache is used before the image is made again.
        /// Cached files are kept forever when not set.
        /// </summary>
        public TimeSpan? DiskCacheMaxAge { get; set; }

        /// <summary>
        /// The most disk space, in bytes, the disk cache is allowed to use. When it grows past this
        /// the oldest files are deleted. There is no limit when not set.
        /// </summary>
        public long? DiskCacheMaxSizeBytes { get; set; }

        /// <summary>
        /// How long a browser or CDN may reuse an image without asking the server again. Sent as
        /// the Cache-Control max-age header. When not set the header is not sent, and browsers
        /// check back with the server (which answers 304 Not Modified when nothing has changed).
        /// </summary>
        public TimeSpan? BrowserCacheMaxAge { get; set; }

        /// <summary>
        /// When set, a url with a query string is only processed if it ends with a valid signature
        /// made with this key (see <see cref="CommonImageActionsUrlSigner"/>). This stops anyone
        /// but your own site from asking for images in sizes of their choosing.
        /// </summary>
        public string SigningKey { get; set; }

        public ImageActions DefaultImageActions { get; set; }

        /// <summary>
        /// Called after the url has been turned into image actions and before anything is done
        /// with them. The actions can be changed here, for example to limit sizes or add a watermark.
        /// </summary>
        public Func<HttpContext, ImageActions, Task> OnActionsParsed { get; set; }

        /// <summary>
        /// Called just before an image is sent, after the status code and headers have been set.
        /// Headers can be added or changed here.
        /// </summary>
        public Action<HttpContext> OnPrepareResponse { get; set; }

        public static string DefaultDiskCacheLocation { get; set; }

        public static int MaxUrlWidth { get; set; } = 5000;

        public static int MaxUrlHeight { get; set; } = 5000;

        public static string[] ValidImageExtensions = {
            ".bmp",
            ".gif",
            ".ico",
            ".jpg",
            ".jpeg",
            ".png",
            ".wbmp",
            ".webp",
            ".pkm",
            ".ktx",
            ".astc",
            ".dng",
            ".heif",
            ".avif",
            ".jpegxl",
            ".jxl",

            ".pdf"
        };
    }
}
