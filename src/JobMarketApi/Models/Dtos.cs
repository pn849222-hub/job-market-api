namespace JobMarketApi.Models;

/// <summary>A single job posting, as returned by the listing endpoint.</summary>
public record JobSummary(
    string JobId,
    string? Title,
    string? Company,
    string? City,
    string? Seniority,
    string? EmploymentType,
    string? PostedDate,
    double? SalaryMinUsd,
    double? SalaryMaxUsd,
    bool SalaryDisclosed,
    string? SourceUrl,
    IReadOnlyList<string> Skills
);

/// <summary>How many open postings name a given skill.</summary>
/// <remarks>
/// Written with init properties rather than a positional constructor: SQLite
/// returns COUNT(*) as Int64, which Dapper converts when setting a property but
/// not when matching a constructor signature.
/// </remarks>
public record SkillCount
{
    public string Skill { get; init; } = string.Empty;
    public int Jobs { get; init; }
}

/// <summary>Headline numbers for the whole dataset.</summary>
public record MarketStats(
    int TotalJobs,
    int JobsWithSalary,
    double SalaryDisclosureRate,
    double? AverageSalaryMinUsd,
    double? AverageSalaryMaxUsd,
    int Companies,
    IReadOnlyList<CityCount> Cities
);

/// <summary>Posting count for one city. Init properties for the same reason as
/// <see cref="SkillCount"/>.</summary>
public record CityCount
{
    public string City { get; init; } = string.Empty;
    public int Jobs { get; init; }
}

/// <summary>One page of results plus the paging metadata a client needs.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;
    public bool HasNext => Page < TotalPages;
}

/// <summary>Filters accepted by the listing endpoint.</summary>
public record JobQuery
{
    public string? City { get; init; }
    public string? Skill { get; init; }
    public string? Seniority { get; init; }
    public bool WithSalaryOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
