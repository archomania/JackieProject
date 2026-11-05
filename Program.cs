using TestProject.Services;

namespace TestProject {
  public class Program {
    public static void Main(string[] args) {
      var builder = WebApplication.CreateBuilder(args);

      builder.Services.AddControllers();

      builder.Services.AddSingleton<FileWorkspace>(); 

      var app = builder.Build();

      app.UseHttpsRedirection();

      app.UseDefaultFiles();

      app.UseStaticFiles(new StaticFileOptions
      {
        OnPrepareResponse = context =>
        {
          if (app.Environment.IsDevelopment())
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        }
      });
      
      app.Use(async (context, next) =>
      {
        try { await next(); }
        catch (Exception ex) when (!context.Response.HasStarted && ex is not OperationCanceledException) 
        {
            var (status, title) = ex switch 
            {
                BadHttpRequestException error => (error.StatusCode, error.Message),
                ArgumentException => (400, "Invalid path or file name."),
                FileNotFoundException or DirectoryNotFoundException => (404, "Item not found."),
                UnauthorizedAccessException => (403, "Access denied."),
                _ => (500, "Could not complete the file operation.")
            };
            if (status == 500) app.Logger.LogError(ex, "File operation failed.");
            await Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
          }
        });

      app.MapControllers();

      app.Run();
    }
  }
}