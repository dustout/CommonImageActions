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
    }
}