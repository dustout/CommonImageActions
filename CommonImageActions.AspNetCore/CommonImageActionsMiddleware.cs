using CommonImageActions.Core;
using CommonImageActions.Pdf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionsMiddleware
    {
        //one client for the life of the application. Creating a client per request leaves its
        //sockets behind after every call, and under load the server runs out of them
        private static readonly HttpClient _httpClient = new HttpClient();

        //every option an image can have, read once. The cache key is built from all of them so
        //that a new option can never be forgotten there
        private static readonly PropertyInfo[] _imageActionProperties = typeof(ImageActions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.CanRead)
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        private static readonly string _libraryVersion = typeof(ImageProcessor).Assembly.GetName().Version?.ToString();

        //cache folders that could not be written to, and when that was found out
        private static readonly ConcurrentDictionary<string, DateTime> _diskCacheWriteFailures = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly RequestDelegate _next;
        private readonly CommonImageActionSettings _options;
        private readonly IHostEnvironment _env;
        private readonly ILogger _logger;

        public CommonImageActionsMiddleware(RequestDelegate next, IOptions<CommonImageActionSettings> options, IHostEnvironment env, ILoggerFactory loggerFactory = null)
        {
            _next = next;
            _options = options.Value;
            _env = env;
            _logger = loggerFactory?.CreateLogger<CommonImageActionsMiddleware>();
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
            var isHead = string.Equals(context.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
            var isGet = string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase);
            if (isGet == false && isHead == false)
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
                var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;

                //when urls have to be signed, a url that asks for something needs a valid signature
                if (string.IsNullOrEmpty(_options.SigningKey) == false && string.IsNullOrEmpty(queryString) == false)
                {
                    var signedPath = $"{context.Request.PathBase.Value}{requestPath}";
                    if (CommonImageActionsUrlSigner.IsValid(signedPath, queryString, _options.SigningKey) == false)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                }

                //convert query string into image actions
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

                //give the application the chance to look at and change what is about to be done
                if (_options.OnActionsParsed != null)
                {
                    await _options.OnActionsParsed(context, imageActions);
                }

                //find the file when it is on this server
                string imageFilePath = null;
                string sourceVersion = null;
                DateTimeOffset? lastModified = null;
                if (isRemoteServer == false && isVirtual == false)
                {
                    //this is null if the request has been modified to point outside of wwwroot
                    //(for example if someone has requested root). Let the normal flow deal with
                    //that, and with a file that does not exist
                    imageFilePath = GetLocalFilePath(webRootPath, requestPath);
                    var imageFile = imageFilePath != null ? new FileInfo(imageFilePath) : null;
                    if (imageFile == null || imageFile.Exists == false)
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

                    //when the file was last saved and how big it is. Part of the cache key, so that
                    //replacing the file makes everything cached from the old one be ignored
                    sourceVersion = $"{imageFile.LastWriteTimeUtc.Ticks}-{imageFile.Length}";

                    //http dates only go down to the second
                    var lastWrite = imageFile.LastWriteTimeUtc;
                    lastModified = new DateTimeOffset(lastWrite.Year, lastWrite.Month, lastWrite.Day, lastWrite.Hour, lastWrite.Minute, lastWrite.Second, TimeSpan.Zero);
                }

                //the same request for the same file always has this name
                var cacheFileName = GetCacheFileName(requestPath, imageActions, sourceVersion);

                //for a local or virtual image the name is all that is needed to know nothing has
                //changed, so a browser that already has the image can be told so without any work.
                //A remote file can change without us knowing, so that is decided once we have it
                string etag = null;
                if (isRemoteServer == false)
                {
                    etag = $"\"{cacheFileName.Substring(0, 32)}\"";
                    if (IsNotModified(context.Request, etag, lastModified))
                    {
                        SendNotModified(context, etag, lastModified);
                        return;
                    }
                }

                //check to see if file is in the cache, if it is then use it
                string cachedFilePath = null;
                var isFromCache = false;
                if (_options.UseDiskCache)
                {
                    cachedFilePath = Path.Combine(GetDiskCacheLocation(webRootPath), cacheFileName);
                    try
                    {
                        imageData = await DiskCache.TryReadAsync(cachedFilePath, _options.DiskCacheMaxAge);
                        isFromCache = imageData != null;
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Error getting cached file {cachedFilePath}.");
                    }
                }

                if (imageData != null)
                {
                    //already have it from the cache
                }
                else if (isRemoteServer)
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

                //a remote image is identified by what it turned out to be
                if (etag == null)
                {
                    etag = $"\"{GetHash(imageData).Substring(0, 32)}\"";
                    if (IsNotModified(context.Request, etag, lastModified))
                    {
                        SendNotModified(context, etag, lastModified);
                        await WriteToDiskCacheAsync(cachedFilePath, imageData, isFromCache);
                        return;
                    }
                }

                //set response type from what was actually produced
                SetCachingHeaders(context, etag, lastModified);
                context.Response.ContentType = GetContentType(imageData, imageData.Length);
                context.Response.ContentLength = imageData.Length;
                _options.OnPrepareResponse?.Invoke(context);

                //send the resulting data, a HEAD request only wants to know about it
                if (isHead == false)
                {
                    await context.Response.Body.WriteAsync(imageData, 0, imageData.Length);
                }

                //write to disk cache if enabled. This is after the image has been sent so that
                //whoever asked for it is not kept waiting on the disk
                await WriteToDiskCacheAsync(cachedFilePath, imageData, isFromCache);

                return;
            }
            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }

        private async Task WriteToDiskCacheAsync(string cachedFilePath, byte[] imageData, bool isFromCache)
        {
            //nothing to do when the cache is off, or when this image came out of the cache
            if (cachedFilePath == null || isFromCache)
            {
                return;
            }

            //the cache is on unless it is turned off, so it can be on somewhere that can not be
            //written to (a read only file system for example). When a write fails, stop trying
            //for a while rather than failing and logging on every single request
            var folder = Path.GetDirectoryName(cachedFilePath);
            if (_diskCacheWriteFailures.TryGetValue(folder, out var failedAt)
                && DateTime.UtcNow - failedAt < CommonImageActionSettings.DiskCacheRetryInterval)
            {
                return;
            }

            try
            {
                await DiskCache.WriteAsync(cachedFilePath, imageData, _options.DiskCacheMaxAge, _options.DiskCacheMaxSizeBytes, LogError);
                _diskCacheWriteFailures.TryRemove(folder, out _);
            }
            catch (Exception ex)
            {
                _diskCacheWriteFailures[folder] = DateTime.UtcNow;
                LogError(ex, $"Error writing cached file {cachedFilePath}. Images are still being made and sent, but they are not being cached. " +
                    $"Writing to the cache will be tried again in {CommonImageActionSettings.DiskCacheRetryInterval.TotalMinutes} minutes. " +
                    $"Set {nameof(CommonImageActionSettings.DiskCacheLocation)} to a folder that can be written to, or set {nameof(CommonImageActionSettings.UseDiskCache)} to false.");
            }
        }

        private void LogError(Exception ex, string message)
        {
            if (_logger != null)
            {
                _logger.LogWarning(ex, message);
            }
            else
            {
                Console.WriteLine($"{message} {ex}");
            }
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

        /// <summary>
        /// Has the browser told us it already has this exact image?
        /// </summary>
        private static bool IsNotModified(HttpRequest request, string etag, DateTimeOffset? lastModified)
        {
            //the browser sends back the etag it was given, this is the check that counts when present
            var ifNoneMatch = request.Headers["If-None-Match"].ToString();
            if (string.IsNullOrEmpty(ifNoneMatch) == false)
            {
                foreach (var candidate in ifNoneMatch.Split(','))
                {
                    var value = candidate.Trim();
                    if (value == "*")
                    {
                        return true;
                    }

                    //a weak etag (W/"...") matches the same as a strong one for this purpose
                    if (value.StartsWith("W/", StringComparison.Ordinal))
                    {
                        value = value.Substring(2);
                    }

                    if (string.Equals(value, etag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            //otherwise fall back to the date the browser got its copy
            var ifModifiedSince = request.Headers["If-Modified-Since"].ToString();
            if (lastModified.HasValue
                && string.IsNullOrEmpty(ifModifiedSince) == false
                && DateTimeOffset.TryParseExact(ifModifiedSince, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since))
            {
                return lastModified.Value <= since;
            }

            return false;
        }

        private void SendNotModified(HttpContext context, string etag, DateTimeOffset? lastModified)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            SetCachingHeaders(context, etag, lastModified);
            _options.OnPrepareResponse?.Invoke(context);
        }

        private void SetCachingHeaders(HttpContext context, string etag, DateTimeOffset? lastModified)
        {
            context.Response.Headers["ETag"] = etag;

            if (lastModified.HasValue)
            {
                context.Response.Headers["Last-Modified"] = lastModified.Value.ToString("r", CultureInfo.InvariantCulture);
            }

            if (_options.BrowserCacheMaxAge.HasValue)
            {
                var seconds = (long)Math.Max(0, _options.BrowserCacheMaxAge.Value.TotalSeconds);
                context.Response.Headers["Cache-Control"] = $"public, max-age={seconds}";
            }
        }

        /// <summary>
        /// The name of the cache file for a request. It is built from the path and the image actions
        /// that will be applied rather than the raw url, so the same image is cached once however the
        /// url is written, and query parameters that mean nothing do not create new cache files.
        /// It is hashed so the name is always short enough to be a valid file name.
        /// </summary>
        /// <param name="sourceVersion">Something that changes when the original file changes.</param>
        public static string GetCacheFileName(string requestPath, ImageActions imageActions, string sourceVersion = null)
        {
            var key = new StringBuilder(requestPath);
            key.Append('|').Append(sourceVersion);

            //a new version of the library can draw the same request differently, as can a change
            //to the default jpeg quality, so neither should be answered from what was made before
            key.Append('|').Append(_libraryVersion);
            key.Append('|').Append(ImageProcessor.JpegQuality);

            foreach (var property in _imageActionProperties)
            {
                var value = property.GetValue(imageActions);
                string text;
                if (value == null)
                {
                    text = string.Empty;
                }
                else if (value is byte[] bytes)
                {
                    //for example the watermark image, its hash stands in for it
                    text = GetHash(bytes);
                }
                else if (value is IFormattable formattable)
                {
                    text = formattable.ToString(null, CultureInfo.InvariantCulture);
                }
                else
                {
                    text = value.ToString();
                }

                key.Append('|').Append(property.Name).Append('=').Append(Uri.EscapeDataString(text));
            }

            return GetHash(Encoding.UTF8.GetBytes(key.ToString()));
        }

        private static string GetHash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                return ByteArrayToHexString(sha256.ComputeHash(data));
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

        /// <summary>
        /// Read a number from the url, only accepting it when it is inside the allowed range.
        /// </summary>
        private static bool TryGetNumber(NameValueCollection query, string name, string shortName, double min, double max, out double value)
        {
            var text = query[name] ?? query[shortName];
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && double.IsNaN(value) == false
                && value >= min
                && value <= max)
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryGetBoolean(NameValueCollection query, string name, string shortName, out bool value)
        {
            return Boolean.TryParse(query[name] ?? query[shortName], out value);
        }

        /// <summary>
        /// Read a named value from the url, ignoring numbers that are not one of the names.
        /// </summary>
        private static bool TryGetEnum<T>(string text, out T value) where T : struct
        {
            return Enum.TryParse<T>(text, true, out value) && Enum.IsDefined(typeof(T), value);
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

            //encoding
            if (TryGetNumber(query, "quality", "q", 1, 100, out var quality))
            {
                imageActions.Quality = (int)quality;
            }

            if (TryGetBoolean(query, "metadata", "md", out var keepMetadata))
            {
                imageActions.KeepMetadata = keepMetadata;
            }

            //which part of the image is used and where it is placed
            if (TryGetEnum<ImageAnchor>(query["anchor"] ?? query["a"], out var anchor))
            {
                imageActions.Anchor = anchor;
            }

            if (TryGetNumber(query, "focalX", "fx", 0, 1, out var focalX))
            {
                imageActions.FocalPointX = focalX;
            }

            if (TryGetNumber(query, "focalY", "fy", 0, 1, out var focalY))
            {
                imageActions.FocalPointY = focalY;
            }

            if (TryGetBoolean(query, "upscale", "u", out var allowUpscale))
            {
                imageActions.AllowUpscale = allowUpscale;
            }

            var resamplerString = query["resampler"] ?? query["rs"];
            if (string.Equals(resamplerString, "nearest", StringComparison.OrdinalIgnoreCase))
            {
                imageActions.Resampler = ImageResampler.NearestNeighbor;
            }
            else if (TryGetEnum<ImageResampler>(resamplerString, out var resampler))
            {
                imageActions.Resampler = resampler;
            }

            //crop is four whole numbers: left, top, width, height
            var cropString = query["crop"];
            if (cropString != null)
            {
                var parts = cropString.Split(',');
                if (parts.Length == 4
                    && int.TryParse(parts[0], out var cropX) && cropX >= 0
                    && int.TryParse(parts[1], out var cropY) && cropY >= 0
                    && int.TryParse(parts[2], out var cropWidth) && cropWidth > 0
                    && int.TryParse(parts[3], out var cropHeight) && cropHeight > 0)
                {
                    imageActions.CropX = cropX;
                    imageActions.CropY = cropY;
                    imageActions.CropWidth = cropWidth;
                    imageActions.CropHeight = cropHeight;
                }
            }

            if (TryGetNumber(query, "rotate", "r", -360000, 360000, out var rotate))
            {
                imageActions.Rotate = rotate;
            }

            //flip is h, v or both
            var flipString = (query["flip"] ?? query["fl"])?.ToLowerInvariant();
            if (flipString == "h" || flipString == "horizontal" || flipString == "both" || flipString == "hv" || flipString == "vh")
            {
                imageActions.FlipHorizontal = true;
            }

            if (flipString == "v" || flipString == "vertical" || flipString == "both" || flipString == "hv" || flipString == "vh")
            {
                imageActions.FlipVertical = true;
            }

            var backgroundString = query["background"] ?? query["bg"];
            if (backgroundString != null)
            {
                imageActions.BackgroundColor = backgroundString;
            }

            //text layout
            var fontString = query["font"] ?? query["fn"];
            if (fontString != null)
            {
                imageActions.FontFamily = fontString;
            }

            if (TryGetNumber(query, "fontSize", "fs", 1, CommonImageActionSettings.MaxUrlHeight, out var fontSize))
            {
                imageActions.FontSize = (int)fontSize;
            }

            if (TryGetEnum<ImageAnchor>(query["textAnchor"] ?? query["ta"], out var textAnchor))
            {
                imageActions.TextAnchor = textAnchor;
            }

            if (TryGetBoolean(query, "wrap", "tw", out var textWrap))
            {
                imageActions.TextWrap = textWrap;
            }

            //filters, each limited to a range that is useful and can not be used to tie up the server
            if (TryGetBoolean(query, "sepia", "se", out var isSepia))
            {
                imageActions.IsSepia = isSepia;
            }

            if (TryGetBoolean(query, "invert", "inv", out var isInverted))
            {
                imageActions.IsInverted = isInverted;
            }

            if (TryGetNumber(query, "brightness", "br", 0, 10, out var brightness))
            {
                imageActions.Brightness = brightness;
            }

            if (TryGetNumber(query, "contrast", "co", 0, 10, out var contrast))
            {
                imageActions.Contrast = contrast;
            }

            if (TryGetNumber(query, "saturation", "sa", 0, 10, out var saturation))
            {
                imageActions.Saturation = saturation;
            }

            if (TryGetNumber(query, "blur", "bl", 0, 100, out var blur))
            {
                imageActions.Blur = blur;
            }

            if (TryGetNumber(query, "sharpen", "sh", 0, 10, out var sharpen))
            {
                imageActions.Sharpen = sharpen;
            }

            if (TryGetNumber(query, "pixelate", "px", 2, CommonImageActionSettings.MaxUrlWidth, out var pixelate))
            {
                imageActions.Pixelate = (int)pixelate;
            }

            if (TryGetNumber(query, "vignette", "v", 0, 1, out var vignette))
            {
                imageActions.Vignette = vignette;
            }

            return imageActions;
        }

        private string GetImageExtension(string path)
        {
            return CommonImageActionSettings.ValidImageExtensions.FirstOrDefault(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }
    }
}
