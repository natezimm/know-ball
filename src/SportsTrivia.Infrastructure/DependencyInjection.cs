using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SportsTrivia.Application;
using SportsTrivia.Application.Games.Silhouette;
using SportsTrivia.Application.Import;
using SportsTrivia.Infrastructure.Images;
using SportsTrivia.Infrastructure.Persistence;
using SportsTrivia.Infrastructure.Sources;

namespace SportsTrivia.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database and data sources.
    /// Database:Provider = "Postgres" (default) or "Sqlite" (no Docker needed for quick local runs).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Postgres";
        var connectionString = config.GetConnectionString("Trivia")
                               ?? throw new InvalidOperationException("ConnectionStrings:Trivia is not set.");

        services.AddDbContext<TriviaDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString);
            else
                options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TriviaDbContext).Assembly.FullName));
        });
        services.AddScoped<ITriviaDb>(sp => sp.GetRequiredService<TriviaDbContext>());

        services.Configure<NbaStatsOptions>(config.GetSection("Sources:NbaStats"));
        services.Configure<SeedFileOptions>(config.GetSection("Sources:Seed"));
        services.Configure<EspnOptions>(config.GetSection("Sources:Espn"));
        services.Configure<BrefOptions>(config.GetSection("Sources:Bref"));
        services.Configure<WikidataOptions>(config.GetSection("Sources:Wikidata"));

        services.AddHttpClient<NbaStatsPlayerSource>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<NbaStatsOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            // stats.nba.com rejects requests that don't look like they come from nba.com.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            http.DefaultRequestHeaders.Referrer = new Uri("https://www.nba.com/");
            http.DefaultRequestHeaders.Add("Origin", "https://www.nba.com");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddHttpClient<EspnRosterSource>((sp, http) =>
        {
            http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<EspnOptions>>().Value.TimeoutSeconds);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddHttpClient<BrefCsvSource>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<BrefOptions>>().Value.TimeoutSeconds));
        services.AddHttpClient<WikidataCrosswalkSource>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<WikidataOptions>>().Value.TimeoutSeconds));

        services.AddScoped<IPlayerSource>(sp => sp.GetRequiredService<EspnRosterSource>());
        services.AddScoped<IPlayerSource>(sp => sp.GetRequiredService<NbaStatsPlayerSource>());
        services.AddScoped<IPlayerSource, SeedFilePlayerSource>();
        services.AddScoped<PlayerSourceResolver>();
        services.AddScoped<IHistorySource>(sp => sp.GetRequiredService<BrefCsvSource>());
        services.AddScoped<ICrosswalkSource>(sp => sp.GetRequiredService<WikidataCrosswalkSource>());

        services.Configure<MediaOptions>(config.GetSection("Media"));
        services.AddSingleton<LocalImageStore>();
        services.AddSingleton<IImageStore>(sp => sp.GetRequiredService<LocalImageStore>());
        services.AddSingleton<ISilhouetteRenderer, SilhouetteRenderer>();

        return services;
    }
}

public sealed class PlayerSourceResolver(IEnumerable<IPlayerSource> sources)
{
    public IReadOnlyList<string> Names => sources.Select(s => s.Name).ToList();

    public IPlayerSource Get(string name) =>
        sources.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown source '{name}'. Known sources: {string.Join(", ", Names)}");
}
