using Microsoft.AspNetCore.Http;
using Xunit;

namespace TestProject.Tests;

public sealed class WorkspaceEdgeCaseTests : WorkspaceFixture
{
    [Theory]
    [InlineData("...")]
    [InlineData(". ")]
    [InlineData(".. ")]
    [InlineData("folder.")]
    [InlineData("folder ")]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("COM1.log")]
    [InlineData("LPT9")]
    [InlineData("COM¹.txt")]
    [InlineData("CONIN$")]
    [InlineData("folder\\child")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("a\nb")]
    public async Task UnsafeNamesCannotResolveCreateUploadOrDelete(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Workspace.Resolve(name));
        Assert.Throws<ArgumentException>(() => Workspace.CreateFolder(null, name));
        var file = Upload(name, "content");
        using (file.OpenReadStream())
            await Assert.ThrowsAsync<ArgumentException>(() => Workspace.Save(null, file, default));
        Assert.ThrowsAny<ArgumentException>(() => Workspace.Delete(name));
        Assert.True(Directory.Exists(Root));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public void LongNamesAreRejectedBeforeFilesystemAccess() =>
        Assert.Throws<ArgumentException>(() => Workspace.CreateFolder(null, new string('a', 256)));

    [Fact]
    public async Task SlashesAreRejectedInNewItemNames()
    {
        Assert.Throws<ArgumentException>(() => Workspace.CreateFolder(null, "a/b"));
        var file = Upload("a/b", "content");
        using (file.OpenReadStream())
            await Assert.ThrowsAsync<ArgumentException>(() => Workspace.Save(null, file, default));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public void RootWithTrailingSeparatorCannotBeDeleted()
    {
        var workspace = CreateWorkspace(Root + Path.DirectorySeparatorChar);
        Assert.Equal(400, Assert.Throws<BadHttpRequestException>(() => workspace.Delete("")).StatusCode);
        Assert.True(Directory.Exists(Root));
    }

    [Fact]
    public async Task SpecialCharactersAndUnicodeRoundTrip()
    {
        const string folder = "Reports & notes 日本語";
        const string name = "hello #1 + 100%.txt";
        Workspace.CreateFolder(null, folder);
        var file = Upload(name, "hello");
        using (file.OpenReadStream()) await Workspace.Save(folder, file, default);
        Assert.Equal("hello", File.ReadAllText(Workspace.Resolve(folder + "/" + name)));
        var listing = Json(Workspace.List(folder, "#1", default));
        Assert.Equal(folder + "/" + name,
            Assert.Single(listing.GetProperty("entries").EnumerateArray()).GetProperty("path").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no matches")]
    public void EmptyOrFilteredListingHonorsCancellation(string? query) =>
        Assert.Throws<OperationCanceledException>(() => Workspace.List(null, query, new CancellationToken(true)));

    [Fact]
    public void LargeFolderSizeIsUnknownWithoutHidingEntriesOrInventingTotal()
    {
        Workspace.CreateFolder(null, "large");
        File.WriteAllText(Path.Combine(Root, "large", "one.txt"), "123");
        File.WriteAllText(Path.Combine(Root, "large", "two.txt"), "456");
        File.WriteAllText(Path.Combine(Root, "visible.txt"), "hello");
        var listing = Json(CreateWorkspace("StoredFiles", maxSizeEntries: 1).List(null, null, default));
        var entries = listing.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entries[0].GetProperty("sizeBytes").ValueKind);
        Assert.Equal(5, entries[1].GetProperty("sizeBytes").GetInt64());
        var summary = listing.GetProperty("summary");
        Assert.True(summary.GetProperty("sizesIncomplete").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, summary.GetProperty("totalFileBytes").ValueKind);
        Assert.Equal(1, summary.GetProperty("folderCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("fileCount").GetInt32());
    }

    [Fact]
    public async Task ServiceRejectsOversizedUploadBeforeCreatingTemporaryFile()
    {
        using var stream = new MemoryStream();
        var file = new FormFile(stream, 0, TestProject.Services.FileWorkspace.MaxUploadBytes + 1, "file", "large.bin");
        Assert.Equal(413, (await Assert.ThrowsAsync<BadHttpRequestException>(() => Workspace.Save(null, file, default))).StatusCode);
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task UploadToFileParentReturnsNotFoundWithoutTemporaryFiles()
    {
        File.WriteAllText(Path.Combine(Root, "parent"), "original");
        var file = Upload("child.txt", "hello");
        using (file.OpenReadStream())
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Workspace.Save("parent", file, default));
        Assert.Equal("original", File.ReadAllText(Path.Combine(Root, "parent")));
        Assert.Single(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task FailedCopyRemovesPartiallyWrittenUpload()
    {
        await Assert.ThrowsAsync<IOException>(() => Workspace.Save(null, new FailingUpload(), default));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    private sealed class FailingUpload : IFormFile
    {
        public string ContentType => "application/octet-stream";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => 100;
        public string Name => "file";
        public string FileName => "broken.txt";
        public Stream OpenReadStream() => throw new NotSupportedException();
        public void CopyTo(Stream target) => throw new NotSupportedException();
        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            await target.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
            throw new IOException("Upload disconnected.");
        }
    }
}
