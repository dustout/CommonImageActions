using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using System.Net;

namespace CommonImageActions.AspNetCore.Tests
{
    public class CommonImageActionsMiddlewareFeatureTests
    {
        private const string SigningKey = "a key that only the server knows";

        [Fact]
        public void ConvertQueryString_ShouldReadTheNewParameters_ByTheirLongNames()
        {
            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions(
                "?quality=80&metadata=true&anchor=topleft&focalX=0.25&focalY=0.75&upscale=false&resampler=bilinear"
                + "&crop=10,20,300,400&rotate=90&flip=both&background=ffffff"
                + "&font=Georgia&fontSize=24&textAnchor=bottom&wrap=true"
                + "&sepia=true&invert=true&brightness=1.2&contrast=1.3&saturation=0.5&blur=4.5&sharpen=1&pixelate=8&vignette=0.6");

            Assert.Equal(80, actions.Quality);
            Assert.True(actions.KeepMetadata);
            Assert.Equal(ImageAnchor.TopLeft, actions.Anchor);
            Assert.Equal(0.25, actions.FocalPointX);
            Assert.Equal(0.75, actions.FocalPointY);
            Assert.False(actions.AllowUpscale);
            Assert.Equal(ImageResampler.Bilinear, actions.Resampler);
            Assert.Equal((10, 20, 300, 400), (actions.CropX, actions.CropY, actions.CropWidth, actions.CropHeight));
            Assert.Equal(90, actions.Rotate);
            Assert.True(actions.FlipHorizontal);
            Assert.True(actions.FlipVertical);
            Assert.Equal("ffffff", actions.BackgroundColor);
            Assert.Equal("Georgia", actions.FontFamily);
            Assert.Equal(24, actions.FontSize);
            Assert.Equal(ImageAnchor.Bottom, actions.TextAnchor);
            Assert.True(actions.TextWrap);
            Assert.True(actions.IsSepia);
            Assert.True(actions.IsInverted);
            Assert.Equal(1.2, actions.Brightness);
            Assert.Equal(1.3, actions.Contrast);
            Assert.Equal(0.5, actions.Saturation);
            Assert.Equal(4.5, actions.Blur);
            Assert.Equal(1, actions.Sharpen);
            Assert.Equal(8, actions.Pixelate);
            Assert.Equal(0.6, actions.Vignette);
        }

        [Fact]
        public void ConvertQueryString_ShouldReadTheNewParameters_ByTheirShortNames()
        {
            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions(
                "?q=80&md=true&a=right&fx=0.25&fy=0.75&u=false&rs=nearest&r=-45&fl=h&bg=000000"
                + "&fn=Arial&fs=12&ta=topright&tw=true&se=true&inv=true&br=2&co=2&sa=2&bl=1&sh=2&px=4&v=1");

            Assert.Equal(80, actions.Quality);
            Assert.True(actions.KeepMetadata);
            Assert.Equal(ImageAnchor.Right, actions.Anchor);
            Assert.Equal(0.25, actions.FocalPointX);
            Assert.Equal(0.75, actions.FocalPointY);
            Assert.False(actions.AllowUpscale);
            Assert.Equal(ImageResampler.NearestNeighbor, actions.Resampler);
            Assert.Equal(-45, actions.Rotate);
            Assert.True(actions.FlipHorizontal);
            Assert.Null(actions.FlipVertical);
            Assert.Equal("000000", actions.BackgroundColor);
            Assert.Equal("Arial", actions.FontFamily);
            Assert.Equal(12, actions.FontSize);
            Assert.Equal(ImageAnchor.TopRight, actions.TextAnchor);
            Assert.True(actions.TextWrap);
            Assert.True(actions.IsSepia);
            Assert.True(actions.IsInverted);
            Assert.Equal(2, actions.Brightness);
            Assert.Equal(2, actions.Contrast);
            Assert.Equal(2, actions.Saturation);
            Assert.Equal(1, actions.Blur);
            Assert.Equal(2, actions.Sharpen);
            Assert.Equal(4, actions.Pixelate);
            Assert.Equal(1, actions.Vignette);
        }

        [Fact]
        public void ConvertQueryString_ShouldIgnoreValuesOutsideOfTheirRange()
        {
            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions(
                "?quality=0&anchor=99&focalX=2&resampler=best&crop=1,2,3&rotate=NaN&flip=sideways"
                + "&fontSize=0&brightness=-1&contrast=50&blur=100000&sharpen=abc&pixelate=1&vignette=5");

            Assert.Null(actions.Quality);
            Assert.Null(actions.Anchor);
            Assert.Null(actions.FocalPointX);
            Assert.Null(actions.Resampler);
            Assert.False(actions.HasCrop());
            Assert.Null(actions.Rotate);
            Assert.Null(actions.FlipHorizontal);
            Assert.Null(actions.FontSize);
            Assert.Null(actions.Brightness);
            Assert.Null(actions.Contrast);
            Assert.Null(actions.Blur);
            Assert.Null(actions.Sharpen);
            Assert.Null(actions.Pixelate);
            Assert.Null(actions.Vignette);
        }

        [Fact]
        public void GetCacheFileName_ShouldChange_WhenAnyPropertyChanges()
        {
            // Arrange
            var baseline = CommonImageActionsMiddleware.GetCacheFileName("/test/photo.jpg", new ImageActions());
            var properties = typeof(ImageActions).GetProperties().Where(x => x.CanRead && x.CanWrite).ToList();

            // Act & Assert - a property that did not change the name would serve the wrong cached image
            Assert.NotEmpty(properties);
            foreach (var property in properties)
            {
                var actions = new ImageActions();
                property.SetValue(actions, TestHelpers.CreateValue(property.PropertyType));

                var name = CommonImageActionsMiddleware.GetCacheFileName("/test/photo.jpg", actions);
                Assert.True(name != baseline, $"{property.Name} does not change the cache file name.");
            }

            //the file itself changing has to change it too
            Assert.NotEqual(baseline, CommonImageActionsMiddleware.GetCacheFileName("/test/photo.jpg", new ImageActions(), "changed"));
            Assert.Equal(baseline, CommonImageActionsMiddleware.GetCacheFileName("/test/photo.jpg", new ImageActions()));
        }

        [Fact]
        public async Task ShouldApplyNewParametersFromTheUrl()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();

            //the photo is 400x300, a quarter turn makes it 300x400
            var rotated = await client.GetByteArrayAsync("/test/photo.jpg?rotate=90&w=150");
            var cropped = await client.GetByteArrayAsync("/test/photo.jpg?crop=0,0,100,50");
            var notEnlarged = await client.GetByteArrayAsync("/test/photo.jpg?w=2000&upscale=false");
            var inverted = await client.GetByteArrayAsync("/test/photo.jpg?invert=true&f=png");

            Assert.Equal((150, 200), Size(rotated));
            Assert.Equal((100, 50), Size(cropped));
            Assert.Equal((TestSite.PhotoWidth, TestSite.PhotoHeight), Size(notEnlarged));

            //the photo is red, which is cyan when inverted
            TestHelpers.AssertColor(new SKColor(0, 255, 255), TestHelpers.GetPixel(inverted, 10, 10), tolerance: 12);
        }

        [Fact]
        public async Task ShouldAnswerNotModified_WhenTheBrowserAlreadyHasTheImage()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();

            // first visit
            var first = await client.GetAsync("/test/photo.jpg?w=50");
            var etag = first.Headers.ETag;

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.NotNull(etag);
            Assert.NotNull(first.Content.Headers.LastModified);
            Assert.Equal((await first.Content.ReadAsByteArrayAsync()).Length, first.Content.Headers.ContentLength);

            // second visit, the browser sends back the etag it was given
            var request = new HttpRequestMessage(HttpMethod.Get, "/test/photo.jpg?w=50");
            request.Headers.IfNoneMatch.Add(etag!);
            var second = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
            Assert.Equal(etag, second.Headers.ETag);
            Assert.Empty(await second.Content.ReadAsByteArrayAsync());

            // or only the date it got its copy
            var byDate = new HttpRequestMessage(HttpMethod.Get, "/test/photo.jpg?w=50");
            byDate.Headers.IfModifiedSince = first.Content.Headers.LastModified;
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(byDate)).StatusCode);

            // a different image is not the same thing
            var other = new HttpRequestMessage(HttpMethod.Get, "/test/photo.jpg?w=60");
            other.Headers.IfNoneMatch.Add(etag!);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(other)).StatusCode);
        }

        [Fact]
        public async Task ShouldSendTheImageAgain_WhenTheFileHasChanged()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                UseDiskCache = true,
                DiskCacheLocation = site.CacheLocation
            });
            var client = host.GetTestClient();
            var photoPath = Path.Combine(site.WebRoot, "test", "photo.jpg");

            var first = await client.GetAsync("/test/photo.jpg?w=50&f=png");
            var firstBytes = await first.Content.ReadAsByteArrayAsync();

            // replace the photo with a blue one
            File.WriteAllBytes(photoPath, TestHelpers.CreateImage(TestSite.PhotoWidth, TestSite.PhotoHeight, SKEncodedImageFormat.Jpeg, TestHelpers.Blue));
            File.SetLastWriteTimeUtc(photoPath, DateTime.UtcNow.AddMinutes(5));

            var request = new HttpRequestMessage(HttpMethod.Get, "/test/photo.jpg?w=50&f=png");
            request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
            var second = await client.SendAsync(request);
            var secondBytes = await second.Content.ReadAsByteArrayAsync();

            // neither the browser's copy nor the disk cache is used for the new file
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.NotEqual(first.Headers.ETag, second.Headers.ETag);
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(firstBytes, 10, 10), tolerance: 12);
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(secondBytes, 10, 10), tolerance: 12);
        }

        [Fact]
        public async Task ShouldOnlySendCacheControl_WhenBrowserCacheMaxAgeIsSet()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(
                new CommonImageActionSettings() { PathToWatch = "/test" },
                new CommonImageActionSettings() { PathToWatch = "/virtual", IsVirtual = true, BrowserCacheMaxAge = TimeSpan.FromDays(7) });
            var client = host.GetTestClient();

            var withoutMaxAge = await client.GetAsync("/test/photo.jpg?w=50");
            var withMaxAge = await client.GetAsync("/virtual/x.png?w=50");

            Assert.Null(withoutMaxAge.Headers.CacheControl);
            Assert.Equal(TimeSpan.FromDays(7), withMaxAge.Headers.CacheControl?.MaxAge);
            Assert.True(withMaxAge.Headers.CacheControl?.Public);
            Assert.NotNull(withMaxAge.Headers.ETag);
        }

        [Fact]
        public async Task ShouldAnswerAHeadRequestWithoutTheImage()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();

            var get = await client.GetAsync("/test/photo.jpg?w=50");
            var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/test/photo.jpg?w=50"));

            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Equal("image/jpeg", head.Content.Headers.ContentType?.ToString());
            Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task ShouldRequireAValidSignature_WhenASigningKeyIsSet()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test", SigningKey = SigningKey });
            var client = host.GetTestClient();

            var signedUrl = CommonImageActionsUrlSigner.SignUrl("/test/photo.jpg?w=50", SigningKey);
            var signedUrlWithSpace = CommonImageActionsUrlSigner.SignUrl("/test/my%20photo.jpg?w=50", SigningKey);

            var unsigned = await client.GetAsync("/test/photo.jpg?w=50");
            var signed = await client.GetAsync(signedUrl);
            var withSpace = await client.GetAsync(signedUrlWithSpace);
            var changedSize = await client.GetAsync(signedUrl.Replace("w=50", "w=500"));
            var changedFile = await client.GetAsync(signedUrl.Replace("photo.jpg", "my%20photo.jpg"));
            var wrongKey = await client.GetAsync(CommonImageActionsUrlSigner.SignUrl("/test/photo.jpg?w=50", "some other key"));
            var extraParameter = await client.GetAsync(signedUrl + "&h=5000");

            Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            Assert.Equal(50, TestHelpers.GetImageInfo(await signed.Content.ReadAsByteArrayAsync()).Width);
            Assert.Equal(HttpStatusCode.OK, withSpace.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, changedSize.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, changedFile.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, extraParameter.StatusCode);
        }

        [Fact]
        public async Task ShouldNotRequireASignature_WhenTheUrlAsksForNothing()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/virtual", IsVirtual = true, SigningKey = SigningKey });

            var response = await host.GetTestClient().GetAsync("/virtual/x.png");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public void UrlSigner_ShouldSignAndCheckUrls()
        {
            var signature = CommonImageActionsUrlSigner.CreateSignature("/test/photo.jpg?w=50", SigningKey);
            var signedUrl = CommonImageActionsUrlSigner.SignUrl("/test/photo.jpg?w=50", SigningKey);
            var signedWithoutQuery = CommonImageActionsUrlSigner.SignUrl("/test/photo.jpg", SigningKey);

            Assert.Equal($"/test/photo.jpg?w=50&sig={signature}", signedUrl);
            Assert.StartsWith("/test/photo.jpg?sig=", signedWithoutQuery);

            Assert.True(CommonImageActionsUrlSigner.IsValid("/test/photo.jpg", $"?w=50&sig={signature}", SigningKey));
            Assert.True(CommonImageActionsUrlSigner.IsValid("/test/photo.jpg", $"?w=50&sig={signature.ToUpperInvariant()}", SigningKey));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/test/photo.jpg", $"?w=51&sig={signature}", SigningKey));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/test/other.jpg", $"?w=50&sig={signature}", SigningKey));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/test/photo.jpg", "?w=50", SigningKey));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/test/photo.jpg", "?w=50&sig=", SigningKey));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/test/photo.jpg", $"?sig={signature}&w=50", SigningKey));
        }

        [Fact]
        public async Task ShouldMakeTheImageAgain_WhenTheCachedFileIsOlderThanDiskCacheMaxAge()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                UseDiskCache = true,
                DiskCacheLocation = site.CacheLocation,
                DiskCacheMaxAge = TimeSpan.FromHours(1)
            });
            var client = host.GetTestClient();

            var first = await client.GetByteArrayAsync("/test/photo.jpg?w=50");
            var cachedFile = Directory.GetFiles(site.CacheLocation).Single();

            // mark what is in the cache so it can be told apart from a newly made image
            var marker = TestHelpers.CreateImage(5, 5, SKEncodedImageFormat.Png, TestHelpers.Blue);
            File.WriteAllBytes(cachedFile, marker);

            // still fresh, so the cached file is what is sent
            var fresh = await client.GetByteArrayAsync("/test/photo.jpg?w=50");

            // two hours old, so it is ignored and replaced
            File.SetLastWriteTimeUtc(cachedFile, DateTime.UtcNow.AddHours(-2));
            var expired = await client.GetByteArrayAsync("/test/photo.jpg?w=50");

            Assert.Equal(marker, fresh);
            Assert.Equal(first, expired);
            Assert.Equal(first, File.ReadAllBytes(cachedFile));
        }

        [Fact]
        public async Task ShouldDeleteTheOldestCachedFiles_WhenTheCacheIsOverDiskCacheMaxSizeBytes()
        {
            using var site = new TestSite();

            //room for about three of these images
            var oneImage = await ImageProcessor.ProcessImageAsync(File.ReadAllBytes(Path.Combine(site.WebRoot, "test", "photo.jpg")), new ImageActions() { Width = 200, Format = SKEncodedImageFormat.Png });
            var maxBytes = oneImage.Length * 3;

            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                UseDiskCache = true,
                DiskCacheLocation = site.CacheLocation,
                DiskCacheMaxSizeBytes = maxBytes
            });
            var client = host.GetTestClient();

            for (var width = 200; width < 212; width++)
            {
                await client.GetByteArrayAsync($"/test/photo.jpg?w={width}&f=png");
            }

            // the clean up runs in the background, so give it a moment
            long cacheBytes = 0;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                cacheBytes = new DirectoryInfo(site.CacheLocation).GetFiles().Sum(x => x.Length);
                if (cacheBytes <= maxBytes)
                {
                    break;
                }

                await Task.Delay(50);
            }

            Assert.True(cacheBytes <= maxBytes, $"The cache is {cacheBytes} bytes and the limit is {maxBytes} bytes.");
            Assert.NotEmpty(Directory.GetFiles(site.CacheLocation));
        }

        [Fact]
        public async Task ShouldCallTheHooks()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",

                //never let an image be wider than 20, whatever the url asks for
                OnActionsParsed = (context, actions) =>
                {
                    if (actions.Width > 20)
                    {
                        actions.Width = 20;
                    }
                    return Task.CompletedTask;
                },
                OnPrepareResponse = context => context.Response.Headers["X-Test"] = "prepared"
            });

            var response = await host.GetTestClient().GetAsync("/test/photo.jpg?w=300");

            Assert.Equal(20, TestHelpers.GetImageInfo(await response.Content.ReadAsByteArrayAsync()).Width);
            Assert.Equal("prepared", response.Headers.GetValues("X-Test").Single());
        }

        [Fact]
        public async Task ShouldUseSettingsRegisteredWithAddCommonImageActions()
        {
            using var host = await new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddCommonImageActions(settings =>
                    {
                        settings.PathToWatch = "/virtual";
                        settings.IsVirtual = true;
                        settings.DefaultImageActions = new ImageActions() { Width = 40, Height = 30 };
                    });
                })
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .Configure(app =>
                        {
                            app.UseCommonImageActions();
                        });
                })
                .StartAsync();
            var client = host.GetTestClient();

            var watched = await client.GetAsync("/virtual/x.png");
            var notWatched = await client.GetAsync("/other/x.png");

            Assert.Equal(HttpStatusCode.OK, watched.StatusCode);
            Assert.Equal((40, 30), Size(await watched.Content.ReadAsByteArrayAsync()));
            Assert.Equal(HttpStatusCode.NotFound, notWatched.StatusCode);
        }

        private static (int Width, int Height) Size(byte[] image)
        {
            var info = TestHelpers.GetImageInfo(image);
            return (info.Width, info.Height);
        }
    }
}
