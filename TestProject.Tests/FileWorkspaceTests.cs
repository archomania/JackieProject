using Microsoft.AspNetCore.Http;
using Xunit;

namespace TestProject.Tests;

public sealed class FileWorkspaceTests : WorkspaceFixture
{
    [Theory]
    [InlineData(null)]
    [InlineData("custom")]
    public void ConstructorCreatesRoot(string? home)
    {
        var workspace = CreateWorkspace(home);
        Assert.Equal(Path.Combine(ContentRoot, home ?? "StoredFiles"), workspace.Resolve(null));
        Assert.True(Directory.Exists(workspace.Resolve("")));
    }

    [Fact]
    public void ConstructorAcceptsAbsoluteRoot()
    {
        var absolute = Path.Combine(ContentRoot, "absolute");
        Assert.Equal(absolute, CreateWorkspace(absolute).Resolve(null));
    }

    [Fact]
    public void ResolveReturnsExistingNestedPath()
    {
        Directory.CreateDirectory(Path.Combine(Root, "parent", "child"));
        Assert.Equal(Path.Combine(Root, "parent", "child"), Workspace.Resolve("parent/child"));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData(".")]
    [InlineData(" ")]
    [InlineData("/absolute")]
    [InlineData(".upload-hidden")]
    [InlineData(".UPLOAD-hidden")]
    [InlineData("bad\0name")]
    public void ResolveRejectsUnsafePaths(string path) =>
        Assert.Throws<ArgumentException>(() => Workspace.Resolve(path));

    [Fact]
    public void ListSortsFoldersFirstAndComputesRecursiveSizes()
    {
        Directory.CreateDirectory(Path.Combine(Root, "zFolder", "nested"));
        Directory.CreateDirectory(Path.Combine(Root, "AFolder"));
        File.WriteAllText(Path.Combine(Root, "zFolder", "nested", "child.txt"), "12345");
        File.WriteAllText(Path.Combine(Root, "zFolder", ".UPLOAD-hidden"), "ignored");
        File.WriteAllText(Path.Combine(Root, "b.txt"), "12");
        File.WriteAllText(Path.Combine(Root, "A.txt"), "123");
        File.WriteAllText(Path.Combine(Root, ".upload-pending"), "ignored");
        var result = Json(Workspace.List(null, null, default));
        var entries = result.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal("", result.GetProperty("path").GetString());
        Assert.Equal(new[] { "AFolder", "zFolder", "A.txt", "b.txt" }, entries.Select(e => e.GetProperty("name").GetString()));
        Assert.Equal("folder", entries[1].GetProperty("kind").GetString());
        Assert.Equal(5, entries[1].GetProperty("sizeBytes").GetInt64());
        Assert.Equal("file", entries[2].GetProperty("kind").GetString());
        Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(Root, "A.txt")), entries[2].GetProperty("lastModifiedUtc").GetDateTime());
        var summary = result.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("folderCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("fileCount").GetInt32());
        Assert.Equal(10, summary.GetProperty("totalFileBytes").GetInt64());
    }

    [Fact]
    public void ListFiltersCaseInsensitivelyWithinRequestedFolder()
    {
        Directory.CreateDirectory(Path.Combine(Root, "folder"));
        File.WriteAllText(Path.Combine(Root, "folder", "Notes.txt"), "abc");
        File.WriteAllText(Path.Combine(Root, "folder", "other.txt"), "12345");
        var result = Json(Workspace.List("folder", "NOTE", default));
        var entry = Assert.Single(result.GetProperty("entries").EnumerateArray());
        Assert.Equal("folder", result.GetProperty("path").GetString());
        Assert.Equal("folder/Notes.txt", entry.GetProperty("path").GetString());
        Assert.Equal(3, result.GetProperty("summary").GetProperty("totalFileBytes").GetInt64());
        Assert.Equal(1, result.GetProperty("summary").GetProperty("fileCount").GetInt32());
    }

    [Fact]
    public void ListEmptyFolderReturnsZeroSummary()
    {
        var result = Json(Workspace.List(null, null, default));
        Assert.Empty(result.GetProperty("entries").EnumerateArray());
        Assert.All(result.GetProperty("summary").EnumerateObject(), p => Assert.Equal(0, p.Value.GetInt64()));
    }

    [Fact]
    public void ListRejectsMissingFolder() => Assert.Throws<FileNotFoundException>(() => Workspace.List("missing", null, default));

    [Fact]
    public void ListRejectsFileAsFolder()
    {
        File.WriteAllText(Path.Combine(Root, "file"), "");
        Assert.Throws<DirectoryNotFoundException>(() => Workspace.List("file", null, default));
    }

    [Fact]
    public void ListHonorsCancellation()
    {
        File.WriteAllText(Path.Combine(Root, "file"), "content");
        Assert.Throws<OperationCanceledException>(() => Workspace.List(null, null, new CancellationToken(true)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    public async Task SaveStoresContentInRequestedFolder(string content)
    {
        Workspace.CreateFolder(null, "folder");
        var file = Upload("notes.txt", content);
        using (file.OpenReadStream())
            await Workspace.Save("folder", file, default);
        Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(Root, "folder", "notes.txt")));
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, "folder"), ".upload-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveRejectsExistingItemAndCleansTemporaryFile(bool directory)
    {
        var destination = Path.Combine(Root, "item");
        if (directory) Directory.CreateDirectory(destination);
        else File.WriteAllText(destination, "original");
        var file = Upload("item", "replacement");
        using (file.OpenReadStream())
        {
            var error = await Assert.ThrowsAsync<BadHttpRequestException>(() => Workspace.Save(null, file, default));
            Assert.Equal(409, error.StatusCode);
        }
        if (!directory) Assert.Equal("original", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(Root, ".upload-*"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(".upload-hidden")]
    public async Task SaveRejectsInvalidFilename(string name)
    {
        var file = Upload(name, "content");
        using (file.OpenReadStream())
            await Assert.ThrowsAsync<ArgumentException>(() => Workspace.Save(null, file, default));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public async Task SaveCancellationRemovesPartialUpload()
    {
        var file = Upload("cancelled.txt", "content");
        using (file.OpenReadStream())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Workspace.Save(null, file, new CancellationToken(true)));
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public void CreateFolderCreatesNestedFolder()
    {
        Workspace.CreateFolder(null, "parent");
        Workspace.CreateFolder("parent", "child");
        Assert.True(Directory.Exists(Path.Combine(Root, "parent", "child")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateFolderRejectsNameConflict(bool directory)
    {
        if (directory) Directory.CreateDirectory(Path.Combine(Root, "item"));
        else File.WriteAllText(Path.Combine(Root, "item"), "original");
        Assert.Equal(409, Assert.Throws<BadHttpRequestException>(() => Workspace.CreateFolder(null, "item")).StatusCode);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(".UPLOAD-hidden")]
    public void CreateFolderRejectsInvalidName(string name) =>
        Assert.Throws<ArgumentException>(() => Workspace.CreateFolder(null, name));

    [Fact]
    public void CreateFolderRejectsFileParent()
    {
        File.WriteAllText(Path.Combine(Root, "file"), "");
        Assert.Throws<DirectoryNotFoundException>(() => Workspace.CreateFolder("file", "child"));
    }

    [Fact]
    public void DeleteRemovesFileAndFolderRecursively()
    {
        Directory.CreateDirectory(Path.Combine(Root, "folder", "nested"));
        File.WriteAllText(Path.Combine(Root, "folder", "nested", "file"), "content");
        File.WriteAllText(Path.Combine(Root, "file"), "content");
        Workspace.Delete("file");
        Workspace.Delete("folder");
        Assert.Empty(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public void DeleteRejectsRootAndMissingItem()
    {
        Assert.Equal(400, Assert.Throws<BadHttpRequestException>(() => Workspace.Delete("")).StatusCode);
        Assert.True(Directory.Exists(Root));
        Assert.Throws<FileNotFoundException>(() => Workspace.Delete("missing"));
    }
}

