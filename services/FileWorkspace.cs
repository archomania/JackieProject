namespace TestProject.Services {
    public sealed record Entry(
        string Name, 
        string Path, 
        string Kind, 
        long SizeBytes, 
        DateTime LastModified);

    public sealed class FileWorkspace {
        private readonly ILogger<FileWorkspace> _logger;
        private readonly string root;
        private const string Pending = ".upload-";

        public FileWorkspace(IConfiguration config, IWebHostEnvironment env, ILogger<FileWorkspace> logger) {
            _logger = logger;
            var configuredRoot = config["FileBrowser:HomeDirectory"] ?? throw new ArgumentException("FileBrowser:HomeDirectory is not configured.");
            root = Path.GetFullPath(configuredRoot, env.ContentRootPath);
            Directory.CreateDirectory(root);
        }

        public string Resolve(string? relative) {
            string current = root;
            RejectLink(current);
            if (string.IsNullOrEmpty(relative)) return current;
            foreach (var part in relative.Split('/')) {
                ValidateName(part);
                current = Path.Combine(current, part);
                RejectLink(current);
            }
            // Return the resulting safe absolute path.
            return current;
        }

        public string GetFiles() {
            // Implement your file browsing logic here
            return "List of files";
        }

        // All the guards
        private static void RejectLink(string path){
            if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new UnauthorizedAccessException();
        }

        private static void ValidateName(string name){
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name) 
                || name is "." or ".."
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 
                || name.StartsWith(Pending, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("Invalid file or directory name.");
            }
        }

        private static IEnumerable<FileSystemInfo> Children(DirectoryInfo folder){
            return folder.EnumerateFileSystemInfos()
                    .Where(item => !item.Attributes.HasFlag(FileAttributes.ReparsePoint) && !item.Name.StartsWith(
                            Pending,
                            StringComparison.OrdinalIgnoreCase));
        }

        private static long Size(FileSystemInfo item, CancellationToken token){
            token.ThrowIfCancellationRequested();
            if (item is FileInfo file) return file.Length;
                return Children((DirectoryInfo)item).Sum(child => Size(child, token));
        }

        public object List(string? path, string? query, CancellationToken token) {
            var folder = new DirectoryInfo(Resolve(path));

            if (!folder.Exists) throw new DirectoryNotFoundException();

            var entries = Children(folder)
                .Where(item => item.Name.Contains(query ?? "", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item is not DirectoryInfo)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new Entry(item.Name, Path.GetRelativePath(root, item.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/'), item is DirectoryInfo ? "folder" : "file",
                    Size(item, token), item.LastWriteTimeUtc)).ToList();
            int folderCount = entries.Count(entry => entry.Kind == "folder");
            return new { path = path ?? "", entries, summary = new {
                folderCount, fileCount = entries.Count - folderCount,
                totalFileBytes = entries.Sum(entry => entry.SizeBytes)
            }};
        }

        public async Task Save(string? path, IFormFile file, CancellationToken token) {
            string folder = Resolve(path);
            ValidateName(file.FileName);
            string destination = Path.Combine(folder, file.FileName);
            string temp = Path.Combine(folder, Pending + Guid.NewGuid().ToString("N"));
            try {
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await file.CopyToAsync(stream, token);
                File.Move(temp, destination, overwrite: false);
           } catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)){
                throw new BadHttpRequestException("An item with that name already exists.", 409);
            }
             // Always remove the temporary file after success or failure.
            finally { File.Delete(temp); }
        }
    }
}
