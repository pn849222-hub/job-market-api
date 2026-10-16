using JobMarketApi.Data;
using JobMarketApi.Models;
using Microsoft.AspNetCore.Mvc;

// Docker's HEALTHCHECK runs the same image with this flag: the container probes
// itself over HTTP and the exit code is the verdict, so no curl in the image.
if (args.Contains("--healthcheck"))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

// The database is built by the companion Python pipeline (vn-it-job-market) and
// mounted read-only, so the API never writes to it.
var databasePath = builder.Configuration["Database:Path"]
                   ?? Path.Combine(AppContext.BaseDirectory, "data", "jobs.db");
var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
    Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared
}.ToString();

builder.Services.AddSingleton<IJobRepository>(_ => new JobRepository(connectionString));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Vietnam IT Job Market API",
        Version = "v1",
        Description = "Read-only access to IT job postings collected and cleaned "
                      + "by the vn-it-job-market pipeline."
    });
});
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1"));

// Browsing the root should land on the documentation rather than a 404.
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health")
   .WithSummary("Liveness probe: the process is up.");

app.MapGet("/health/ready", async (IJobRepository repo, CancellationToken ct) =>
        await repo.IsReadyAsync(ct)
            ? Results.Ok(new { status = "ready" })
            : Results.Problem("Job database is unavailable or empty.", statusCode: 503))
   .WithName("Readiness")
   .WithSummary("Readiness probe: the job database is reachable and populated.");

app.MapGet("/api/jobs", async (
        [FromQuery] string? city,
        [FromQuery] string? skill,
        [FromQuery] string? seniority,
        // Nullable so the filters stay optional: a non-nullable value type bound
        // from the query string is treated as required and 400s when omitted.
        [FromQuery] bool? withSalaryOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        IJobRepository repo,
        CancellationToken ct) =>
    {
        var query = new JobQuery
        {
            City = city,
            Skill = skill,
            Seniority = seniority,
            WithSalaryOnly = withSalaryOnly ?? false,
            Page = page is > 0 ? page.Value : 1,
            PageSize = pageSize is > 0 ? pageSize.Value : 20
        };
        return Results.Ok(await repo.GetJobsAsync(query, ct));
    })
   .WithName("ListJobs")
   .WithSummary("List postings, filtered by city, skill or seniority.");

app.MapGet("/api/skills/top", async (
        [FromQuery] int? limit,
        IJobRepository repo,
        CancellationToken ct) =>
        Results.Ok(await repo.GetTopSkillsAsync(limit is > 0 ? limit.Value : 20, ct)))
   .WithName("TopSkills")
   .WithSummary("Skills ranked by how many open postings name them.");

app.MapGet("/api/stats", async (IJobRepository repo, CancellationToken ct) =>
        Results.Ok(await repo.GetStatsAsync(ct)))
   .WithName("MarketStats")
   .WithSummary("Headline numbers: posting count, salary disclosure rate, city mix.");

await app.RunAsync();
return 0;

/// <summary>Self-probe used by the container health check.</summary>
internal static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(':').Last()
                   ?? "8080";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var response = await client.GetAsync($"http://localhost:{port}/health/ready");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}

/// <summary>Exposed so the test project can spin the app up in memory.</summary>
public partial class Program;
