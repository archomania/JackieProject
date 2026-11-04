using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TestProject.Services;
using Xunit;

namespace TestProject.Tests;

public sealed class FileWorkspaceTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "TestProjectTests", Guid.NewGuid().ToString("N"));
    private readonly FileWorkspace _workspace;

    public FileWorkspaceTests()
    {
        Directory.CreateDirectory(_contentRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileBrowser:HomeDirectory"] = "StoredFiles"
            })
            .Build();

        _workspace = new FileWorkspace(
            configuration,
            new TestEnvironment(_contentRoot),
            NullLogger<FileWorkspace>.Instance);
    }

    [Fact]
    public void ConstructorCreatesConfiguredRoot()
    {
        Assert.True(Directory.Exists(Path.Combine(_contentRoot, "StoredFiles")));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_contentRoot, "StoredFiles")),
            _workspace.Resolve(null));
    }

    [Fact]
    public void ListFiltersAndSortsEntriesWithSummary()
    {
        var root = _workspace.Resolve(null);
        Directory.CreateDirectory(Path.Combine(root, "Zulu"));
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "123");
        File.WriteAllText(Path.Combine(root, ".upload-pending"), "ignored");
        File.WriteAllText(Path.Combine(root, "Zulu", "nested.txt"), "12345");

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var result = JsonSerializer.SerializeToDocument(
            _workspace.List(null, null, CancellationToken.None),
            options);
        var entries = result.RootElement.GetProperty("entries");

        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("Zulu", entries[0].GetProperty("name").GetString());
        Assert.Equal("folder", entries[0].GetProperty("kind").GetString());
        Assert.Equal("alpha.txt", entries[1].GetProperty("name").GetString());
        Assert.True(entries[1].TryGetProperty("lastModifiedUtc", out var lastModified));
        Assert.True(lastModified.TryGetDateTime(out _));
        Assert.Equal(1, result.RootElement.GetProperty("summary").GetProperty("folderCount").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("summary").GetProperty("fileCount").GetInt32());
        Assert.Equal(8, result.RootElement.GetProperty("summary").GetProperty("totalFileBytes").GetInt64());

        var filtered = JsonSerializer.SerializeToDocument(
            _workspace.List(null, "alpha", CancellationToken.None),
            options);
        Assert.Single(filtered.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task SaveStoresUploadedFileAndRejectsDuplicateName()
    {
        var file = CreateFormFile("notes.txt", "hello");

        await _workspace.Save(null, file, CancellationToken.None);

        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_workspace.Resolve(null), "notes.txt")));
        await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            _workspace.Save(null, CreateFormFile("notes.txt", "again"), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_workspace.Resolve(null), ".upload-*"));
    }

    [Fact]
    public void ResolveRejectsInvalidNames()
    {
        Assert.Throws<ArgumentException>(() => _workspace.Resolve("../outside"));
        Assert.Throws<ArgumentException>(() => _workspace.Resolve(".upload-hidden"));
    }

    private static IFormFile CreateFormFile(string name, string content)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "file", name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TestProject.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
    }
}
