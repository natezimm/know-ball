using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KnowBall.Application.Games.Silhouette;

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
        var upload = Form(vince, Image(clear: false), Image(clear: true));
        upload.Add(new StringContent("Example photographer"), "credit");
        var created = await admin.PostAsync("/api/admin/whos-that/puzzles", upload);
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
            Assert.DoesNotContain("Example photographer", body);
        }

        var won = await Json(await player.PostAsJsonAsync($"/api/whos-that/{puzzleId}/guess", new { playerId = vince }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal(("Vince Carter", photoUrl, "The dunk over Frédéric Weis"),
            (won.GetProperty("answer").GetProperty("name").GetString(), won.GetProperty("answer").GetProperty("photoUrl").GetString(), won.GetProperty("answer").GetProperty("caption").GetString()));
        Assert.Equal("image/jpeg", (await player.GetAsync(photoUrl)).Content.Headers.ContentType?.MediaType);
        Assert.Equal("Example photographer", won.GetProperty("answer").GetProperty("attribution").GetProperty("credit").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/admin/whos-that/puzzles/{id}", Metadata(vince))).StatusCode);

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
    private static MultipartFormDataContent Metadata(int playerId, string source = "https://example.com/photo") => new()
    {
        { new StringContent(playerId.ToString()), "playerId" },
        { new StringContent("2001"), "year" },
        { new StringContent("San Antonio Spurs"), "team" },
        { new StringContent("An edited caption"), "caption" },
        { new StringContent(source), "sourceUrl" },
        { new StringContent("Example photographer"), "credit" },
        { new StringContent("Used with permission"), "license" },
    };

    [Fact]
    public async Task Editing_a_draft_keeps_its_id_and_images_and_previews_without_player_results()
    {
        var admin = Admin();
        var vince = await PlayerId("Vince Carter");
        var tim = await PlayerId("Tim Duncan");
        var draft = await Json(await admin.PostAsync("/api/admin/whos-that/puzzles", Form(vince, Image(false), Image(true))));
        var id = draft.GetProperty("id").GetInt32();
        var path = $"/api/admin/whos-that/puzzles/{id}";
        var edited = await Json(await admin.PutAsync(path, Metadata(tim)));
        Assert.Equal(id, edited.GetProperty("id").GetInt32());
        Assert.Equal("Tim Duncan", edited.GetProperty("playerName").GetString());
        Assert.Equal("draft", edited.GetProperty("state").GetString());
        foreach (var image in new[] { "silhouetteUrl", "blurredUrl", "photoUrl" })
            Assert.Equal(draft.GetProperty(image).GetString(), edited.GetProperty(image).GetString());

        var player = NewPlayer();
        var statsBefore = await player.GetStringAsync("/api/me/stats");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowBall.Application.ITriviaDb>();
        var resultsBefore = await db.UserResults.CountAsync();
        for (var misses = 0; misses <= 4; misses++)
        {
            var preview = await Json(await admin.GetAsync($"{path}/preview?misses={misses}"));
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("answer").ValueKind);
            var clues = preview.GetProperty("clues");
            Assert.Equal(misses >= 1, clues.GetProperty("year").ValueKind == JsonValueKind.Number);
            Assert.Equal(misses >= 2, clues.GetProperty("team").ValueKind == JsonValueKind.String);
            Assert.Equal(misses >= 3, clues.GetProperty("blurredUrl").ValueKind == JsonValueKind.String);
            Assert.DoesNotContain("Example photographer", preview.GetRawText());
            Assert.DoesNotContain(edited.GetProperty("photoUrl").GetString()!, preview.GetRawText());
        }
        var reveal = await Json(await admin.GetAsync($"{path}/preview?reveal=true"));
        Assert.Equal("https://example.com/photo", reveal.GetProperty("answer").GetProperty("attribution").GetProperty("sourceUrl").GetString());
        Assert.Equal("Tim Duncan", reveal.GetProperty("answer").GetProperty("name").GetString());
        Assert.Equal(resultsBefore, await db.UserResults.CountAsync());
        Assert.Equal(statsBefore, await player.GetStringAsync("/api/me/stats"));
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/api/whos-that/puzzles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"{path}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsync(path, Metadata(tim))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{path}/preview?misses=5")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/whos-that/puzzles/2147483647/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsync("/api/admin/whos-that/puzzles/2147483647", Metadata(tim))).StatusCode);

        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(110);
        await Json(await admin.PostAsJsonAsync($"{path}/schedule", new { date = future.ToString("yyyy-MM-dd") }));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync(path, Metadata(vince))).StatusCode);
        await Json(await admin.PostAsync($"{path}/unschedule", null));
        Assert.True((await admin.PutAsync(path, Metadata(tim))).IsSuccessStatusCode);
        await admin.DeleteAsync(path);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,no")]
    [InlineData("https://user:secret@example.com/photo")]
    [InlineData("not a URL")]
    public async Task Unsafe_attribution_is_rejected_on_create_and_edit(string source)
    {
        var admin = Admin();
        var tim = await PlayerId("Tim Duncan");
        var form = Form(tim, Image(false), Image(true));
        form.Add(new StringContent(source), "sourceUrl");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/whos-that/puzzles", form)).StatusCode);
        var draft = await Json(await admin.PostAsync("/api/admin/whos-that/puzzles", Form(tim, Image(false), Image(true))));
        var path = $"/api/admin/whos-that/puzzles/{draft.GetProperty("id").GetInt32()}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync(path, Metadata(tim, source))).StatusCode);
        await admin.DeleteAsync(path);
    }

    [Fact]
    public async Task Draft_image_replacement_is_optional_but_must_be_a_valid_pair()
    {
        var admin = Admin();
        var tim = await PlayerId("Tim Duncan");
        var draft = await Json(await admin.PostAsync("/api/admin/whos-that/puzzles", Form(tim, Image(false), Image(true))));
        var path = $"/api/admin/whos-that/puzzles/{draft.GetProperty("id").GetInt32()}";
        var incomplete = Metadata(tim);
        incomplete.Add(new ByteArrayContent(Image(false)), "photo", "photo.jpg");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync(path, incomplete)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync(path, Form(tim, Image(false), Image(false)))).StatusCode);
        var unchanged = await Json(await admin.GetAsync($"{path}/preview?reveal=true"));
        Assert.Equal(draft.GetProperty("photoUrl").GetString(), unchanged.GetProperty("answer").GetProperty("photoUrl").GetString());
        var updated = await Json(await admin.PutAsync(path, Form(tim, Image(false), Image(true))));
        foreach (var image in new[] { "photoUrl", "silhouetteUrl", "blurredUrl" })
        {
            Assert.NotEqual(draft.GetProperty(image).GetString(), updated.GetProperty(image).GetString());
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(updated.GetProperty(image).GetString())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(draft.GetProperty(image).GetString())).StatusCode);
        }
        await admin.DeleteAsync(path);
    }

    [Fact]
    public async Task Scheduling_during_image_rendering_rejects_the_edit_and_removes_only_new_images()
    {
        var admin = Admin();
        var tim = await PlayerId("Tim Duncan");
        var draft = await Json(await admin.PostAsync("/api/admin/whos-that/puzzles", Form(tim, Image(false), Image(true))));
        var path = $"/api/admin/whos-that/puzzles/{draft.GetProperty("id").GetInt32()}";
        var renderer = new PausedRenderer(factory.Services.GetRequiredService<ISilhouetteRenderer>());
        var store = new RecordingImages(factory.Services.GetRequiredService<IImageStore>());
        using var concurrent = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton<ISilhouetteRenderer>(renderer);
            services.AddSingleton<IImageStore>(store);
        }));
        var editor = concurrent.CreateClient();
        editor.DefaultRequestHeaders.Add("X-Admin-Key", ApiFactory.AdminKey);
        var pending = editor.PutAsync(path, Form(tim, Image(false), Image(true)));
        try
        {
            await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(130).ToString("yyyy-MM-dd");
            await Json(await admin.PostAsJsonAsync($"{path}/schedule", new { date }));
        }
        finally { renderer.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.Equal(3, store.Saved.Count);
        Assert.Equal(store.Saved, store.Deleted);
        foreach (var url in store.Saved) Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
        var preview = await Json(await admin.GetAsync($"{path}/preview?reveal=true"));
        Assert.Equal(draft.GetProperty("photoUrl").GetString(), preview.GetProperty("answer").GetProperty("photoUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(draft.GetProperty("photoUrl").GetString())).StatusCode);
        await admin.PostAsync($"{path}/unschedule", null);
        await admin.DeleteAsync(path);
    }

    private sealed class PausedRenderer(ISilhouetteRenderer inner) : ISilhouetteRenderer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SilhouetteImages> RenderAsync(Stream photo, Stream cutout, CancellationToken ct)
        {
            Started.TrySetResult();
            await Resume.Task.WaitAsync(ct);
            return await inner.RenderAsync(photo, cutout, ct);
        }
    }

    private sealed class RecordingImages(IImageStore inner) : IImageStore
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct)
        {
            var url = await inner.SaveAsync(content, extension, ct);
            Saved.Add(url);
            return url;
        }
        public Task DeleteAsync(string url, CancellationToken ct)
        {
            Deleted.Add(url);
            return inner.DeleteAsync(url, ct);
        }
    }

}
