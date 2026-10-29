using KnowBall.Infrastructure.Persistence;

namespace KnowBall.Api.Endpoints;

public static class NumbersEndpoints
{
    public static IEndpointRouteBuilder MapNumbers(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/numbers-dont-lie").RequireRateLimiting(RateLimits.GamePlay);
        group.MapGet("/daily", (HttpContext http, NumbersPlayService service, CancellationToken ct) =>
            Handle(http, user => service.Daily(user, ct)));
        group.MapGet("/quizzes/{id:guid}", (Guid id, HttpContext http, NumbersPlayService service, CancellationToken ct) =>
            Handle(http, user => service.Get(user, id, ct)));
        group.MapPost("/practice", (NumbersStart request, HttpContext http, NumbersPlayService service, CancellationToken ct) =>
            Handle(http, user => service.Start(user, request, ct)));
        group.MapPost("/practice/coverage", (NumbersStart request, HttpContext http, NumbersPlayService service, CancellationToken ct) =>
            Handle(http, user => service.Coverage(user, request, ct)));
        group.MapPost("/quizzes/{id:guid}/answers", (Guid id, NumbersMove request, HttpContext http, NumbersPlayService service, CancellationToken ct) =>
            Handle(http, user => service.Move(user, id, request, false, ct)));
        group.MapPost("/quizzes/{id:guid}/continue", (Guid id, NumbersMove request, HttpContext http, NumbersPlayService service, CancellationToken ct) =>
            Handle(http, user => service.Move(user, id, request, true, ct)));
        return api;
    }
    private static async Task<IResult> Handle(HttpContext http, Func<string, Task<object>> action)
    {
        http.Response.Headers.CacheControl = "no-store";
        var id = http.Request.Headers["X-Anon-Id"].ToString();
        if (id.Length is < 8 or > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            return Results.BadRequest(new { error = "A valid anonymous player ID is required." });
        try { return Results.Ok(await action(id)); }
        catch (NumbersFailure e) { return Results.Json(new { error = e.Message }, statusCode: e.Status); }
    }
}
