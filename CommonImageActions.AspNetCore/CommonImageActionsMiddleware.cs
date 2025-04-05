using CommonImageActions.Core;
using CommonImageActions.Pdf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Graphics.Skia;
using PDFiumCore;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Web;

namespace CommonImageActions.AspNetCore
{
    public class CommonImageActionsMiddleware
    {

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
            if(context.Request.Path.HasValue == false)
            {
                await _next(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments(_options.PathToWatch))
            {
                //check if request is for a supported image
                var imageExtension = GetImageExtension(context.Request.Path);
                if(imageExtension == null)
                {
                    await _next(context);
                    return;
                }

                //conver url into a Uri
                var requestAndQuery = $"{context.Request.Path}{context.Request.QueryString}";
                var url = $"{context.Request.Scheme}://{context.Request.Host}{requestAndQuery}";
                var uri = new Uri(url);

                //get file information
                var segments = uri.Segments.Skip(1).Select(x => x.TrimEnd('/')).ToArray();
                var imageFileRelativePath = Path.Combine(segments);
                var webRootPath = Path.Combine(_env.ContentRootPath, "wwwroot");
                var imageFilePath = Path.Combine(webRootPath, imageFileRelativePath);
                var isPdf = string.Equals(imageExtension, ".pdf", StringComparison.OrdinalIgnoreCase);
                var isRemoteServer = !string.IsNullOrEmpty(_options.RemoteFileServerUrl);
                var isVirtual = _options.IsVirtual;

                //convert query string into image actions
                var imageActions = ConvertQueryStringToImageActions(uri.Query, _options.DefaultImageActions);
                byte[] imageData = null;

                if (_options.UseDiskCache)
                {
                    var diskCacheLocation = _options.DiskCacheLocation;
                    if(string.IsNullOrEmpty(diskCacheLocation))
                    {
                        diskCacheLocation = CommonImageActionSettings.DefaultDiskCacheLocation;
                    }

                    //check to see if file is in the cache, if it does then return it
                    var cachedFileName = ByteArrayToHexString(Encoding.UTF8.GetBytes(requestAndQuery));
                    var cachedFilePath = Path.Combine(diskCacheLocation, cachedFileName);
                    try
                    {
                        if (File.Exists(cachedFilePath))
                        {
                            //if the file exists then return it
                            if (imageActions.Format.HasValue)
                            {
                                context.Response.ContentType = $"image/{imageActions.Format.Value.ToString().ToLower()}";
                            }
                            else
                            {
                                context.Response.ContentType = $"image/{imageExtension.Replace(".", string.Empty)}";
                            }
                           
                            using (var cachedFileStream = File.OpenRead(cachedFilePath))
                            { 
                                await cachedFileStream.CopyToAsync(context.Response.Body);
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting cached file {cachedFilePath}. {ex.ToString()}");
                    }
                }

                if (isRemoteServer)
                {
                    //get the server path by removing the path to watch
                    var serverPath = context.Request.Path.Value.Replace(_options.PathToWatch, string.Empty).TrimStart('/');

                    //clean the remote url so if there is a slash then we remove it 
                    var cleanedRemoteUrl = _options.RemoteFileServerUrl.TrimEnd('/');

                    //combine them as a request to the server
                    var imageRemoteUrlPath = new Uri($"{cleanedRemoteUrl}/{serverPath}");

                    //get the image from the remote server
                    var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(imageRemoteUrlPath);
                    var responseData = await response.Content.ReadAsByteArrayAsync();

                    //if failure then forward the failure
                    if (response.IsSuccessStatusCode == false)
                    {
                        context.Response.StatusCode = (int)response.StatusCode;
                        await context.Response.Body.WriteAsync(responseData);
                        return;
                    }

                    //if no actions then just send the raw file
                    if (imageActions.HasAnyActions() == false)
                    {
                        context.Response.StatusCode = (int)response.StatusCode;
                        await context.Response.Body.WriteAsync(responseData);
                        return;
                    }

                    //get the image data and process it
                    if (isPdf)
                    {
                        imageData = PdfProcessor.ProcessPdf(responseData, imageActions);
                    }
                    else
                    {
                        imageData = ImageProcessor.ProcessImage(responseData, imageActions);
                    }
                }
                else if (isVirtual)
                {
                    imageData = await ImageProcessor.ProcessVirtualImageAsync(imageActions);
                }
                else
                {
                    //check to see if the request has been modified (for example if someone has requested root)
                    if (imageFilePath.StartsWith(webRootPath) == false)
                    {
                        throw new UnauthorizedAccessException();
                    }

                    //if file does not exist then let normal flow deal with it
                    if (!File.Exists(imageFilePath))
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

                //set response type
                if (imageActions.Format.HasValue)
                {
                    context.Response.ContentType = $"image/{imageActions.Format.Value.ToString().ToLower()}";
                }
                else
                {
                    context.Response.ContentType = "image/png";
                }

                //send the resulting data
                await context.Response.Body.WriteAsync(imageData, 0, imageData.Length);

                //write to disk cache if enabled
                if (_options.UseDiskCache)
                {
                    var diskCacheLocation = _options.DiskCacheLocation;
                    if (string.IsNullOrEmpty(diskCacheLocation))
                    {
                        diskCacheLocation = CommonImageActionSettings.DefaultDiskCacheLocation;
                    }

                    var cachedFileName = ByteArrayToHexString(Encoding.UTF8.GetBytes(requestAndQuery));
                    var cachedFilePath = Path.Combine(diskCacheLocation, cachedFileName);
                    try
                    {
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

        private static byte[] HexStringToByteArray(string Hex)
        {
            byte[] Bytes = new byte[Hex.Length / 2];
            int[] HexValue = new int[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05,
       0x06, 0x07, 0x08, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
       0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };

            for (int x = 0, i = 0; i < Hex.Length; i += 2, x += 1)
            {
                Bytes[x] = (byte)(HexValue[Char.ToUpper(Hex[i + 0]) - '0'] << 4 |
                                  HexValue[Char.ToUpper(Hex[i + 1]) - '0']);
            }

            return Bytes;
        }

        public static ImageActions ConvertQueryStringToImageActions(string queryString, ImageActions defaultImageActions = null)
        {
            //initialize image actions
            var imageActions = new ImageActions(defaultImageActions);

            //if there are no paramters then let the normal flow deal with it
            var query = HttpUtility.ParseQueryString(queryString);
            if (query.Count == 0)
            {
                return imageActions;
            }

            //convert url parameters into an image action
            var widthString = query["width"] ?? query["w"];
            if (int.TryParse(widthString, out int width))
            {
                imageActions.Width = width;
            }

            var heightString = query["height"] ?? query["h"];
            if (int.TryParse(heightString, out int height))
            {
                imageActions.Height = height;
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

            var pdfPasswordString = query["password"] ?? query["pw"];
            imageActions.PdfPassword = pdfPasswordString;

            var textString = query["text"] ?? query["t"];
            imageActions.Text = textString;

            var asInitialsString = query["asInitials"] ?? query["in"];
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
            imageActions.TextColor = textColorString;

            var virtualImageColorString = query["virtualColor"] ?? query["c"];
            imageActions.ImageColor = virtualImageColorString;
            
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
