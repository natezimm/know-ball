using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using KnowBall.Application;
using KnowBall.Application.Games;
using KnowBall.Domain;
using KnowBall.Infrastructure.Persistence;

namespace KnowBall.UnitTests;

/// <summary>
/// The rules in AGENTS.md that the compiler does not already hold: project references stop a layer
/// from naming a project above it, but not from taking a provider, ASP.NET Core or the clock.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(Player).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(ITriviaDb).Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = typeof(TriviaDbContext).Assembly;

    /// <summary>Our own types only: coverage instrumentation adds a tracker type that uses System.IO.</summary>
    private static PredicateList Ours(System.Reflection.Assembly assembly) =>
        Types.InAssembly(assembly).That().ResideInNamespaceStartingWith("KnowBall");

    private static void Holds(TestResult result, string rule) =>
        Assert.True(result.IsSuccessful, $"{rule}: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void The_domain_depends_on_nothing_but_the_framework() =>
        Holds(Ours(Domain).ShouldNot().HaveDependencyOnAny(
            "KnowBall.Application", "KnowBall.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "Microsoft.Extensions").GetResult(), "Domain types reach outside the domain");

    [Fact]
    public void Game_rules_reach_the_database_only_through_ITriviaDb() =>
        Holds(Ours(Application).ShouldNot().HaveDependencyOnAny(
            "KnowBall.Infrastructure", "Npgsql", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.Data.Sqlite").GetResult(),
            "Application types use a database provider or Infrastructure");

    [Fact]
    public void Application_has_no_web_or_outside_calls() =>
        Holds(Ours(Application).ShouldNot().HaveDependencyOnAny(
            "Microsoft.AspNetCore", "System.Net.Http").GetResult(),
            "Application types use ASP.NET Core or HttpClient; data source clients belong in Infrastructure");

    [Fact]
    public void Infrastructure_knows_nothing_of_the_hosts() =>
        Holds(Ours(Infrastructure).ShouldNot().HaveDependencyOnAny(
            "KnowBall.Api", "KnowBall.Importer", "Microsoft.AspNetCore").GetResult(),
            "Infrastructure types use the API, the importer or ASP.NET Core");

    [Fact]
    public void Every_daily_game_is_registered()
    {
        var written = Application.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IGame).IsAssignableFrom(t)).ToHashSet();
        // What each IGame registration hands GameRegistry: its type, or what its factory asks the container for.
        var resolved = new ServiceCollection().AddApplication()
            .Where(d => d.ServiceType == typeof(IGame))
            .Select(d => d.ImplementationType ?? d.ImplementationInstance?.GetType() ?? d.ImplementationFactory!(new Uninitialized()).GetType())
            .ToList();

        Assert.Equal(written.OrderBy(t => t.Name), resolved.OrderBy(t => t.Name));
    }

    /// <summary>Answers every request with a blank instance, so a factory shows which type it returns without its dependencies.</summary>
    private sealed class Uninitialized : IServiceProvider
    {
        public object GetService(Type serviceType) => RuntimeHelpers.GetUninitializedObject(serviceType);
    }

    /// <summary>
    /// Calls that read the real clock or make an unseeded random, which tests cannot hold still.
    /// <c>Random.Shared</c> is allowed: it is the default that <c>PuzzleScheduler.Random</c> and the
    /// Letter Jacket service take until a test passes its own.
    /// </summary>
    private static readonly string[] Unsteady =
    [
        "System.DateTime System.DateTime::get_Now()",
        "System.DateTime System.DateTime::get_UtcNow()",
        "System.DateTime System.DateTime::get_Today()",
        "System.DateTimeOffset System.DateTimeOffset::get_Now()",
        "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
        "System.Void System.Random::.ctor()",
    ];

    [Fact]
    public void Time_and_randomness_are_injected_in_the_domain_and_application()
    {
        var found = new List<string>();
        foreach (var assembly in new[] { Domain, Application })
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            // Only our own types: coverage instrumentation adds a tracker type of its own that reads the clock.
            foreach (var method in module.GetTypes().Where(t => Outermost(t).Namespace.StartsWith("KnowBall", StringComparison.Ordinal))
                         .SelectMany(t => t.Methods).Where(m => m.HasBody))
            {
                found.AddRange(method.Body.Instructions
                    .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Newobj)
                    .Select(i => i.Operand).OfType<MethodReference>()
                    .Where(called => Unsteady.Contains(called.FullName))
                    .Select(called => $"{method.DeclaringType.FullName}.{method.Name} calls {called.FullName}"));
            }
        }

        Assert.True(found.Count == 0, "Use TimeProvider or PuzzleScheduler.Random instead:\n" + string.Join("\n", found));
    }

    /// <summary>Async methods and lambdas compile to nested types, which carry no namespace of their own.</summary>
    private static TypeDefinition Outermost(TypeDefinition type) => type.DeclaringType is { } outer ? Outermost(outer) : type;
}
