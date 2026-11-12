using CommonImageActions.Core;
using CommonImageActions.Core.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using NuGet.Frameworks;

namespace CommonImageActions.AspNetCore.Tests
{
    public class CommonImageActionsMiddlewareTests
    {
        [Fact]
        public async Task ShouldBootSuccessfully()
        {
            using var host = await new HostBuilder()
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
        }

        [Fact]
        public async Task ShouldGenerateVirtualImageSuccessfully()
        {
            using var host = await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .Configure(app =>
                        {
                            app.UseCommonImageActions(
                                new CommonImageActionSettings()
                                {
                                    PathToWatch = "/virtual",
                                    IsVirtual = true
                                });
                        });
                })
                .StartAsync();

            var response = await host.GetTestClient().GetAsync("/virtual/test.png");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsByteArrayAsync();
            Assert.NotNull(content);

            var isImage = TestHelpers.IsImage(content);
            Assert.True(isImage, "The result is not a valid image.");
        }

        [Fact]
        public void ConvertQueryString_ShouldIgnoreHeightOverMax_WhenWidthIsNotSet()
        {
            var tooTall = CommonImageActionSettings.MaxUrlHeight + 1;

            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions($"?h={tooTall}");

            Assert.Null(actions.Height);
        }

        [Fact]
        public void ConvertQueryString_ShouldKeepValidHeight_WhenWidthIsOverMax()
        {
            var tooWide = CommonImageActionSettings.MaxUrlWidth + 1;

            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions($"?w={tooWide}&h=100");

            Assert.Null(actions.Width);
            Assert.Equal(100, actions.Height);
        }

        [Fact]
        public void ConvertQueryString_ShouldReportAnAction_WhenOnlyGrayscaleIsRequested()
        {
            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions("?g=true");

            Assert.True(actions.IsGrayscale);
            Assert.True(actions.HasAnyActions());
        }

        [Fact]
        public void ConvertQueryString_ShouldCarryOverDefaultGrayscale()
        {
            var defaults = new ImageActions()
            {
                IsGrayscale = true
            };

            var actions = CommonImageActionsMiddleware.ConvertQueryStringToImageActions(string.Empty, defaults);

            Assert.True(actions.IsGrayscale);
        }
    }
}