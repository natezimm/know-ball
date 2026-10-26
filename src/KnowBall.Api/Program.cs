using Microsoft.AspNetCore.HttpOverrides;
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
api.MapNumbers();

app.MapHealthChecks("/health");

// Angular handles its own routes, so any other page address gets the app shell. Addresses that
// look like files are left alone ("nonfile"): a route that matched them would stop the static
// file middleware from serving the real scripts, styles and fonts.
if (servesSite)
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|photos/|health).*$)}", "index.html");

app.Run();

public partial class Program { }
