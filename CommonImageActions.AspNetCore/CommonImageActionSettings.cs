using CommonImageActions.Core;
using System;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionSettings
    {
        private string _pathToWatch = "";
        public string PathToWatch {
            get
            {
                return this._pathToWatch;
            }

            set
            {
                if(value == "/" || value == null)
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

        public ImageActions DefaultImageActions { get; set; }

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
