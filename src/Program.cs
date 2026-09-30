using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/v1/openapi/v1.json");
    app.MapScalarApiReference(
        "/api/v1/docs",
        (options) =>
        {
            options.WithOpenApiRoutePattern("/api/v1/openapi/v1.json");
            options.WithTheme(ScalarTheme.DeepSpace);
            options.WithTitle("E-Commerce API");
            options.WithYamlDocumentDownload();
        }
    );
}

app.UseAuthorization();
app.MapControllers();
app.Run();
