using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TestProject.Controllers;
using Xunit;

namespace TestProject.Tests;

public sealed class TestControllerTests : WorkspaceFixture
{
    private TestController Controller => new(NullLogger<TestController>.Instance, Workspace);

    [Fact]
    public void BrowseReturnsRequestedFolder()
    {
        Workspace.CreateFolder(null, "folder");
        File.WriteAllText(Path.Combine(Root, "folder", "notes.txt"), "hello");
        var result = Assert.IsType<OkObjectResult>(Controller.Browse("folder", default));
        var body = Json(result.Value!);
        Assert.Equal("folder", body.GetProperty("path").GetString());
        Assert.Equal("notes.txt", Assert.Single(body.GetProperty("entries").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public void SearchReturnsMatchingEntries()
    {
        File.WriteAllText(Path.Combine(Root, "Notes.txt"), "hello");
        File.WriteAllText(Path.Combine(Root, "other.txt"), "other");
        var result = Assert.IsType<OkObjectResult>(Controller.Search(null, "NOTE", default));
        Assert.Equal("Notes.txt", Assert.Single(Json(result.Value!).GetProperty("entries").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public void BrowseAndSearchPropagateCancellation()
    {
        File.WriteAllText(Path.Combine(Root, "file"), "hello");
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => Controller.Browse(null, token));
        Assert.Throws<OperationCanceledException>(() => Controller.Search(null, "file", token));
    }

    [Fact]
    public async Task DownloadReturnsFileContentAndMetadata()
    {
        Workspace.CreateFolder(null, "folder");
        File.WriteAllText(Path.Combine(Root, "folder", "notes.txt"), "hello");
        var result = Assert.IsType<FileStreamResult>(Controller.Download("folder/notes.txt"));
        using var stream = result.FileStream;
        using var reader = new StreamReader(stream);
        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Equal("notes.txt", result.FileDownloadName);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public void DownloadRejectsDirectoryAndPropagatesMissingFile()
    {
        Assert.IsType<BadRequestResult>(Controller.Download(""));
        Assert.Throws<FileNotFoundException>(() => Controller.Download("missing"));
    }

    [Fact]
    public async Task UploadSavesFileAndReturnsName()
    {
        Workspace.CreateFolder(null, "folder");
        var file = Upload("notes.txt", "hello");
        using var stream = file.OpenReadStream();
        var result = Assert.IsType<OkObjectResult>(await Controller.Upload("folder", file, default));
        Assert.Equal("notes.txt", Json(result.Value!).GetProperty("name").GetString());
        Assert.Equal("hello", File.ReadAllText(Path.Combine(Root, "folder", "notes.txt")));
    }

    [Theory]
    [InlineData(10 * 1024 * 1024, false)]
    [InlineData(10 * 1024 * 1024 + 1, true)]
    public async Task UploadEnforcesSizeLimit(long length, bool rejected)
    {
        using var stream = new MemoryStream(new byte[length]);
        var file = new FormFile(stream, 0, length, "file", "large.bin");
        var result = await Controller.Upload(null, file, default);
        if (rejected)
        {
            var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
            Assert.Equal(413, problem.Status);
            Assert.Equal("Choose a file of 10 MiB or less.", problem.Title);
            Assert.Empty(Directory.GetFileSystemEntries(Root));
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(length, new FileInfo(Path.Combine(Root, "large.bin")).Length);
        }
    }

    [Fact]
    public async Task UploadPropagatesConflictAndCancellation()
    {
        File.WriteAllText(Path.Combine(Root, "notes.txt"), "original");
        var file = Upload("notes.txt", "replacement");
        using var stream = file.OpenReadStream();
        Assert.Equal(409, (await Assert.ThrowsAsync<BadHttpRequestException>(() => Controller.Upload(null, file, default))).StatusCode);
        var cancelled = Upload("cancelled.txt", "content");
        using var cancelledStream = cancelled.OpenReadStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller.Upload(null, cancelled, new CancellationToken(true)));
        Assert.False(File.Exists(Path.Combine(Root, "cancelled.txt")));
    }

    [Fact]
    public void CreateFolderReturnsNameAndPropagatesConflict()
    {
        var result = Assert.IsType<OkObjectResult>(Controller.CreateFolder(null, "folder"));
        Assert.Equal("folder", Json(result.Value!).GetProperty("name").GetString());
        Assert.True(Directory.Exists(Path.Combine(Root, "folder")));
        Assert.Equal(409, Assert.Throws<BadHttpRequestException>(() => Controller.CreateFolder(null, "folder")).StatusCode);
    }

    [Fact]
    public void DeleteReturnsNoContentAndPropagatesErrors()
    {
        File.WriteAllText(Path.Combine(Root, "file"), "content");
        Assert.IsType<NoContentResult>(Controller.Delete("file"));
        Assert.False(File.Exists(Path.Combine(Root, "file")));
        Assert.Throws<FileNotFoundException>(() => Controller.Delete("missing"));
        Assert.Equal(400, Assert.Throws<BadHttpRequestException>(() => Controller.Delete("")).StatusCode);
    }

    [Fact]
    public void ActionsPropagateInvalidPaths()
    {
        Assert.Throws<ArgumentException>(() => Controller.Browse("..", default));
        Assert.Throws<ArgumentException>(() => Controller.Search("..", "query", default));
        Assert.Throws<ArgumentException>(() => Controller.Download(".."));
        Assert.Throws<ArgumentException>(() => Controller.CreateFolder(null, ".."));
        Assert.Throws<ArgumentException>(() => Controller.Delete(".."));
    }
}
