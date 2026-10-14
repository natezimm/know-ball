using Microsoft.Extensions.DependencyInjection;
using KnowBall.Application.Games;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Games.Silhouette;
using KnowBall.Application.Games.Spordle;
using KnowBall.Application.Import;
using KnowBall.Application.Players;

namespace KnowBall.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PlayerImportService>();
        services.AddScoped<HistoryImportService>();
        services.AddScoped<CrosswalkImportService>();
        services.AddScoped<PlayerSearchService>();
        services.AddSingleton(_ => SpordleWordBank.LoadEmbedded());
        services.AddSingleton<SpordleGameService>();

        // The daily games, in the order the home page shows them.
        services.AddScoped<ScoutingReportGame>();
        services.AddScoped<CareerPathGame>();
        services.AddScoped<WhosThatGame>();
        services.AddScoped<IGame>(sp => sp.GetRequiredService<ScoutingReportGame>());
        services.AddScoped<IGame>(sp => sp.GetRequiredService<CareerPathGame>());
        services.AddScoped<IGame>(sp => sp.GetRequiredService<WhosThatGame>());
        services.AddScoped<GameRegistry>();
        services.AddSingleton<PuzzleCalendar>();
        services.AddScoped<PuzzleScheduler>();
        services.AddScoped<GameService>();
        services.AddScoped<CuratedPuzzleService>();
        return services;
    }
}
