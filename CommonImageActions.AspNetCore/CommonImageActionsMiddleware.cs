using CommonImageActions.Core;
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
using System.Runtime.InteropServices;
using System.Text;
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
            if (context.Request.Path.StartsWithSegments(_options.PathToWatch))
            {
                //conver url into a Uri
                var url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                var uri = new Uri(url);

                //if there are no paramters then let the normal flow deal with it
                var query = HttpUtility.ParseQueryString(uri.Query);
                if (query.Count == 0)
                {
                    //do nothing, let the normal flow deal with it
                    await _next(context);
                    return;
                }

                //get file information
                var segments = uri.Segments.Skip(1).Select(x => x.TrimEnd('/')).ToArray();
                var imageFileRelativePath = Path.Combine(segments);
                var imageFilePath = Path.Combine(_env.ContentRootPath, "wwwroot", imageFileRelativePath);
                var imageExtension = GetImageExtension(imageFilePath);

                //if image extension not found then let the normal flow deal with it
                if (imageExtension == null)
                {
                    //do nothing, let the normal flow deal with it
                    await _next(context);
                    return;
                }

                //if file does not exist then let normal flow deal with it
                if (!File.Exists(imageFilePath))
                {
                    await _next(context);
                    return;
                }

                //initialize image actions
                var imageActions = new ImageActions();

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

                //if there are no actions to perform then let the normal flow deal with it
                if (imageActions.HasAnyActions() == false)
                {
                    await _next(context);
                    return;
                }

                //check if is pdf
                var isPdf = string.Equals(imageExtension, ".PDF", StringComparison.OrdinalIgnoreCase);

                using var inputStream = File.OpenRead(imageFilePath);
                var resultingData = await ImageProcessor.ProcessImageAsync(inputStream, imageActions, isPdf);

                //if there is nothing to return then throw exception, this should never happen
                if (resultingData == null)
                {
                    throw new NotImplementedException($"{imageExtension} not supported");
                }

                //send set response type
                if (imageActions.Format.HasValue)
                {
                    context.Response.ContentType = $"image/{imageActions.Format.Value.ToString().ToLower()}";
                }
                else
                {
                    context.Response.ContentType = "image/png";
                }

                //send the resulting data
                await context.Response.Body.WriteAsync(resultingData, 0, resultingData.Length);
                return;
            }
            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }

        private string GetImageExtension(string path)
        {
            return CommonImageActionSettings.ValidImageExtensions.FirstOrDefault(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }
    }
}
