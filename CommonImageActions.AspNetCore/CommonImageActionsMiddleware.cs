using CommonImageActions.Core;
using CommonImageActions.Pdf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionsMiddleware
    {
        //how many bytes from the start of a file are needed to tell what kind of image it is
        private const int ContentTypeHeaderLength = 12;

        //one client for the life of the application. Creating a client per request leaves its
        //sockets behind after every call, and under load the server runs out of them
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly RequestDelegate _next;
        private readonly CommonImageActionSettings _options;
        private readonly IHostEnvironment _env;

        public CommonImageActionsMiddleware(RequestDelegate next, IOptions<CommonImageActionSettings> options, IHostEnvironment env)
        {
            _next = next;
            _options = options.Value;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            //if no path then always ignore
            if (context.Request.Path.HasValue == false)
            {
                await _next(context);
                return;
            }

            //only requests that read an image are handled, the rest are left for the pipeline
            var isGetOrHead = string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
            if (isGetOrHead == false)
            {
                await _next(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments(_options.PathToWatch, out var pathAfterWatchedPath))
            {
                //the path as the browser meant it, with any %20 style escaping already decoded
                var requestPath = context.Request.Path.Value;

                //check if request is for a supported image
                var imageExtension = GetImageExtension(requestPath);
                if (imageExtension == null)
                {
                    await _next(context);
                    return;
                }

                //get file information
                var webRootPath = Path.Combine(_env.ContentRootPath, "wwwroot");
                var isPdf = string.Equals(imageExtension, ".pdf", StringComparison.OrdinalIgnoreCase);
                var isRemoteServer = !string.IsNullOrEmpty(_options.RemoteFileServerUrl);
                var isVirtual = _options.IsVirtual;

                //convert query string into image actions
                var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;
                var imageActions = ConvertQueryStringToImageActions(queryString, _options.DefaultImageActions);
                byte[] imageData = null;

                //if the file name is to be used as text then set it, unless the request asked for its own text
                if (_options.UseFileNameAsText && string.IsNullOrEmpty(GetTextFromQueryString(queryString)))
                {
                    var lastSegment = requestPath.Substring(requestPath.LastIndexOf('/') + 1);
                    var fileName = Path.GetFileNameWithoutExtension(lastSegment);
                    if (string.IsNullOrEmpty(fileName) == false)
                    {
                        imageActions.Text = fileName;
                    }
                }

                //check to see if file is in the cache, if it is then return it
                string cachedFilePath = null;
                if (_options.UseDiskCache)
                {
                    cachedFilePath = Path.Combine(GetDiskCacheLocation(webRootPath), GetCacheFileName(requestPath, imageActions));
                    if (await TrySendCachedFileAsync(context, cachedFilePath))
                    {
                        return;
                    }
                }

                if (isRemoteServer)
                {
                    //get the server path by removing the path to watch, keeping it escaped for the url
                    var serverPath = pathAfterWatchedPath.ToUriComponent().TrimStart('/');

                    //clean the remote url so if there is a slash then we remove it
                    var cleanedRemoteUrl = _options.RemoteFileServerUrl.TrimEnd('/');

                    //combine them as a request to the server
                    var imageRemoteUrlPath = new Uri($"{cleanedRemoteUrl}/{serverPath}");

                    //get the image from the remote server
                    using (var response = await _httpClient.GetAsync(imageRemoteUrlPath))
                    {
                        var responseData = await response.Content.ReadAsByteArrayAsync();

                        //if failure then forward the failure, if no actions then just send the raw file
                        if (response.IsSuccessStatusCode == false || imageActions.HasAnyActions() == false)
                        {
                            context.Response.StatusCode = (int)response.StatusCode;
                            context.Response.ContentType = response.Content.Headers.ContentType?.ToString()
                                ?? GetContentType(responseData, responseData.Length);
                            await context.Response.Body.WriteAsync(responseData, 0, responseData.Length);
                            return;
                        }

                        //get the image data and process it
                        if (isPdf)
                        {
                            imageData = await PdfProcessor.ProcessPdfAsync(responseData, imageActions);
                        }
                        else
                        {
                            imageData = await ImageProcessor.ProcessImageAsync(responseData, imageActions);
                        }
                    }
                }
                else if (isVirtual)
                {
                    imageData = await ImageProcessor.ProcessVirtualImageAsync(imageActions);
                }
                else
                {
                    //find the file, this is null if the request has been modified to point outside
                    //of wwwroot (for example if someone has requested root). Let the normal flow
                    //deal with that, and with a file that does not exist
                    var imageFilePath = GetLocalFilePath(webRootPath, requestPath);
                    if (imageFilePath == null || !File.Exists(imageFilePath))
                    {
                        await _next(context);
                        return;
                    }

                    //if there are no actions to perform then let the normal flow deal with it
                    if (imageActions.HasAnyActions() == false)
                    {
                        await _next(context);
                        return;
                    }

                    //load the image and process it
                    using var inputStream = File.OpenRead(imageFilePath);
                    if (isPdf)
                    {
                        imageData = await PdfProcessor.ProcessPdfAsync(inputStream, imageActions);
                    }
                    else
                    {
                        imageData = await ImageProcessor.ProcessImageAsync(inputStream, imageActions);
                    }
                }

                //if there is nothing to return then something went wrong, throw exception
                if (imageData == null)
                {
                    throw new NotImplementedException($"{imageExtension} not supported");
                }

                //set response type from what was actually produced
                context.Response.ContentType = GetContentType(imageData, imageData.Length);

                //send the resulting data
                await context.Response.Body.WriteAsync(imageData, 0, imageData.Length);

                //write to disk cache if enabled
                if (cachedFilePath != null)
                {
                    try
                    {
                        var diskCacheLocation = Path.GetDirectoryName(cachedFilePath);
                        if (Directory.Exists(diskCacheLocation) == false)
                        {
                            Directory.CreateDirectory(diskCacheLocation);
                        }

                        await File.WriteAllBytesAsync(cachedFilePath, imageData);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing cached file {cachedFilePath}. {ex.ToString()}");
                    }
                }

                return;
            }
            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }

        private string GetDiskCacheLocation(string webRootPath)
        {
            //make sure default disk cache location is set
            if (string.IsNullOrEmpty(CommonImageActionSettings.DefaultDiskCacheLocation))
            {
                CommonImageActionSettings.DefaultDiskCacheLocation = Path.Combine(webRootPath, "cache");
            }

            //chose the best most specific cache location
            var diskCacheLocation = _options.DiskCacheLocation;
            if (string.IsNullOrEmpty(diskCacheLocation))
            {
                diskCacheLocation = CommonImageActionSettings.DefaultDiskCacheLocation;
            }

            return diskCacheLocation;
        }

        private static async Task<bool> TrySendCachedFileAsync(HttpContext context, string cachedFilePath)
        {
            try
            {
                if (File.Exists(cachedFilePath) == false)
                {
                    return false;
                }

                using (var cachedFileStream = File.OpenRead(cachedFilePath))
                {
                    //the cached file has no extension, so read its first bytes to see what it is
                    var header = new byte[ContentTypeHeaderLength];
                    var headerLength = 0;
                    while (headerLength < header.Length)
                    {
                        var read = await cachedFileStream.ReadAsync(header, headerLength, header.Length - headerLength);
                        if (read == 0)
                        {
                            break;
                        }
                        headerLength += read;
                    }

                    context.Response.ContentType = GetContentType(header, headerLength);
                    await context.Response.Body.WriteAsync(header, 0, headerLength);
                    await cachedFileStream.CopyToAsync(context.Response.Body);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting cached file {cachedFilePath}. {ex.ToString()}");

                //once part of the file has been sent the response can not be started again
                return context.Response.HasStarted;
            }
        }

        /// <summary>
        /// The name of the cache file for a request. It is built from the path and the image actions
        /// that will be applied rather than the raw url, so the same image is cached once however the
        /// url is written, and query parameters that mean nothing do not create new cache files.
        /// It is hashed so the name is always short enough to be a valid file name.
        /// </summary>
        public static string GetCacheFileName(string requestPath, ImageActions imageActions)
        {
            var key = string.Join("|", new[]
            {
                requestPath,
                $"w={imageActions.Width}",
                $"h={imageActions.Height}",
                $"p={imageActions.Page}",
                $"s={imageActions.Shape}",
                $"cr={imageActions.CornerRadius}",
                $"in={imageActions.AsInitials}",
                $"ft={imageActions.ChooseImageColorFromTextValue}",
                $"g={imageActions.IsGrayscale}",
                $"f={imageActions.Format}",
                $"m={imageActions.Mode}",
                $"t={Uri.EscapeDataString(imageActions.Text ?? string.Empty)}",
                $"tc={Uri.EscapeDataString(imageActions.TextColor ?? string.Empty)}",
                $"c={Uri.EscapeDataString(imageActions.ImageColor ?? string.Empty)}",
                $"pw={Uri.EscapeDataString(imageActions.PdfPassword ?? string.Empty)}",
            });

            using (var sha256 = SHA256.Create())
            {
                return ByteArrayToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(key)));
            }
        }

        /// <summary>
        /// Convert the path of a request into the file it refers to inside the web root. Returns null
        /// when the path does not resolve to somewhere inside the web root.
        /// </summary>
        public static string GetLocalFilePath(string webRootPath, string requestPath)
        {
            try
            {
                var rootPath = Path.GetFullPath(webRootPath);
                var relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

                //GetFullPath resolves any .. in the path, so what is checked is where the file really is
                var filePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

                var separator = Path.DirectorySeparatorChar.ToString();
                var rootPathWithSeparator = rootPath.EndsWith(separator) ? rootPath : rootPath + separator;
                if (filePath.StartsWith(rootPathWithSeparator, StringComparison.Ordinal) == false)
                {
                    return null;
                }

                return filePath;
            }
            catch (Exception)
            {
                //the path has characters that can not be in a file name, so there is no file
                return null;
            }
        }

        /// <summary>
        /// Work out the content type from the first bytes of the data.
        /// </summary>
        public static string GetContentType(byte[] data, int length)
        {
            if (data == null)
            {
                return "application/octet-stream";
            }

            length = Math.Min(length, data.Length);

            if (StartsWith(data, length, 0xFF, 0xD8, 0xFF))
            {
                return "image/jpeg";
            }

            if (StartsWith(data, length, 0x89, 0x50, 0x4E, 0x47))
            {
                return "image/png";
            }

            //RIFF....WEBP
            if (StartsWith(data, length, 0x52, 0x49, 0x46, 0x46)
                && length >= 12 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            {
                return "image/webp";
            }

            //GIF8
            if (StartsWith(data, length, 0x47, 0x49, 0x46, 0x38))
            {
                return "image/gif";
            }

            //BM
            if (StartsWith(data, length, 0x42, 0x4D))
            {
                return "image/bmp";
            }

            //%PDF
            if (StartsWith(data, length, 0x25, 0x50, 0x44, 0x46))
            {
                return "application/pdf";
            }

            if (StartsWith(data, length, 0x00, 0x00, 0x01, 0x00))
            {
                return "image/x-icon";
            }

            return "application/octet-stream";
        }

        private static bool StartsWith(byte[] data, int length, params byte[] signature)
        {
            if (length < signature.Length)
            {
                return false;
            }

            for (var i = 0; i < signature.Length; i++)
            {
                if (data[i] != signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string ByteArrayToHexString(byte[] Bytes)
        {
            StringBuilder Result = new StringBuilder(Bytes.Length * 2);
            string HexAlphabet = "0123456789ABCDEF";

            foreach (byte B in Bytes)
            {
                Result.Append(HexAlphabet[(int)(B >> 4)]);
                Result.Append(HexAlphabet[(int)(B & 0xF)]);
            }

            return Result.ToString();
        }

        private static string GetTextFromQueryString(string queryString)
        {
            var query = HttpUtility.ParseQueryString(queryString ?? string.Empty);
            return query["text"] ?? query["t"];
        }

        public static ImageActions ConvertQueryStringToImageActions(string queryString, ImageActions defaultImageActions = null)
        {
            //initialize image actions
            var imageActions = new ImageActions(defaultImageActions);

            //if there are no paramters then let the normal flow deal with it
            var query = HttpUtility.ParseQueryString(queryString ?? string.Empty);
            if (query.Count == 0)
            {
                return imageActions;
            }

            //convert url parameters into an image action
            var widthString = query["width"] ?? query["w"];
            if (int.TryParse(widthString, out int width))
            {
                //sanity check to make sure no bad actor requests a number that may eat all the ram in the system
                if (width > 0 && width < CommonImageActionSettings.MaxUrlWidth)
                {
                    imageActions.Width = width;
                }
            }

            var heightString = query["height"] ?? query["h"];
            if (int.TryParse(heightString, out int height))
            {
                //sanity check to make sure no bad actor requests a number that may eat all the ram in the system
                if (height > 0 && height < CommonImageActionSettings.MaxUrlHeight)
                {
                    imageActions.Height = height;
                }
            }

            var pageString = query["Page"] ?? query["p"];
            if (int.TryParse(pageString, out int page))
            {
                imageActions.Page = page;
            }

            var cornerRadiusString = query["corner"] ?? query["cr"];
            if (int.TryParse(cornerRadiusString, out int cornerRadius))
            {
                imageActions.CornerRadius = cornerRadius;
            }

            //the text values are only set when they are in the url, otherwise a url that
            //does not mention them would wipe out the values from the default image actions
            var pdfPasswordString = query["password"] ?? query["pw"];
            if (pdfPasswordString != null)
            {
                imageActions.PdfPassword = pdfPasswordString;
            }

            var textString = query["text"] ?? query["t"];
            if (textString != null)
            {
                imageActions.Text = textString;
            }

            var asInitialsString = query["initials"] ?? query["in"];
            if (Boolean.TryParse(asInitialsString, out var asInitials))
            {
                imageActions.AsInitials = asInitials;
            }

            var asUseRandomColorString = query["colorFromText"] ?? query["ft"];
            if (Boolean.TryParse(asUseRandomColorString, out var asUseRandomColor))
            {
                imageActions.ChooseImageColorFromTextValue = asUseRandomColor;
            }

            var textColorString = query["textColor"] ?? query["tc"];
            if (textColorString != null)
            {
                imageActions.TextColor = textColorString;
            }

            var virtualImageColorString = query["color"] ?? query["c"];
            if (virtualImageColorString != null)
            {
                imageActions.ImageColor = virtualImageColorString;
            }

            var formatString = query["format"] ?? query["f"];
            if (Enum.TryParse<SKEncodedImageFormat>(formatString, true, out var format))
            {
                imageActions.Format = format;
            }

            var modeString = query["mode"] ?? query["m"];
            if (Enum.TryParse<ImageMode>(modeString, true, out var mode))
            {
                imageActions.Mode = mode;
            }

            var shapeString = query["shape"] ?? query["s"];
            if (Enum.TryParse<ImageShape>(shapeString, true, out var shape))
            {
                imageActions.Shape = shape;
            }

            var isGrayscale = query["gray"] ?? query["g"];
            if (Boolean.TryParse(isGrayscale, out var asIsGrayscale))
            {
                imageActions.IsGrayscale = asIsGrayscale;
            }

            //add unofficial support for other interpretations of mode based on feedback
            //example: I like zoom, but others feel it should be call crop, so just fall back to zoom
            //         as a general catchall for any other interpretations
            if (imageActions.Mode.HasValue == false && !string.IsNullOrEmpty(modeString))
            {
                if (string.Equals(modeString, "pad", StringComparison.InvariantCultureIgnoreCase))
                {
                    imageActions.Mode = ImageMode.Fit;
                }
                else
                {
                    imageActions.Mode = ImageMode.Zoom;
                }
            }

            return imageActions;
        }

        private string GetImageExtension(string path)
        {
            return CommonImageActionSettings.ValidImageExtensions.FirstOrDefault(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }
    }
}
