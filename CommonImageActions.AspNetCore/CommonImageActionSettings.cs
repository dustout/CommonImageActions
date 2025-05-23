﻿using CommonImageActions.Core;
using System;

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

        public ImageActions DefaultImageActions { get; set; }

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
