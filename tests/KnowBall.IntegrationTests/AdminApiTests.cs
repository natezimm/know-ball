using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;

namespace KnowBall.IntegrationTests;

/// <summary>The admin area's API and the Who's That Sportsman? game it feeds, end to end with real images.</summary>
[Collection(ApiCollection.Name)]
public sealed class AdminApiTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureHistoryAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", ApiFactory.AdminKey);
        return client;
    }

    private HttpClient NewPlayer()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Anon-Id", Guid.NewGuid().ToString());
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>A figure on a plain background; with <paramref name="clear"/> the background is transparent.</summary>
    private static byte[] Image(bool clear)
    {
        using var bitmap = new SKBitmap(640, 480);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(clear ? SKColors.Transparent : SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawOval(320, 240, 90, 170, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(clear ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static MultipartFormDataContent Form(int playerId, byte[] photo, byte[] cutout, string year = "2000")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(playerId.ToString()), "playerId" },
            { new StringContent(year), "year" },
            { new StringContent("Team USA"), "team" },
            { new StringContent("The dunk over Frédéric Weis"), "caption" },
        };
        var photoPart = new ByteArrayContent(photo);
        photoPart.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(photoPart, "photo", "photo.jpg");
        var cutoutPart = new ByteArrayContent(cutout);
        cutoutPart.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(cutoutPart, "cutout", "cutout.png");
        return form;
    }

    private async Task<int> PlayerId(string name)
    {
        var results = await Json(await factory.CreateClient().GetAsync($"/api/players/search?q={Uri.EscapeDataString(name)}&includeInactive=true"));
        return results[0].GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task The_admin_area_needs_the_key()
    {
        var noKey = await factory.CreateClient().GetAsync("/api/admin/overview");
        var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Admin-Key", "guess");

        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/api/admin/whos-that/puzzles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Admin().GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task With_no_key_configured_the_admin_area_does_not_exist()
    {
        using var closed = factory.WithWebHostBuilder(b => b.UseSetting("Admin:Key", ""));
        var client = closed.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task The_overview_counts_queues_and_data_without_naming_answers()
    {
        await NewPlayer().GetAsync("/api/scouting-report/daily"); // makes today's puzzle

        var overview = await Json(await Admin().GetAsync("/api/admin/overview"));

        var scouting = overview.GetProperty("games").EnumerateArray().Single(g => g.GetProperty("game").GetString() == "scouting-report");
        Assert.True(scouting.GetProperty("generated").GetBoolean());
        Assert.True(scouting.GetProperty("daysQueued").GetInt32() >= 1);
        Assert.True(overview.GetProperty("data").GetProperty("players").GetInt32() >= 60);
        Assert.Contains(overview.GetProperty("imports").EnumerateArray(), run => run.GetProperty("source").GetString() == "bref");
        Assert.DoesNotContain("answer", overview.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_photo_becomes_a_puzzle_that_releases_its_hints_one_miss_at_a_time()
    {
        var admin = Admin();
        var vince = await PlayerId("Vince Carter");

        // ---- Make it and put it on today -------------------------------------------------
        var created = await admin.PostAsync("/api/admin/whos-that/puzzles", Form(vince, Image(clear: false), Image(clear: true)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await Json(created);
        Assert.Equal(("draft", "Vince Carter", 2000, "Team USA"),
            (draft.GetProperty("state").GetString(), draft.GetProperty("playerName").GetString(), draft.GetProperty("year").GetInt32(), draft.GetProperty("team").GetString()));
        var id = draft.GetProperty("id").GetInt32();
        var photoUrl = draft.GetProperty("photoUrl").GetString()!;
        var blurredUrl = draft.GetProperty("blurredUrl").GetString()!;

        var scheduled = await Json(await admin.PostAsJsonAsync($"/api/admin/whos-that/puzzles/{id}/schedule", new { }));
        Assert.Equal("published", scheduled.GetProperty("state").GetString());
        Assert.Contains((await Json(await admin.GetAsync("/api/admin/whos-that/puzzles"))).EnumerateArray(), p => p.GetProperty("id").GetInt32() == id);

        // ---- Play it ---------------------------------------------------------------------
        var player = NewPlayer();
        var state = await Json(await player.GetAsync("/api/whos-that/daily"));
        var puzzleId = state.GetProperty("puzzleId").GetInt32();
        Assert.Equal(id, puzzleId);

        var silhouette = await player.GetAsync(state.GetProperty("clues").GetProperty("silhouetteUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, silhouette.StatusCode);
        Assert.Equal("image/png", silhouette.Content.Headers.ContentType?.MediaType);
        using (var outline = SKBitmap.Decode(await silhouette.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal((0, 0, 0, 255), (outline.GetPixel(320, 240).Red, outline.GetPixel(320, 240).Green, outline.GetPixel(320, 240).Blue, outline.GetPixel(320, 240).Alpha));
            Assert.Equal(0, outline.GetPixel(4, 4).Alpha);
        }

        var wrong = new[] { await PlayerId("Tim Duncan"), await PlayerId("Kobe Bryant"), await PlayerId("Ray Allen") };
        for (var miss = 1; miss <= wrong.Length; miss++)
        {
            state = await Json(await player.PostAsJsonAsync($"/api/whos-that/{puzzleId}/guess", new { playerId = wrong[miss - 1] }));
            var clues = state.GetProperty("clues");
            var body = state.GetRawText();

            Assert.Equal(2000, clues.GetProperty("year").GetInt32());
            Assert.Equal(miss >= 2, clues.GetProperty("team").ValueKind == JsonValueKind.String);
            Assert.Equal(miss >= 3, clues.GetProperty("blurredUrl").ValueKind == JsonValueKind.String);
            Assert.Equal(miss >= 3, body.Contains(blurredUrl));
            Assert.DoesNotContain(photoUrl, body);
            Assert.DoesNotContain("Carter", body);
            Assert.DoesNotContain("Weis", body);
        }

        var won = await Json(await player.PostAsJsonAsync($"/api/whos-that/{puzzleId}/guess", new { playerId = vince }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal(("Vince Carter", photoUrl, "The dunk over Frédéric Weis"),
            (won.GetProperty("answer").GetProperty("name").GetString(), won.GetProperty("answer").GetProperty("photoUrl").GetString(), won.GetProperty("answer").GetProperty("caption").GetString()));
        Assert.Equal("image/jpeg", (await player.GetAsync(photoUrl)).Content.Headers.ContentType?.MediaType);

        // ---- A live puzzle is locked -----------------------------------------------------
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/whos-that/puzzles/{id}")).StatusCode);
    }

    [Fact]
    public async Task Uploads_that_cannot_make_a_puzzle_are_refused_with_a_reason()
    {
        var admin = Admin();
        var vince = await PlayerId("Vince Carter");

        var noCutout = await admin.PostAsync("/api/admin/whos-that/puzzles", Form(vince, Image(clear: false), Image(clear: false)));
        var notAnImage = await admin.PostAsync("/api/admin/whos-that/puzzles", Form(vince, "nope"u8.ToArray(), Image(clear: true)));
        var badYear = await admin.PostAsync("/api/admin/whos-that/puzzles", Form(vince, Image(clear: false), Image(clear: true), year: "soon"));

        Assert.Equal(HttpStatusCode.BadRequest, noCutout.StatusCode);
        Assert.Contains("no clear background", (await noCutout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, notAnImage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badYear.StatusCode);
    }

    [Fact]
    public async Task A_draft_can_be_scheduled_for_later_taken_back_and_deleted()
    {
        var admin = Admin();
        var created = await Json(await admin.PostAsync("/api/admin/whos-that/puzzles", Form(await PlayerId("Tim Duncan"), Image(clear: false), Image(clear: true))));
        var id = created.GetProperty("id").GetInt32();
        var later = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40);

        var scheduled = await Json(await admin.PostAsJsonAsync($"/api/admin/whos-that/puzzles/{id}/schedule", new { date = later.ToString("yyyy-MM-dd") }));
        Assert.Equal(("scheduled", later.ToString("yyyy-MM-dd")), (scheduled.GetProperty("state").GetString(), scheduled.GetProperty("date").GetString()));

        // Not playable before its day.
        Assert.Equal(HttpStatusCode.NotFound, (await NewPlayer().GetAsync($"/api/whos-that/puzzles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/whos-that/puzzles/{id}/move", new { direction = "sideways" })).StatusCode);

        var back = await Json(await admin.PostAsync($"/api/admin/whos-that/puzzles/{id}/unschedule", null));
        Assert.Equal("draft", back.GetProperty("state").GetString());

        var silhouetteUrl = created.GetProperty("silhouetteUrl").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/whos-that/puzzles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(silhouetteUrl)).StatusCode); // its images go with it
    }
}
