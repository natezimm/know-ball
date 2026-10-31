using System.Text.RegularExpressions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using KnowBall.Api.Endpoints;
using KnowBall.Application;
using KnowBall.Infrastructure;
using KnowBall.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddScoped<NumbersPlayService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
#if NET9_0_OR_GREATER
builder.Services.AddOpenApi();
#endif
builder.Services.AddHealthChecks().AddDbContextCheck<TriviaDbContext>("database");
builder.Services.AddApiRateLimits();

// Behind a reverse proxy, a visitor's address comes from X-Forwarded-For, but only on requests that
// arrive from a trusted proxy: loopback, plus the networks in ForwardedHeaders:KnownNetworks
// (CIDR, e.g. the Docker network nginx reaches the site through). Anyone else's header is ignored,
// so the per-address rate limits cannot be dodged by sending a made-up address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var cidr in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
#if NET10_0_OR_GREATER
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
#else
        options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(cidr));
#endif
});

// The site's scripts, styles and shell go out compressed: the main script is about 300 kB raw and
// 93 kB gzipped, and nothing in front of the API compresses it. API JSON is left alone; it is small.
builder.Services.AddResponseCompression(options =>
{
    // Requests arrive over HTTPS at nginx; these types carry no secrets, so compressing them is safe.
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ["text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml", "application/manifest+json"];
});
// Gzip at Optimal makes the main script 93 kB in about 9 ms; brotli came out no smaller.
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("GET", "POST", "DELETE")));

var app = builder.Build();

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
    await DatabaseInitializer.InitializeAsync(app.Services);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();

// When the built Angular app sits in wwwroot (the Docker image), this one process serves the whole site.
var servesSite = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"));
if (servesSite)
{
    app.UseResponseCompression();
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        // The build names scripts and styles by their content, so a browser can keep them for good.
        OnPrepareResponse = context =>
        {
            if (HashedBuildFile().IsMatch(context.File.Name))
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        },
    });
}

#if NET9_0_OR_GREATER
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
#endif

var api = app.MapGroup("/api");
api.MapData();
api.MapSpordle();
api.MapGames();
api.MapNumbers();

app.MapHealthChecks("/health");

// Angular handles its own routes, so any other page address gets the app shell. Addresses that
// look like files are left alone ("nonfile"): a route that matched them would stop the static
// file middleware from serving the real scripts, styles and fonts.
if (servesSite)
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|photos/|health).*$)}", "index.html");

app.Run();

public partial class Program
{
    /// <summary>Angular's content-hashed output, such as main-5F6GC5JD.js or chunk-a1fQ06g-.js.</summary>
    [GeneratedRegex(@"^(main|chunk|polyfills|styles)-[A-Za-z0-9_-]+\.(js|css)$")]
    private static partial Regex HashedBuildFile();
}
