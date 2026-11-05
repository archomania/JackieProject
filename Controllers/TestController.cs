using Microsoft.AspNetCore.Mvc;
using TestProject.Services;

namespace TestProject.Controllers {
    [ApiController]
    [Route("api/files")]
    public class TestController : ControllerBase {

        private readonly ILogger<TestController> _logger;
        private readonly FileWorkspace workspace;

        public TestController(
            ILogger<TestController> logger,
            FileWorkspace workspace) {
            _logger = logger;
            this.workspace = workspace;
        }

        [HttpGet]
        public IActionResult Browse(string? path, CancellationToken token) =>
            Ok(workspace.List(path, null, token));

        [HttpGet("search")]
        public IActionResult Search(string? path, string q, CancellationToken token) =>
            Ok(workspace.List(path, q, token));

        [HttpGet("download")]
        public IActionResult Download(string path) {
            string fullPath = workspace.Resolve(path);
            if (Directory.Exists(fullPath)) return BadRequest();
            return File(System.IO.File.OpenRead(fullPath), "application/octet-stream", Path.GetFileName(fullPath));
        }

        [HttpPost("upload")]
        [RequestSizeLimit(11 * 1024 * 1024)]
        public async Task<IActionResult> Upload([FromQuery] string? path, [FromForm] IFormFile file, CancellationToken token) {
            if (file.Length > FileWorkspace.MaxUploadBytes)
                return Problem(statusCode: 413, title: "Choose a file of 10 MiB or less.");
            await workspace.Save(path, file, token);
            return Ok(new { name = file.FileName });
        }

        [HttpPost("create-folder")]
        public IActionResult CreateFolder([FromQuery] string? path, [FromForm] string name){
            workspace.CreateFolder(path, name);
            return Ok(new { name });
        }

        [HttpDelete]
        public IActionResult Delete([FromQuery] string path) {
            workspace.Delete(path);
            return NoContent();
        }
    }
}
