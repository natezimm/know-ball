using KnowBall.Application;
using KnowBall.Domain;
using KnowBall.Importer;
using KnowBall.Infrastructure;
using KnowBall.Infrastructure.Persistence;

// Offline preflight must run before host/database initialization.
if (args.Contains("--validate-numbers"))
{
    var path = GetArg(args, "--validate-numbers");
    if (path is null) { Console.Error.WriteLine("--validate-numbers requires a JSON file."); return 2; }
    try
    {
        using var catalogue = KnowBall.Application.Numbers.QuizCatalogue.Read(await File.ReadAllTextAsync(path));
        var errors = KnowBall.Application.Numbers.QuizCatalogue.Validate(catalogue.RootElement, args.Contains("--require-approved"));
        if (errors.Count != 0) { foreach (var error in errors) Console.Error.WriteLine(error); return 1; }
        Console.WriteLine($"Valid catalogue: {catalogue.RootElement.GetProperty("questions").GetArrayLength()} questions. No database writes.");
        return 0;
    }
    catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
    { Console.Error.WriteLine(e.Message); return 1; }
}

// Usage:
//   dotnet run --project src/KnowBall.Importer                       → worker: runs nightly at Import:DailyAt
//   dotnet run --project src/KnowBall.Importer -- --once             → one run, then exit (0 = the roster import succeeded)
//   dotnet run --project src/KnowBall.Importer -- --once --source seed --no-history
//   dotnet run --project src/KnowBall.Importer -- --once --history   → reload the full player history first, due or not
var importNumbers = GetArg(args, "--import-numbers");
if (args.Contains("--import-numbers") && (string.IsNullOrWhiteSpace(importNumbers) || importNumbers.StartsWith("--")))
{ Console.Error.WriteLine("--import-numbers requires an approved JSON file."); return 2; }
var once = args.Contains("--once") || importNumbers is not null;
var forceHistory = args.Contains("--history");
var sourceArg = GetArg(args, "--source");
var hostArgs = StripOwnArgs(args);

// Content root = the app folder, so appsettings.json is found no matter where it's started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = hostArgs, ContentRootPath = AppContext.BaseDirectory });
if (sourceArg is not null) builder.Configuration["Import:Source"] = sourceArg;
if (args.Contains("--no-history")) builder.Configuration["Import:HistoryRefreshDays"] = "0";

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection("Import"));
builder.Services.AddSingleton<ImportJob>();

if (!once) builder.Services.AddHostedService<NightlyImportWorker>();

var host = builder.Build();
await DatabaseInitializer.InitializeAsync(host.Services);

if (importNumbers is not null)
{
    try
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = new NumbersContentStore(scope.ServiceProvider.GetRequiredService<TriviaDbContext>(), TimeProvider.System);
        var catalogue = await store.ImportAsync(await File.ReadAllTextAsync(importNumbers));
        Console.WriteLine($"Imported approved Numbers catalogue {catalogue.Id}.");
        return 0;
    }
    catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
    { Console.Error.WriteLine(e.Message); return 1; }
}

if (once)
{
    var run = await host.Services.GetRequiredService<ImportJob>().RunAsync(CancellationToken.None, forceHistory);
    Console.WriteLine(ImportJob.Describe(run));
    return run.Status == ImportStatus.Succeeded ? 0 : 1;
}

await host.RunAsync();
return 0;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == name && i + 1 < args.Length) return args[i + 1];
        if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
    }
    return null;
}

// --once, --source, --history and --no-history are ours; everything else (e.g. --Import:DailyAt=03:00) goes to configuration.
static string[] StripOwnArgs(string[] args)
{
    var rest = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--import-numbers=", StringComparison.Ordinal)) continue;
        if (args[i] == "--import-numbers") { i++; continue; }
        if (args[i] is "--once" or "--history" or "--no-history" || args[i].StartsWith("--source=", StringComparison.Ordinal)) continue;
        if (args[i] == "--source") { i++; continue; }
        rest.Add(args[i]);
    }
    return rest.ToArray();
}
