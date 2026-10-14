using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SportsTrivia.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef migrations add` run without starting the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TriviaDbContext>
{
    public TriviaDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Trivia")
                 ?? "Host=localhost;Port=5432;Database=sports_trivia;Username=trivia;Password=trivia";
        var options = new DbContextOptionsBuilder<TriviaDbContext>().UseNpgsql(cs).Options;
        return new TriviaDbContext(options);
    }
}
