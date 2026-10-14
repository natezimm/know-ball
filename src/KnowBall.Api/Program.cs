using Microsoft.Extensions.FileProviders;
using KnowBall.Api.Endpoints;
using KnowBall.Application;
using KnowBall.Infrastructure;
using KnowBall.Infrastructure.Images;
using KnowBall.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
#if NET9_0_OR_GREATER
builder.Services.AddOpenApi();
#endif
builder.Services.AddHealthChecks().AddDbContextCheck<TriviaDbContext>("database");
builder.Services.AddApiRateLimits();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("GET", "POST", "DELETE")));

var app = builder.Build();

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
    await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();

// Puzzle images made in the admin area.
var media = app.Services.GetRequiredService<LocalImageStore>();
Directory.CreateDirectory(media.Root);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(media.Root),
    RequestPath = app.Configuration["Media:RequestPath"] ?? "/photos",
});

// When the built Angular app sits in wwwroot (the Docker image), this one process serves the whole site.
var servesSite = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"));
if (servesSite)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

#if NET9_0_OR_GREATER
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
#endif

var api = app.MapGroup("/api");
api.MapData();
api.MapSpordle();
api.MapGames();
api.MapAdmin();

app.MapHealthChecks("/health");

// Angular handles its own routes, so any other page address gets the app shell. Addresses that
// look like files are left alone ("nonfile"): a route that matched them would stop the static
// file middleware from serving the real scripts, styles and fonts.
if (servesSite)
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|photos/|health).*$)}", "index.html");

app.Run();

public partial class Program { }
