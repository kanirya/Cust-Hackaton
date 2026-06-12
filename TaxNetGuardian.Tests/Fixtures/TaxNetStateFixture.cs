using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TaxNetGuardian.Api;

namespace TaxNetGuardian.Tests.Fixtures;

/// <summary>
/// Shared test helpers for constructing an isolated <see cref="TaxNetState"/>.
///
/// Each instance is built against a unique temporary
/// <see cref="IWebHostEnvironment.ContentRootPath"/> so its <c>App_Data</c>
/// directory (snapshot + object store) never collides with other tests or the
/// real application. The state itself uses the deterministic <c>_random = new(42)</c>
/// seed baked into <see cref="TaxNetState"/>, matching how the API constructs it
/// in <c>Program.cs</c>, so synthetic data generation is reproducible.
///
/// Implements <see cref="IDisposable"/> to clean up the temp directory.
/// </summary>
public sealed class TaxNetStateFixture : IDisposable
{
    /// <summary>The isolated content root backing this fixture's App_Data.</summary>
    public string ContentRootPath { get; }

    /// <summary>A freshly constructed, isolated state instance.</summary>
    public TaxNetState State { get; }

    public TaxNetStateFixture()
    {
        ContentRootPath = Path.Combine(
            Path.GetTempPath(),
            "taxnet-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRootPath);

        State = Create(ContentRootPath);
    }

    /// <summary>
    /// Constructs a <see cref="TaxNetState"/> against the given content root,
    /// mirroring the API's DI registration (environment + configuration +
    /// default <see cref="TaxNetPlatformOptions"/>).
    /// </summary>
    public static TaxNetState Create(string contentRootPath, TaxNetPlatformOptions? platformOptions = null)
    {
        var environment = new TestWebHostEnvironment(contentRootPath);
        IConfiguration configuration = new ConfigurationBuilder().Build();
        return new TaxNetState(environment, configuration, platformOptions);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(ContentRootPath))
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; temp files are reclaimed by the OS otherwise.
        }
    }

    /// <summary>
    /// Minimal <see cref="IWebHostEnvironment"/> that points
    /// <see cref="IWebHostEnvironment.ContentRootPath"/> at an isolated temp directory.
    /// </summary>
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
            WebRootPath = contentRootPath;
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "TaxNetGuardian.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }
}
