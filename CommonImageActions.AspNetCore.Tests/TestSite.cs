using CommonImageActions.Core.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;

namespace CommonImageActions.AspNetCore.Tests
{
    /// <summary>
    /// A throw away web site on disk for the middleware to serve, so tests can use real files.
    /// </summary>
    public sealed class TestSite : IDisposable
    {
        public const int PhotoWidth = 400;
        public const int PhotoHeight = 300;

        public string ContentRoot { get; }

        public string WebRoot => Path.Combine(ContentRoot, "wwwroot");

        public string CacheLocation => Path.Combine(ContentRoot, "cache");

        public TestSite()
        {
            ContentRoot = Path.Combine(Path.GetTempPath(), "CommonImageActionsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(WebRoot, "test"));

            var photo = TestHelpers.CreateImage(PhotoWidth, PhotoHeight, SKEncodedImageFormat.Jpeg);
            File.WriteAllBytes(Path.Combine(WebRoot, "test", "photo.jpg"), photo);
            File.WriteAllBytes(Path.Combine(WebRoot, "test", "my photo.jpg"), photo);

            //a file that is outside of wwwroot and must never be served
            File.WriteAllBytes(Path.Combine(ContentRoot, "secret.jpg"), photo);
        }

        /// <summary>
        /// Start an in memory server that runs the middleware once for each of the settings.
        /// </summary>
        public Task<IHost> StartAsync(params CommonImageActionSettings[] settings)
        {
            return new HostBuilder()
                .UseContentRoot(ContentRoot)
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .Configure(app =>
                        {
                            foreach (var setting in settings)
                            {
                                app.UseCommonImageActions(setting);
                            }
                        });
                })
                .StartAsync();
        }

        /// <summary>
        /// Start a real server on a free local port that only serves the files in wwwroot. It stands
        /// in for the remote file server (blob storage) that the middleware downloads images from.
        /// </summary>
        public async Task<(IHost Host, string Url)> StartFileServerAsync()
        {
            var host = await new HostBuilder()
                .UseContentRoot(ContentRoot)
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseKestrel()
                        .UseUrls("http://127.0.0.1:0")
                        .Configure(app =>
                        {
                            app.UseStaticFiles();
                        });
                })
                .StartAsync();

            var url = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return (host, url);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(ContentRoot, recursive: true);
            }
            catch (IOException)
            {
                //a file is still in use, it is in the temp folder so leave it
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
