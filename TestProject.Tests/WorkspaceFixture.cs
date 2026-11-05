using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TestProject.Services;

namespace TestProject.Tests;

public abstract class WorkspaceFixture : IDisposable
{
    protected string ContentRoot { get; } = Path.Combine(Path.GetTempPath(), "TestProjectTests", Guid.NewGuid().ToString("N"));
    protected FileWorkspace Workspace { get; }
    protected string Root => Workspace.Resolve(null);

    protected WorkspaceFixture()
    {
        Directory.CreateDirectory(ContentRoot);
        Workspace = CreateWorkspace("StoredFiles");
    }

    protected FileWorkspace CreateWorkspace(string? home) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileBrowser:HomeDirectory"] = home
        }).Build(), new TestEnvironment(ContentRoot), NullLogger<FileWorkspace>.Instance);

    protected static FormFile Upload(string name, string content) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(content)), 0, Encoding.UTF8.GetByteCount(content), "file", name);

    protected static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public void Dispose() => Directory.Delete(ContentRoot, recursive: true);

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestProject.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = root;
        public string ContentRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
