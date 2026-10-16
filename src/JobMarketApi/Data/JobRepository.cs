using Dapper;
using JobMarketApi.Models;
using Microsoft.Data.Sqlite;

namespace JobMarketApi.Data;

/// <summary>
/// Reads the SQLite database produced by the vn-it-job-market Python pipeline.
/// The database is read-only here: the analysis side owns writes.
/// </summary>
public sealed class JobRepository(string connectionString) : IJobRepository
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public async Task<PagedResult<JobSummary>> GetJobsAsync(JobQuery query, CancellationToken ct)
    {
        // Filters are optional, so each clause is skipped when its parameter is
        // null. Values stay parameterised rather than concatenated.
        var where = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            where.Add("j.city = @City");
            parameters.Add("City", query.City);
        }

        if (!string.IsNullOrWhiteSpace(query.Seniority))
        {
            where.Add("j.seniority = @Seniority");
            parameters.Add("Seniority", query.Seniority);
        }

        if (query.WithSalaryOnly)
        {
            where.Add("j.salary_disclosed = 1");
        }

        if (!string.IsNullOrWhiteSpace(query.Skill))
        {
            where.Add("""
                EXISTS (SELECT 1 FROM job_skills s
                        WHERE s.job_id = j.job_id AND s.skill = @Skill COLLATE NOCASE)
                """);
            parameters.Add("Skill", query.Skill);
        }

        var filter = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(query.Page, 1);
        parameters.Add("Limit", pageSize);
        parameters.Add("Offset", (page - 1) * pageSize);

        await using var connection = Open();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition($"SELECT COUNT(*) FROM jobs j {filter}", parameters, cancellationToken: ct));

        // group_concat keeps this to one round trip instead of one query per job.
        var sql = $"""
            SELECT j.job_id          AS JobId,
                   j.title           AS Title,
                   j.company         AS Company,
                   j.city            AS City,
                   j.seniority       AS Seniority,
                   j.employment_type AS EmploymentType,
                   j.posted_date     AS PostedDate,
                   j.salary_min_usd  AS SalaryMinUsd,
                   j.salary_max_usd  AS SalaryMaxUsd,
                   j.salary_disclosed AS SalaryDisclosed,
                   j.source_url      AS SourceUrl,
                   (SELECT group_concat(s.skill, '|') FROM job_skills s
                     WHERE s.job_id = j.job_id) AS SkillList
            FROM jobs j
            {filter}
            ORDER BY j.posted_date DESC, j.job_id DESC
            LIMIT @Limit OFFSET @Offset
            """;

        var rows = await connection.QueryAsync<JobRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        var items = rows.Select(r => r.ToSummary()).ToList();
        return new PagedResult<JobSummary>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<SkillCount>> GetTopSkillsAsync(int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT skill AS Skill, COUNT(*) AS Jobs
            FROM job_skills
            GROUP BY skill
            ORDER BY Jobs DESC, Skill
            LIMIT @Limit
            """;

        await using var connection = Open();
        var rows = await connection.QueryAsync<SkillCount>(
            new CommandDefinition(sql, new { Limit = Math.Clamp(limit, 1, 100) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<MarketStats> GetStatsAsync(CancellationToken ct)
    {
        const string headline = """
            SELECT COUNT(*)                                      AS TotalJobs,
                   COALESCE(SUM(salary_disclosed), 0)            AS JobsWithSalary,
                   AVG(CASE WHEN salary_disclosed = 1 THEN salary_min_usd END) AS AverageSalaryMinUsd,
                   AVG(CASE WHEN salary_disclosed = 1 THEN salary_max_usd END) AS AverageSalaryMaxUsd,
                   COUNT(DISTINCT company)                       AS Companies
            FROM jobs
            """;

        const string byCity = """
            SELECT COALESCE(city, 'Not stated') AS City, COUNT(*) AS Jobs
            FROM jobs
            GROUP BY City
            ORDER BY Jobs DESC
            """;

        await using var connection = Open();

        var row = await connection.QuerySingleAsync<StatsRow>(
            new CommandDefinition(headline, cancellationToken: ct));
        var cities = (await connection.QueryAsync<CityCount>(
            new CommandDefinition(byCity, cancellationToken: ct))).ToList();

        var rate = row.TotalJobs > 0
            ? Math.Round(row.JobsWithSalary / (double)row.TotalJobs, 4)
            : 0;

        return new MarketStats(
            row.TotalJobs,
            row.JobsWithSalary,
            rate,
            Round(row.AverageSalaryMinUsd),
            Round(row.AverageSalaryMaxUsd),
            row.Companies,
            cities);
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = Open();
            var count = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM jobs", cancellationToken: ct));
            return count > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static double? Round(double? value) =>
        value is null ? null : Math.Round(value.Value, 2);

    /// <summary>Flat shape Dapper maps onto; the skill list arrives joined.</summary>
    private sealed record JobRow
    {
        public string JobId { get; init; } = string.Empty;
        public string? Title { get; init; }
        public string? Company { get; init; }
        public string? City { get; init; }
        public string? Seniority { get; init; }
        public string? EmploymentType { get; init; }
        public string? PostedDate { get; init; }
        public double? SalaryMinUsd { get; init; }
        public double? SalaryMaxUsd { get; init; }
        public long SalaryDisclosed { get; init; }
        public string? SourceUrl { get; init; }
        public string? SkillList { get; init; }

        public JobSummary ToSummary() => new(
            JobId, Title, Company, City, Seniority, EmploymentType, PostedDate,
            SalaryMinUsd, SalaryMaxUsd, SalaryDisclosed == 1, SourceUrl,
            string.IsNullOrEmpty(SkillList)
                ? Array.Empty<string>()
                : SkillList.Split('|', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record StatsRow
    {
        public int TotalJobs { get; init; }
        public int JobsWithSalary { get; init; }
        public double? AverageSalaryMinUsd { get; init; }
        public double? AverageSalaryMaxUsd { get; init; }
        public int Companies { get; init; }
    }
}
