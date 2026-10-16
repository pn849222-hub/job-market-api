using JobMarketApi.Data;
using JobMarketApi.Models;
using Microsoft.Data.Sqlite;

namespace JobMarketApi.Tests;

/// <summary>
/// Each test builds its own throwaway SQLite file with a known set of postings,
/// so the assertions do not depend on whatever the live crawl happened to find.
/// </summary>
public sealed class JobRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"jobs-{Guid.NewGuid():N}.db");
    private readonly JobRepository _repo;

    public JobRepositoryTests()
    {
        Seed();
        _repo = new JobRepository($"Data Source={_dbPath};Mode=ReadOnly");
    }

    private void Seed()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE jobs (
                job_id TEXT PRIMARY KEY, title TEXT, company TEXT, city TEXT,
                district TEXT, seniority TEXT, employment_type TEXT,
                posted_date TEXT, valid_through TEXT, months_experience INTEGER,
                salary_currency TEXT, salary_min_usd REAL, salary_max_usd REAL,
                salary_raw TEXT, salary_disclosed INTEGER, source_url TEXT);
            CREATE TABLE job_skills (job_id TEXT, skill TEXT, PRIMARY KEY (job_id, skill));

            INSERT INTO jobs VALUES
              ('1','Senior Backend Developer','Acme','Hà Nội','Ba Đình','Senior','FULL_TIME',
               '2026-10-10',NULL,37,'USD',2000,3000,'2,000 - 3,000 USD',1,'https://example.test/1'),
              ('2','Junior Data Analyst','Beta','Hồ Chí Minh','Q1','Junior','FULL_TIME',
               '2026-10-12',NULL,10,'USD',NULL,NULL,'You will love it',0,'https://example.test/2'),
              ('3','DevOps Engineer','Acme','Hồ Chí Minh','Q3','Unspecified','FULL_TIME',
               '2026-10-11',NULL,37,'VND',800,1200,'21 - 32M',1,'https://example.test/3');

            INSERT INTO job_skills VALUES
              ('1','Python'),('1','PostgreSQL'),
              ('2','SQL'),('2','Python'),
              ('3','Docker');
            """;
        create.ExecuteNonQuery();
    }

    [Fact]
    public async Task GetStats_counts_postings_and_salary_disclosure()
    {
        var stats = await _repo.GetStatsAsync(CancellationToken.None);

        Assert.Equal(3, stats.TotalJobs);
        Assert.Equal(2, stats.JobsWithSalary);
        Assert.Equal(0.6667, stats.SalaryDisclosureRate, 4);
        Assert.Equal(2, stats.Companies);
        Assert.Equal(1400, stats.AverageSalaryMinUsd);
    }

    [Fact]
    public async Task GetStats_groups_cities_largest_first()
    {
        var stats = await _repo.GetStatsAsync(CancellationToken.None);

        Assert.Equal("Hồ Chí Minh", stats.Cities[0].City);
        Assert.Equal(2, stats.Cities[0].Jobs);
    }

    [Fact]
    public async Task GetJobs_without_filters_returns_every_posting_newest_first()
    {
        var result = await _repo.GetJobsAsync(new JobQuery(), CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal("2", result.Items[0].JobId);   // posted 2026-10-12
        Assert.False(result.HasNext);
    }

    [Fact]
    public async Task GetJobs_filters_by_skill_regardless_of_case()
    {
        var result = await _repo.GetJobsAsync(
            new JobQuery { Skill = "python" }, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, job => Assert.Contains("Python", job.Skills));
    }

    [Fact]
    public async Task GetJobs_can_exclude_postings_that_hide_the_salary()
    {
        var result = await _repo.GetJobsAsync(
            new JobQuery { WithSalaryOnly = true }, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, job => Assert.True(job.SalaryDisclosed));
    }

    [Fact]
    public async Task GetJobs_pages_results_and_reports_what_is_left()
    {
        var first = await _repo.GetJobsAsync(
            new JobQuery { PageSize = 2, Page = 1 }, CancellationToken.None);

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.TotalPages);
        Assert.True(first.HasNext);

        var second = await _repo.GetJobsAsync(
            new JobQuery { PageSize = 2, Page = 2 }, CancellationToken.None);

        Assert.Single(second.Items);
        Assert.False(second.HasNext);
    }

    [Fact]
    public async Task GetJobs_caps_an_oversized_page_size()
    {
        var result = await _repo.GetJobsAsync(
            new JobQuery { PageSize = 5_000 }, CancellationToken.None);

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task GetJobs_returns_the_skills_attached_to_each_posting()
    {
        var result = await _repo.GetJobsAsync(
            new JobQuery { Seniority = "Senior" }, CancellationToken.None);

        var job = Assert.Single(result.Items);
        Assert.Equal(["PostgreSQL", "Python"], job.Skills.Order());
    }

    [Fact]
    public async Task GetTopSkills_ranks_by_how_many_postings_name_each_skill()
    {
        var skills = await _repo.GetTopSkillsAsync(10, CancellationToken.None);

        Assert.Equal("Python", skills[0].Skill);
        Assert.Equal(2, skills[0].Jobs);
    }

    [Fact]
    public async Task IsReady_is_false_when_the_database_file_is_missing()
    {
        var missing = new JobRepository("Data Source=/nonexistent/jobs.db;Mode=ReadOnly");

        Assert.False(await missing.IsReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IsReady_is_true_for_a_populated_database()
    {
        Assert.True(await _repo.IsReadyAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
