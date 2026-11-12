using CommonImageActions.Core.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.Concurrent;

namespace CommonImageActions.AspNetCore.Tests
{
    /// <summary>
    /// A throw away web site on disk for the middleware to serve, so tests can use real files.
    /// </summary>
    public sealed class TestSite : IDisposable
    {
        public const int PhotoWidth = 400;
        public const int PhotoHeight = 300;

        /// <summary>
        /// The password of wwwroot/test/protected.pdf
        /// </summary>
        public const string PdfPassword = "open sesame";

        public string ContentRoot { get; }

        public string WebRoot => Path.Combine(ContentRoot, "wwwroot");

        public string CacheLocation => Path.Combine(ContentRoot, "cache");

        /// <summary>
        /// When set the site is served from under this path (as an application in a sub folder of
        /// a larger site is), for example "/app".
        /// </summary>
        public string? PathBase { get; set; }

        /// <summary>
        /// Everything the site logged as a warning or an error.
        /// </summary>
        public ConcurrentQueue<string> Problems { get; } = new ConcurrentQueue<string>();

        public TestSite()
        {
            ContentRoot = Path.Combine(Path.GetTempPath(), "CommonImageActionsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(WebRoot, "test"));

            var photo = TestHelpers.CreateImage(PhotoWidth, PhotoHeight, SKEncodedImageFormat.Jpeg);
            File.WriteAllBytes(Path.Combine(WebRoot, "test", "photo.jpg"), photo);
            File.WriteAllBytes(Path.Combine(WebRoot, "test", "my photo.jpg"), photo);

            //a pdf with a red first page and a blue second page, and one that needs a password with a green page
            File.WriteAllBytes(Path.Combine(WebRoot, "test", "document.pdf"), TestPdf.Create(TestHelpers.Red, TestHelpers.Blue));
            File.WriteAllBytes(Path.Combine(WebRoot, "test", "protected.pdf"), TestPdf.CreateProtected(PdfPassword, TestHelpers.Green));

            //a file that is outside of wwwroot and must never be served
            File.WriteAllBytes(Path.Combine(ContentRoot, "secret.jpg"), photo);
        }

        /// <summary>
        /// Start an in memory server that runs the middleware once for each of the settings.
        /// </summary>
        public Task<IHost> StartAsync(params CommonImageActionSettings[] settings)
        {
            //the disk cache is on by default and its default folder is shared by the whole
            //process, so give every site its own unless a test chose one
            foreach (var setting in settings)
            {
                if (string.IsNullOrEmpty(setting.DiskCacheLocation))
                {
                    setting.DiskCacheLocation = CacheLocation;
                }
            }

            return new HostBuilder()
                .UseContentRoot(ContentRoot)
                .ConfigureLogging(logging => logging.AddProvider(new ProblemCollector(Problems)))
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .Configure(app =>
                        {
                            if (string.IsNullOrEmpty(PathBase) == false)
                            {
                                app.UsePathBase(PathBase);
                            }

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

        /// <summary>
        /// A logger that keeps the warnings and errors, so a test can check what was logged.
        /// </summary>
        private sealed class ProblemCollector : ILoggerProvider, ILogger
        {
            private readonly ConcurrentQueue<string> _problems;

            public ProblemCollector(ConcurrentQueue<string> problems)
            {
                _problems = problems;
            }

            public ILogger CreateLogger(string categoryName) => this;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    _problems.Enqueue(formatter(state, exception));
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
