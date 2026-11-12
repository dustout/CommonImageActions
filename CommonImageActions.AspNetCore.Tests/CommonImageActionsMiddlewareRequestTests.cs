using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using Microsoft.AspNetCore.TestHost;
using SkiaSharp;
using System.Net;

namespace CommonImageActions.AspNetCore.Tests
{
    public class CommonImageActionsMiddlewareRequestTests
    {
        [Fact]
        public async Task ShouldProcessAFile_WhenItsNameHasASpace()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            var response = await host.GetTestClient().GetAsync("/test/my%20photo.jpg?w=50");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var info = TestHelpers.GetImageInfo(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(50, info.Width);
        }

        [Theory]
        [InlineData("/test/..%5C..%5Csecret.jpg?w=50")]
        [InlineData("/test/..%5Csecret.jpg?w=50")]
        [InlineData("/test/%5C%5Clocalhost%5Cshare%5Csecret.jpg?w=50")]
        public async Task ShouldNotServeAFile_WhenThePathLeavesTheWebRoot(string url)
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            var response = await host.GetTestClient().GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public void GetLocalFilePath_ShouldOnlyReturnPathsInsideTheWebRoot()
        {
            using var site = new TestSite();

            var inside = CommonImageActionsMiddleware.GetLocalFilePath(site.WebRoot, "/test/my photo.jpg");
            Assert.Equal(Path.Combine(site.WebRoot, "test", "my photo.jpg"), inside);

            Assert.Null(CommonImageActionsMiddleware.GetLocalFilePath(site.WebRoot, "/test/../../secret.jpg"));
            Assert.Null(CommonImageActionsMiddleware.GetLocalFilePath(site.WebRoot, "/../secret.jpg"));

            //a backslash is only a directory separator on windows, elsewhere it is part of the file name
            if (OperatingSystem.IsWindows())
            {
                Assert.Null(CommonImageActionsMiddleware.GetLocalFilePath(site.WebRoot, "/test/..\\..\\secret.jpg"));
                Assert.Null(CommonImageActionsMiddleware.GetLocalFilePath(site.WebRoot, "/C:/Windows/secret.jpg"));
            }
        }

        [Fact]
        public async Task ShouldOnlyHandleGetAndHeadRequests()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/virtual", IsVirtual = true });
            var client = host.GetTestClient();

            var get = await client.GetAsync("/virtual/test.png");
            var post = await client.PostAsync("/virtual/test.png", new StringContent(string.Empty));

            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        }

        [Fact]
        public async Task ShouldIgnoreASizeThatIsNotPositive()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            var response = await host.GetTestClient().GetAsync("/test/photo.jpg?w=0&h=-5&g=true");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var info = TestHelpers.GetImageInfo(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal((TestSite.PhotoWidth, TestSite.PhotoHeight), (info.Width, info.Height));
        }

        [Fact]
        public async Task ShouldReturnPng_WhenTheRequestedFormatCanNotBeWritten()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            var response = await host.GetTestClient().GetAsync("/test/photo.jpg?w=50&f=gif");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.ToString());
        }

        [Fact]
        public async Task ShouldSendTheSameContentType_WhenTheImageComesFromTheCache()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                UseDiskCache = true,
                DiskCacheLocation = site.CacheLocation
            });
            var client = host.GetTestClient();

            var first = await client.GetAsync("/test/photo.jpg?w=50");
            var firstBytes = await first.Content.ReadAsByteArrayAsync();
            Assert.Single(Directory.GetFiles(site.CacheLocation));

            var second = await client.GetAsync("/test/photo.jpg?w=50");
            var secondBytes = await second.Content.ReadAsByteArrayAsync();

            Assert.Equal("image/jpeg", first.Content.Headers.ContentType?.ToString());
            Assert.Equal("image/jpeg", second.Content.Headers.ContentType?.ToString());
            Assert.Equal(firstBytes, secondBytes);
        }

        [Fact]
        public async Task ShouldCacheOnce_HoweverTheSameRequestIsWritten()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                UseDiskCache = true,
                DiskCacheLocation = site.CacheLocation
            });
            var client = host.GetTestClient();

            //the same image asked for with the short name, the long name, an unknown parameter, and a very long url
            await client.GetAsync("/test/photo.jpg?w=50");
            await client.GetAsync("/test/photo.jpg?width=50");
            await client.GetAsync("/test/photo.jpg?w=50&unknown=1");
            await client.GetAsync("/test/photo.jpg?w=50&unknown=" + new string('x', 300));
            Assert.Single(Directory.GetFiles(site.CacheLocation));

            //a different image is cached separately
            await client.GetAsync("/test/photo.jpg?w=60");
            Assert.Equal(2, Directory.GetFiles(site.CacheLocation).Length);
        }

        [Fact]
        public async Task ShouldUseTextFromTheUrl_BeforeTheFileName()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/icons",
                IsVirtual = true,
                UseFileNameAsText = true,
                DefaultImageActions = new ImageActions()
                {
                    Width = 64,
                    Height = 64,
                    Format = SKEncodedImageFormat.Png,
                    ChooseImageColorFromTextValue = true
                }
            });
            var client = host.GetTestClient();

            //two names that are given different background colours
            var colourCount = (ulong)ImageProcessor.BackgroundColours.Count;
            var firstName = "Alice";
            var secondName = new[] { "Bob", "Carol", "Dave", "Erin" }
                .First(x => ImageProcessor.CalculateHash(x) % colourCount != ImageProcessor.CalculateHash(firstName) % colourCount);

            var fromFileName = await client.GetByteArrayAsync($"/icons/{firstName}.png");
            var fromUrl = await client.GetByteArrayAsync($"/icons/profile.png?t={firstName}");
            var otherFromUrl = await client.GetByteArrayAsync($"/icons/profile.png?t={secondName}");

            Assert.Equal(fromFileName, fromUrl);
            Assert.NotEqual(fromUrl, otherFromUrl);
        }

        [Fact]
        public void ConvertQueryString_ShouldKeepDefaultTextValues_WhenTheUrlDoesNotSetThem()
        {
            var defaults = new ImageActions()
            {
                Text = "abc",
                TextColor = "ff0000",
                ImageColor = "00ff00",
                PdfPassword = "secret"
            };

            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions("?w=50", defaults);

            Assert.Equal(50, actions.Width);
            Assert.Equal("abc", actions.Text);
            Assert.Equal("ff0000", actions.TextColor);
            Assert.Equal("00ff00", actions.ImageColor);
            Assert.Equal("secret", actions.PdfPassword);
        }

        [Fact]
        public void ConvertQueryString_ShouldReplaceDefaultTextValues_WhenTheUrlSetsThem()
        {
            var defaults = new ImageActions()
            {
                Text = "abc",
                TextColor = "ff0000"
            };

            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions("?t=xyz&tc=", defaults);

            Assert.Equal("xyz", actions.Text);
            Assert.Equal(string.Empty, actions.TextColor);
        }

        [Fact]
        public void GetContentType_ShouldRecogniseTheFormatsThatAreProduced()
        {
            var jpeg = TestHelpers.CreateImage(8, 8, SKEncodedImageFormat.Jpeg);
            var png = TestHelpers.CreateImage(8, 8, SKEncodedImageFormat.Png);
            var webp = TestHelpers.CreateImage(8, 8, SKEncodedImageFormat.Webp);

            Assert.Equal("image/jpeg", CommonImageActionsMiddleware.GetContentType(jpeg, jpeg.Length));
            Assert.Equal("image/png", CommonImageActionsMiddleware.GetContentType(png, png.Length));
            Assert.Equal("image/webp", CommonImageActionsMiddleware.GetContentType(webp, webp.Length));
            Assert.Equal("application/octet-stream", CommonImageActionsMiddleware.GetContentType(new byte[] { 1, 2 }, 2));
        }

        [Fact]
        public async Task Remote_ShouldDownloadAndProcessTheImage()
        {
            using var site = new TestSite();
            var (fileServer, fileServerUrl) = await site.StartFileServerAsync();
            using (fileServer)
            {
                using var host = await site.StartAsync(new CommonImageActionSettings()
                {
                    PathToWatch = "/remote",
                    RemoteFileServerUrl = fileServerUrl
                });
                var client = host.GetTestClient();

                //a name with a space, and the watched path in a different case
                var processed = await client.GetAsync("/remote/test/my%20photo.jpg?w=50");
                var differentCase = await client.GetAsync("/REMOTE/test/photo.jpg?w=60");

                Assert.Equal(HttpStatusCode.OK, processed.StatusCode);
                Assert.Equal(50, TestHelpers.GetImageInfo(await processed.Content.ReadAsByteArrayAsync()).Width);
                Assert.Equal("image/jpeg", processed.Content.Headers.ContentType?.ToString());

                Assert.Equal(HttpStatusCode.OK, differentCase.StatusCode);
                Assert.Equal(60, TestHelpers.GetImageInfo(await differentCase.Content.ReadAsByteArrayAsync()).Width);
            }
        }

        [Fact]
        public async Task Remote_ShouldPassTheFileThrough_WhenThereAreNoActions()
        {
            using var site = new TestSite();
            var (fileServer, fileServerUrl) = await site.StartFileServerAsync();
            using (fileServer)
            {
                using var host = await site.StartAsync(new CommonImageActionSettings()
                {
                    PathToWatch = "/remote",
                    RemoteFileServerUrl = fileServerUrl
                });
                var client = host.GetTestClient();

                var original = await client.GetAsync("/remote/test/photo.jpg");
                var missing = await client.GetAsync("/remote/test/missing.jpg?w=50");

                Assert.Equal(HttpStatusCode.OK, original.StatusCode);
                Assert.Equal("image/jpeg", original.Content.Headers.ContentType?.ToString());
                Assert.Equal(File.ReadAllBytes(Path.Combine(site.WebRoot, "test", "photo.jpg")), await original.Content.ReadAsByteArrayAsync());

                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }
        }

        [Fact]
        public async Task Remote_ShouldWork_WhenWatchingEveryPath()
        {
            using var site = new TestSite();
            var (fileServer, fileServerUrl) = await site.StartFileServerAsync();
            using (fileServer)
            {
                //no PathToWatch, so the whole request path is the path on the remote server
                using var host = await site.StartAsync(new CommonImageActionSettings()
                {
                    RemoteFileServerUrl = fileServerUrl
                });

                var response = await host.GetTestClient().GetAsync("/test/photo.jpg?w=50");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(50, TestHelpers.GetImageInfo(await response.Content.ReadAsByteArrayAsync()).Width);
            }
        }
    }
}
