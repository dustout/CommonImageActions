using CommonImageActions.Core.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CommonImageActions.Pdf.Tests
{
    /// <summary>
    /// The same samples are shown in two places: the page of the sample project
    /// (wwwroot/index.html) and the "Code Samples" section of the README. These tests make sure
    /// that the code in them really compiles and makes an image, that the two places say the
    /// same thing, and that every image the README shows is in the Samples folder.
    /// </summary>
    public class DocumentationSamplesTests
    {
        private const string ReadmeImageBase = "https://raw.githubusercontent.com/dustout/CommonImageActions/master/Samples/";

        private class Sample
        {
            public string Title { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
            public string Image { get; set; } = string.Empty;
        }

        [Fact]
        public void Readme_ShouldHaveTheSameSamplesAsTheSamplePage()
        {
            // Arrange
            var page = ReadSamplePage(out var pageIntroduction);
            var readme = ReadReadme(out var readmeIntroduction);

            // Assert
            Assert.NotEmpty(page);
            Assert.Equal(pageIntroduction, readmeIntroduction);
            Assert.Equal(page.Select(x => x.Title), readme.Select(x => x.Title));
            for (var i = 0; i < page.Count; i++)
            {
                Assert.True(page[i].Url == readme[i].Url, $"The url of '{page[i].Title}' is {page[i].Url} on the sample page and {readme[i].Url} in the README.");
                Assert.True(page[i].Code == readme[i].Code, $"The code of '{page[i].Title}' is different on the sample page and in the README.");
            }
        }

        [Fact]
        public void Readme_ShouldOnlyShowImagesThatAreInTheSamplesFolder()
        {
            // Arrange
            var readme = ReadReadme(out _);
            var samplesFolder = Path.Combine(RepositoryRoot(), "Samples");
            var files = Directory.GetFiles(samplesFolder).Select(x => Path.GetFileName(x)).ToList();

            // Assert
            foreach (var sample in readme)
            {
                Assert.StartsWith(ReadmeImageBase, sample.Image);
                var fileName = sample.Image.Substring(ReadmeImageBase.Length);
                Assert.True(files.Contains(fileName), $"The README shows {fileName} for '{sample.Title}' but it is not in the Samples folder.");
                Assert.True(TestHelpers.IsImage(File.ReadAllBytes(Path.Combine(samplesFolder, fileName))), $"{fileName} is not an image.");
            }

            //and nothing is in the folder that the README does not use
            var used = readme.Select(x => x.Image.Substring(ReadmeImageBase.Length)).ToList();
            Assert.Empty(files.Except(used));
        }

        [Fact]
        public async Task EverySample_ShouldCompileAndMakeAnImage()
        {
            // Arrange - the sample page is used, the test above makes sure the README is the same
            var samples = ReadSamplePage(out var introduction);
            var source = BuildSource(samples, introduction);

            // Act - compile every sample as its own method
            var assembly = Compile(source);
            var type = assembly.GetType("Samples")!;

            //the files that the samples are written about
            var testFolder = Path.Combine(RepositoryRoot(), "CommonImageActions.SampleAspnetCoreProject", "wwwroot", "test");
            var image = File.ReadAllBytes(Path.Combine(testFolder, "thumbsUp.jpg"));
            var pdf = File.ReadAllBytes(Path.Combine(testFolder, "test.pdf"));
            var logo = File.ReadAllBytes(Path.Combine(testFolder, "logo.png"));

            // Assert - and run each one
            var introductionResult = await (Task<byte[]>)type.GetMethod("Introduction")!.Invoke(null, new object[] { Path.Combine(testFolder, "thumbsUp.jpg") })!;
            Assert.True(TestHelpers.IsImage(introductionResult), "The code in the introduction did not make an image.");

            for (var i = 0; i < samples.Count; i++)
            {
                try
                {
                    var result = await (Task<byte[]>)type.GetMethod($"Sample{i}")!.Invoke(null, new object[] { image, pdf, logo })!;
                    Assert.True(TestHelpers.IsImage(result), $"The code for '{samples[i].Title}' did not make an image.");
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    Assert.Fail($"The code for '{samples[i].Title}' failed when it was run: {ex.GetBaseException().Message}");
                }
            }
        }

        /// <summary>
        /// Turn the samples into one source file, each sample being a method that returns the image.
        /// </summary>
        private static string BuildSource(List<Sample> samples, string introduction)
        {
            //the introduction says which namespaces are needed, they are all a reader is told about
            var usings = Regex.Matches(introduction, @"^using [\w.]+;\s*$", RegexOptions.Multiline).Select(x => x.Value.Trim()).ToList();
            var introductionBody = Regex.Replace(introduction, @"^using [\w.]+;\s*$", string.Empty, RegexOptions.Multiline)
                .Replace("File.ReadAllBytes(\"thumbsUp.jpg\")", "File.ReadAllBytes(path)");

            var source = new StringBuilder();
            source.AppendLine("using System;");
            source.AppendLine("using System.IO;");
            source.AppendLine("using System.Threading.Tasks;");
            foreach (var line in usings)
            {
                source.AppendLine(line);
            }

            source.AppendLine("public static class Samples");
            source.AppendLine("{");
            source.AppendLine("    public static async Task<byte[]> Introduction(string path)");
            source.AppendLine("    {");
            source.AppendLine(introductionBody);
            source.AppendLine("        return result;");
            source.AppendLine("    }");

            for (var i = 0; i < samples.Count; i++)
            {
                //a sample that loads its own pdf or logo is given them instead, and what it makes is returned
                var code = Regex.Replace(samples[i].Code, @"^var (pdf|logo) = File\.ReadAllBytes\("".*?""\);\s*$", string.Empty, RegexOptions.Multiline);
                var awaitIndex = code.IndexOf("await ", StringComparison.Ordinal);
                Assert.True(awaitIndex >= 0, $"The code for '{samples[i].Title}' does not make anything.");
                code = code.Substring(0, awaitIndex) + "return " + code.Substring(awaitIndex);

                source.AppendLine($"    public static async Task<byte[]> Sample{i}(byte[] image, byte[] pdf, byte[] logo)");
                source.AppendLine("    {");
                source.AppendLine(code);
                source.AppendLine("    }");
            }

            source.AppendLine("}");
            return source.ToString();
        }

        private static Assembly Compile(string source)
        {
            //everything this test project can use is available to the samples
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(x => File.Exists(x))
                .Select(x => MetadataReference.CreateFromFile(x))
                .ToList();

            var compilation = CSharpCompilation.Create(
                "DocumentationSamples",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            var errors = result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                //show the line of code each error is on, the line numbers mean nothing to a reader
                var lines = source.Split('\n');
                var messages = errors.Select(x => $"{x.GetMessage()} -> {lines[x.Location.GetLineSpan().StartLinePosition.Line].Trim()}");
                Assert.Fail("The code samples do not compile:" + Environment.NewLine + string.Join(Environment.NewLine, messages));
            }

            return Assembly.Load(stream.ToArray());
        }

        /// <summary>
        /// Read the samples from the page of the sample project.
        /// </summary>
        private static List<Sample> ReadSamplePage(out string introduction)
        {
            var html = Normalize(File.ReadAllText(Path.Combine(RepositoryRoot(), "CommonImageActions.SampleAspnetCoreProject", "wwwroot", "index.html")));

            var samples = Regex.Matches(
                    html,
                    "<p><img src=\"(?<image>.*?)\"></p>\\s*<h5 class=\"card-title\">(?<title>.*?)</h5>\\s*<code>(?<url>.*?)</code>\\s*<pre class=\"code-example\"><code>(?<code>.*?)</code></pre>",
                    RegexOptions.Singleline)
                .Select(x => new Sample()
                {
                    Title = WebUtility.HtmlDecode(x.Groups["title"].Value).Trim(),
                    Url = WebUtility.HtmlDecode(x.Groups["url"].Value).Trim(),
                    Code = WebUtility.HtmlDecode(x.Groups["code"].Value).Trim(),
                    Image = WebUtility.HtmlDecode(x.Groups["image"].Value).Trim()
                })
                .ToList();

            //every card on the page has to have been found, a card without code would be missed otherwise
            Assert.Equal(Regex.Matches(html, "class=\"card-title\"").Count, samples.Count);

            //the first block of code on the page is the introduction, it is not inside of a card
            var first = Regex.Match(html, "<pre class=\"code-example\"[^>]*><code>(.*?)</code></pre>", RegexOptions.Singleline);
            introduction = WebUtility.HtmlDecode(first.Groups[1].Value).Trim();
            return samples;
        }

        /// <summary>
        /// Read the samples from the "Code Samples" section of the README.
        /// </summary>
        private static List<Sample> ReadReadme(out string introduction)
        {
            var readme = Normalize(File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md")));

            var start = readme.IndexOf("\n## Code Samples\n", StringComparison.Ordinal);
            Assert.True(start >= 0, "The README has no Code Samples section.");
            var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
            var section = end > start ? readme.Substring(start, end - start) : readme.Substring(start);

            var samples = Regex.Matches(
                    section,
                    "#### (?<title>[^\n]+)\n!\\[[^\\]]*\\]\\((?<image>[^)]+)\\)\n\n`(?<url>[^\n]+)`\n```csharp\n(?<code>.*?)\n```",
                    RegexOptions.Singleline)
                .Select(x => new Sample()
                {
                    Title = x.Groups["title"].Value.Trim(),
                    Url = x.Groups["url"].Value.Trim(),
                    Code = x.Groups["code"].Value.Trim(),
                    Image = x.Groups["image"].Value.Trim()
                })
                .ToList();

            //every heading in the section has to have been read as a sample
            Assert.Equal(Regex.Matches(section, "\n#### ").Count, samples.Count);

            //the introduction is the code before the first group of samples
            var firstGroup = section.IndexOf("\n### ", StringComparison.Ordinal);
            var first = Regex.Match(section.Substring(0, firstGroup), "```csharp\n(.*?)\n```", RegexOptions.Singleline);
            introduction = first.Groups[1].Value.Trim();
            return samples;
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        /// <summary>
        /// The folder the solution is in, found by walking up from where the tests are running.
        /// </summary>
        private static string RepositoryRoot()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && File.Exists(Path.Combine(folder.FullName, "CommonImageActions.sln")) == false)
            {
                folder = folder.Parent;
            }

            Assert.True(folder != null, "The solution could not be found above the folder the tests are running in.");
            return folder!.FullName;
        }
    }
}
