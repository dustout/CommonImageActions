using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using CommonImageActions.Pdf;
using Microsoft.AspNetCore.TestHost;
using SkiaSharp;
using System.Net;

namespace CommonImageActions.AspNetCore.Tests
{
    public class CommonImageActionsMiddlewareCoverageTests
    {
        private static (int Width, int Height) Size(byte[] image)
        {
            var info = TestHelpers.GetImageInfo(image);
            return (info.Width, info.Height);
        }

        private static async Task<HttpResponseMessage> GetWithHeaderAsync(HttpClient client, string url, string header, string value, HttpMethod? method = null)
        {
            var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(header, value);
            return await client.SendAsync(request);
        }

        // ---------------------------------------------------------------- pdfs through the middleware

        [Fact]
        public async Task ShouldRenderAPdf_ThatIsAskedForByUrl()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();

            //the first page is red and the second is blue, each 200 by 100
            var firstPage = await client.GetAsync("/test/document.pdf?w=100");
            var firstPageBytes = await firstPage.Content.ReadAsByteArrayAsync();
            var secondPage = await client.GetByteArrayAsync("/test/document.pdf?w=100&page=2");
            var shortName = await client.GetByteArrayAsync("/test/document.pdf?w=100&p=2");
            var asJpeg = await client.GetAsync("/test/document.pdf?w=100&f=jpeg");
            var upperCase = await client.GetAsync("/test/document.PDF?h=20");

            Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
            Assert.Equal("image/png", firstPage.Content.Headers.ContentType?.ToString());
            Assert.Equal((100, 50), Size(firstPageBytes));
            TestHelpers.AssertColor(TestHelpers.Red, TestHelpers.GetPixel(firstPageBytes, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(secondPage, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(shortName, 50, 25));
            Assert.Equal("image/jpeg", asJpeg.Content.Headers.ContentType?.ToString());

            //the extension is not case sensitive, the file name is wherever the file system is
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal((40, 20), Size(await upperCase.Content.ReadAsByteArrayAsync()));
            }
        }

        [Fact]
        public async Task ShouldLeaveAPdfAlone_WhenTheUrlAsksForNothing()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            //nothing after the middleware in this site, so being left alone is a 404. In a real
            //site the static file middleware is next and sends the pdf itself
            var response = await host.GetTestClient().GetAsync("/test/document.pdf");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task ShouldOpenAProtectedPdf_WithThePasswordFromTheUrlOrTheSettings()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(
                new CommonImageActionSettings() { PathToWatch = "/test" },
                new CommonImageActionSettings() { PathToWatch = "/unlocked" });
            var client = host.GetTestClient();

            //the page of the protected pdf is green
            var fromUrl = await client.GetByteArrayAsync($"/test/protected.pdf?w=100&pw={Uri.EscapeDataString(TestSite.PdfPassword)}");
            var longName = await client.GetByteArrayAsync($"/test/protected.pdf?w=100&password={Uri.EscapeDataString(TestSite.PdfPassword)}");

            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(fromUrl, 50, 25));
            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(longName, 50, 25));

            //without it, or with the wrong one, the pdf can not be opened
            await Assert.ThrowsAsync<PdfProcessorException>(() => client.GetAsync("/test/protected.pdf?w=100"));
            await Assert.ThrowsAsync<PdfProcessorException>(() => client.GetAsync("/test/protected.pdf?w=100&pw=wrong"));
        }

        [Fact]
        public async Task ShouldUseADefaultPdfPassword_EvenWhenTheUrlHasOtherParameters()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                DefaultImageActions = new ImageActions() { PdfPassword = TestSite.PdfPassword }
            });

            var result = await host.GetTestClient().GetByteArrayAsync("/test/protected.pdf?w=100");

            TestHelpers.AssertColor(TestHelpers.Green, TestHelpers.GetPixel(result, 50, 25));
        }

        [Fact]
        public async Task ShouldCacheARenderedPdf_AndSendItAsTheImageItIs()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();

            var first = await client.GetAsync("/test/document.pdf?w=100");
            var firstBytes = await first.Content.ReadAsByteArrayAsync();

            //take the pdf away, the second answer can then only have come from the cache
            File.Delete(Path.Combine(site.WebRoot, "test", "document.pdf"));
            Assert.Single(Directory.GetFiles(site.CacheLocation));

            //the etag is made from the file, so the file being gone means it is asked for again...
            var second = await client.GetAsync("/test/document.pdf?w=100");

            //...and with the file gone the request is left for the rest of the site
            Assert.Equal("image/png", first.Content.Headers.ContentType?.ToString());
            Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
            Assert.Equal((100, 50), Size(firstBytes));
        }

        // ---------------------------------------------------------------- remote file server

        [Fact]
        public async Task Remote_ShouldRenderAPdf_AndPassOneThroughUntouched()
        {
            using var site = new TestSite();
            var (fileServer, fileServerUrl) = await site.StartFileServerAsync();
            using (fileServer)
            {
                using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/remote", RemoteFileServerUrl = fileServerUrl });
                var client = host.GetTestClient();

                var rendered = await client.GetAsync("/remote/test/document.pdf?w=100&p=2");
                var renderedBytes = await rendered.Content.ReadAsByteArrayAsync();
                var untouched = await client.GetAsync("/remote/test/document.pdf");

                Assert.Equal("image/png", rendered.Content.Headers.ContentType?.ToString());
                Assert.Equal((100, 50), Size(renderedBytes));
                TestHelpers.AssertColor(TestHelpers.Blue, TestHelpers.GetPixel(renderedBytes, 50, 25));

                Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
                Assert.Equal("application/pdf", untouched.Content.Headers.ContentType?.ToString());
                Assert.Equal(File.ReadAllBytes(Path.Combine(site.WebRoot, "test", "document.pdf")), await untouched.Content.ReadAsByteArrayAsync());
            }
        }

        [Fact]
        public async Task Remote_ShouldAnswerFromTheCache_WithoutAskingTheRemoteServerAgain()
        {
            using var site = new TestSite();
            var (fileServer, fileServerUrl) = await site.StartFileServerAsync();
            using (fileServer)
            {
                using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/remote", RemoteFileServerUrl = fileServerUrl });
                var client = host.GetTestClient();

                var first = await client.GetByteArrayAsync("/remote/test/photo.jpg?w=50");

                //take the file off the remote server, it can now only come from the cache
                File.Delete(Path.Combine(site.WebRoot, "test", "photo.jpg"));
                var second = await client.GetAsync("/remote/test/photo.jpg?w=50");
                var notCached = await client.GetAsync("/remote/test/photo.jpg?w=60");

                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
                Assert.Equal(first, await second.Content.ReadAsByteArrayAsync());
                Assert.Equal(HttpStatusCode.NotFound, notCached.StatusCode);
            }
        }

        [Fact]
        public async Task Remote_ShouldAnswerNotModified_WhenTheBrowserHasTheSameImage()
        {
            using var site = new TestSite();
            var (fileServer, fileServerUrl) = await site.StartFileServerAsync();
            using (fileServer)
            {
                using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/remote", RemoteFileServerUrl = fileServerUrl, UseDiskCache = false });
                var client = host.GetTestClient();

                var first = await client.GetAsync("/remote/test/photo.jpg?w=50");
                var etag = first.Headers.ETag!.ToString();
                var same = await GetWithHeaderAsync(client, "/remote/test/photo.jpg?w=50", "If-None-Match", etag);

                //the remote file changes, which the etag of a remote image is able to notice
                File.WriteAllBytes(Path.Combine(site.WebRoot, "test", "photo.jpg"), TestHelpers.CreateImage(TestSite.PhotoWidth, TestSite.PhotoHeight, SKEncodedImageFormat.Jpeg, TestHelpers.Blue));
                var changed = await GetWithHeaderAsync(client, "/remote/test/photo.jpg?w=50", "If-None-Match", etag);

                //there is no file here to give a date for
                Assert.Null(first.Content.Headers.LastModified);
                Assert.Equal(HttpStatusCode.NotModified, same.StatusCode);
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.NotEqual(etag, changed.Headers.ETag!.ToString());
            }
        }

        [Fact]
        public async Task Remote_ShouldFail_WhenTheRemoteServerCanNotBeReached()
        {
            using var site = new TestSite();

            //nothing is listening on port 1
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/remote", RemoteFileServerUrl = "http://127.0.0.1:1/" });

            await Assert.ThrowsAsync<HttpRequestException>(() => host.GetTestClient().GetAsync("/remote/test/photo.jpg?w=50"));
        }

        // ---------------------------------------------------------------- disk cache

        [Fact]
        public async Task ShouldCacheOneFile_WhenTheSameImageIsAskedForManyTimesAtOnce()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();

            var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(x => client.GetAsync("/test/photo.jpg?w=120&blur=2")));
            var bodies = await Task.WhenAll(responses.Select(x => x.Content.ReadAsByteArrayAsync()));

            Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
            Assert.All(bodies, x => Assert.Equal(bodies[0], x));
            Assert.Equal(120, TestHelpers.GetImageInfo(bodies[0]).Width);

            //one finished file, and none of the temporary files it is written through left behind
            var cached = Directory.GetFiles(site.CacheLocation);
            Assert.Single(cached);
            Assert.Equal(bodies[0], File.ReadAllBytes(cached[0]));
            Assert.Empty(site.Problems);
        }

        [Fact]
        public async Task ShouldLogOnce_AndTryAgainLater_WhenTheCacheCanNotBeWrittenTo()
        {
            using var site = new TestSite();

            //a folder can not be made inside of something that is a file
            var blocker = Path.Combine(site.ContentRoot, "blocker");
            File.WriteAllText(blocker, "this is a file");
            var cacheLocation = Path.Combine(blocker, "cache");

            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test", DiskCacheLocation = cacheLocation });
            var client = host.GetTestClient();

            var retryInterval = CommonImageActionSettings.DiskCacheRetryInterval;
            try
            {
                // every request is answered, and the failure is only logged the first time
                CommonImageActionSettings.DiskCacheRetryInterval = TimeSpan.FromHours(1);
                for (var width = 50; width < 55; width++)
                {
                    Assert.Equal(width, TestHelpers.GetImageInfo(await client.GetByteArrayAsync($"/test/photo.jpg?w={width}")).Width);
                }

                Assert.Single(site.Problems);
                Assert.Contains("not being cached", site.Problems.Single());

                // the problem is fixed, but it is not time to try again yet
                File.Delete(blocker);
                await client.GetByteArrayAsync("/test/photo.jpg?w=60");
                Assert.False(Directory.Exists(cacheLocation));

                // once it is time, caching starts working
                CommonImageActionSettings.DiskCacheRetryInterval = TimeSpan.Zero;
                await client.GetByteArrayAsync("/test/photo.jpg?w=61");
                Assert.Single(Directory.GetFiles(cacheLocation));
                Assert.Single(site.Problems);
            }
            finally
            {
                CommonImageActionSettings.DiskCacheRetryInterval = retryInterval;
            }
        }

        [Fact]
        public async Task ShouldBringACacheThatIsAlreadyTooBigBackUnderItsLimit()
        {
            using var site = new TestSite();

            //a cache folder left over from before, holding five files of 100 kilobytes written a while ago
            Directory.CreateDirectory(site.CacheLocation);
            for (var i = 0; i < 5; i++)
            {
                var old = Path.Combine(site.CacheLocation, $"OLD{i}");
                File.WriteAllBytes(old, new byte[100 * 1024]);
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10 + i));
            }

            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test", DiskCacheMaxSizeBytes = 250 * 1024 });
            var fresh = await host.GetTestClient().GetByteArrayAsync("/test/photo.jpg?w=50");

            // the clean up runs in the background, so give it a moment
            long cacheBytes = 0;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                cacheBytes = new DirectoryInfo(site.CacheLocation).GetFiles().Sum(x => x.Length);
                if (cacheBytes <= 250 * 1024)
                {
                    break;
                }

                await Task.Delay(50);
            }

            var remaining = Directory.GetFiles(site.CacheLocation).Select(x => Path.GetFileName(x)).ToList();
            Assert.True(cacheBytes <= 250 * 1024, $"The cache is {cacheBytes} bytes.");

            //the oldest files are the ones that went, and the image that was just made is still there
            Assert.DoesNotContain("OLD0", remaining);
            Assert.DoesNotContain("OLD1", remaining);
            Assert.Contains("OLD4", remaining);
            Assert.Contains(remaining, x => File.ReadAllBytes(Path.Combine(site.CacheLocation, x)).SequenceEqual(fresh));
        }

        // ---------------------------------------------------------------- http caching headers

        [Theory]
        [InlineData("*")]
        [InlineData("W/{etag}")]
        [InlineData("\"something-else\", {etag}")]
        [InlineData("\"one\",\"two\" ,  {etag}  , \"three\"")]
        public async Task ShouldAnswerNotModified_HoweverTheBrowserSendsTheEtagBack(string ifNoneMatch)
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();
            var etag = (await client.GetAsync("/test/photo.jpg?w=50")).Headers.ETag!.ToString();

            var response = await GetWithHeaderAsync(client, "/test/photo.jpg?w=50", "If-None-Match", ifNoneMatch.Replace("{etag}", etag));

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal(etag, response.Headers.ETag!.ToString());
        }

        [Theory]
        [InlineData("\"something-else\"")]
        [InlineData("\"one\", \"two\"")]
        [InlineData("not even an etag")]
        public async Task ShouldSendTheImage_WhenTheBrowserHasADifferentOne(string ifNoneMatch)
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            var response = await GetWithHeaderAsync(host.GetTestClient(), "/test/photo.jpg?w=50", "If-None-Match", ifNoneMatch);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(50, TestHelpers.GetImageInfo(await response.Content.ReadAsByteArrayAsync()).Width);
        }

        [Fact]
        public async Task ShouldCompareDates_OnlyWhenTheBrowserSendsNoEtag()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });
            var client = host.GetTestClient();
            var first = await client.GetAsync("/test/photo.jpg?w=50");
            var lastModified = first.Content.Headers.LastModified!.Value;

            var sameDate = await GetWithHeaderAsync(client, "/test/photo.jpg?w=50", "If-Modified-Since", lastModified.ToString("r"));
            var laterDate = await GetWithHeaderAsync(client, "/test/photo.jpg?w=50", "If-Modified-Since", lastModified.AddDays(1).ToString("r"));
            var earlierDate = await GetWithHeaderAsync(client, "/test/photo.jpg?w=50", "If-Modified-Since", lastModified.AddDays(-1).ToString("r"));
            var notADate = await GetWithHeaderAsync(client, "/test/photo.jpg?w=50", "If-Modified-Since", "last tuesday");

            //an etag that does not match wins over a date that does
            var request = new HttpRequestMessage(HttpMethod.Get, "/test/photo.jpg?w=50");
            request.Headers.TryAddWithoutValidation("If-None-Match", "\"something-else\"");
            request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified.ToString("r"));
            var etagWins = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotModified, sameDate.StatusCode);
            Assert.Equal(HttpStatusCode.NotModified, laterDate.StatusCode);
            Assert.Equal(HttpStatusCode.OK, earlierDate.StatusCode);
            Assert.Equal(HttpStatusCode.OK, notADate.StatusCode);
            Assert.Equal(HttpStatusCode.OK, etagWins.StatusCode);
        }

        [Fact]
        public async Task ShouldAnswerAHeadRequest_ThatAlreadyHasTheImage_WithNotModified()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test", BrowserCacheMaxAge = TimeSpan.FromMinutes(5) });
            var client = host.GetTestClient();
            var etag = (await client.GetAsync("/test/photo.jpg?w=50")).Headers.ETag!.ToString();

            var response = await GetWithHeaderAsync(client, "/test/photo.jpg?w=50", "If-None-Match", etag, HttpMethod.Head);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal(etag, response.Headers.ETag!.ToString());
            Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task ShouldGiveAVirtualImageAnEtag_ThatChangesWithWhatIsAskedFor()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/virtual", IsVirtual = true });
            var client = host.GetTestClient();

            var red = await client.GetAsync("/virtual/x.png?c=ff0000");
            var redAgain = await client.GetAsync("/virtual/x.png?color=ff0000");
            var blue = await client.GetAsync("/virtual/x.png?c=0000ff");
            var notModified = await GetWithHeaderAsync(client, "/virtual/x.png?c=ff0000", "If-None-Match", red.Headers.ETag!.ToString());

            Assert.Equal(red.Headers.ETag, redAgain.Headers.ETag);
            Assert.NotEqual(red.Headers.ETag, blue.Headers.ETag);
            Assert.Null(red.Content.Headers.LastModified);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        }

        // ---------------------------------------------------------------- signed urls

        [Fact]
        public async Task ShouldCheckSignatures_WhenTheSiteIsServedFromASubFolder()
        {
            const string key = "a key that only the server knows";
            using var site = new TestSite() { PathBase = "/app" };
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test", SigningKey = key });
            var client = host.GetTestClient();

            //the url is signed the way it is written in the page, sub folder included
            var signed = await client.GetAsync(CommonImageActionsUrlSigner.SignUrl("/app/test/photo.jpg?w=50", key));
            var signedWithoutTheFolder = await client.GetAsync("/app" + CommonImageActionsUrlSigner.SignUrl("/test/photo.jpg?w=50", key));
            var unsigned = await client.GetAsync("/app/test/photo.jpg?w=50");

            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            Assert.Equal(50, TestHelpers.GetImageInfo(await signed.Content.ReadAsByteArrayAsync()).Width);
            Assert.Equal(HttpStatusCode.Forbidden, signedWithoutTheFolder.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        }

        [Fact]
        public async Task ShouldCheckTheSignature_BeforeAnythingElseIsDone()
        {
            const string key = "a key that only the server knows";
            using var site = new TestSite();
            var hookCalled = false;
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/test",
                SigningKey = key,
                OnActionsParsed = (context, actions) =>
                {
                    hookCalled = true;
                    return Task.CompletedTask;
                }
            });
            var client = host.GetTestClient();

            //a file that does not exist is still refused rather than reported as missing
            var missingFile = await client.GetAsync("/test/not-there.jpg?w=50");
            var emptySignature = await client.GetAsync("/test/photo.jpg?w=50&sig=");
            var signatureNotLast = await client.GetAsync(CommonImageActionsUrlSigner.SignUrl("/test/photo.jpg?w=50", key) + "&w=5000");

            Assert.Equal(HttpStatusCode.Forbidden, missingFile.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, emptySignature.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, signatureNotLast.StatusCode);
            Assert.False(hookCalled);
            Assert.False(Directory.Exists(site.CacheLocation));
        }

        [Fact]
        public void UrlSigner_ShouldRejectMissingValues()
        {
            Assert.Throws<ArgumentNullException>(() => CommonImageActionsUrlSigner.SignUrl(null!, "key"));
            Assert.Throws<ArgumentNullException>(() => CommonImageActionsUrlSigner.CreateSignature(null!, "key"));
            Assert.Throws<ArgumentNullException>(() => CommonImageActionsUrlSigner.SignUrl("/a.jpg?w=1", null!));
            Assert.Throws<ArgumentNullException>(() => CommonImageActionsUrlSigner.SignUrl("/a.jpg?w=1", string.Empty));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/a.jpg", null!, "key"));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/a.jpg", "?w=1&sig=abc", null!));
            Assert.False(CommonImageActionsUrlSigner.IsValid("/a.jpg", "?w=1&notsig=abc", "key"));

            //a different key or a different url is a different signature
            Assert.NotEqual(CommonImageActionsUrlSigner.CreateSignature("/a.jpg?w=1", "key"), CommonImageActionsUrlSigner.CreateSignature("/a.jpg?w=1", "other key"));
            Assert.NotEqual(CommonImageActionsUrlSigner.CreateSignature("/a.jpg?w=1", "key"), CommonImageActionsUrlSigner.CreateSignature("/a.jpg?w=2", "key"));

            //and the same url written with or without escaping is the same one
            Assert.Equal(CommonImageActionsUrlSigner.CreateSignature("/my photo.jpg?w=1", "key"), CommonImageActionsUrlSigner.CreateSignature("/my%20photo.jpg?w=1", "key"));
        }

        // ---------------------------------------------------------------- which requests are handled

        [Theory]
        [InlineData("/test/photo.txt?w=50")]
        [InlineData("/test/photo?w=50")]
        [InlineData("/test/?w=50")]
        [InlineData("/other/photo.jpg?w=50")]
        [InlineData("/testing/photo.jpg?w=50")]
        [InlineData("/?w=50")]
        [InlineData("/test/missing.jpg?w=50")]
        [InlineData("/test/photo.jpg")]
        [InlineData("/test/photo.jpg?unknown=1")]
        public async Task ShouldLeaveARequestAlone_WhenItIsNotForAnImageItCanMake(string url)
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            var response = await host.GetTestClient().GetAsync(url);

            //nothing is after the middleware in this site, so a request that was left alone is a 404
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(Directory.Exists(site.CacheLocation));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("/", "")]
        [InlineData("", "")]
        [InlineData("test", "/test")]
        [InlineData("/test", "/test")]
        [InlineData("/test/", "/test")]
        [InlineData("two/deep/", "/two/deep")]
        public void PathToWatch_ShouldAlwaysStartWithASlash_AndNeverEndWithOne(string? value, string expected)
        {
            var settings = new CommonImageActionSettings() { PathToWatch = value! };

            Assert.Equal(expected, settings.PathToWatch);
        }

        [Fact]
        public async Task ShouldWatchEveryPath_WhenPathToWatchIsNotSet()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings());

            var response = await host.GetTestClient().GetAsync("/test/photo.jpg?w=50");

            Assert.Equal(50, TestHelpers.GetImageInfo(await response.Content.ReadAsByteArrayAsync()).Width);
        }

        [Fact]
        public async Task ShouldTakeTextFromAFileNameThatIsEscapedInTheUrl()
        {
            using var site = new TestSite();
            using var host = await site.StartAsync(new CommonImageActionSettings()
            {
                PathToWatch = "/icons",
                IsVirtual = true,
                UseFileNameAsText = true,
                DefaultImageActions = new ImageActions() { Width = 64, Height = 64, ChooseImageColorFromTextValue = true }
            });
            var client = host.GetTestClient();

            var fromFileName = await client.GetByteArrayAsync("/icons/John%20Doe.png");
            var fromUrl = await client.GetByteArrayAsync("/icons/anything.png?t=John%20Doe");
            var emptyText = await client.GetByteArrayAsync("/icons/John%20Doe.png?t=");

            //the %20 is a space by the time it is used, and an empty text in the url is not a text
            Assert.Equal(fromUrl, fromFileName);
            Assert.Equal(fromUrl, emptyText);
        }

        [Fact]
        public async Task ShouldFailTheRequest_WhenTheFileIsNotAnImage()
        {
            using var site = new TestSite();
            File.WriteAllText(Path.Combine(site.WebRoot, "test", "broken.jpg"), "this is not a picture");
            using var host = await site.StartAsync(new CommonImageActionSettings() { PathToWatch = "/test" });

            await Assert.ThrowsAsync<ImageProcessorException>(() => host.GetTestClient().GetAsync("/test/broken.jpg?w=50"));

            //and nothing is cached for it
            Assert.False(Directory.Exists(site.CacheLocation));
        }
    }
}
