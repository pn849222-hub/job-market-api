using JobMarketApi.Models;

namespace JobMarketApi.Data;

public interface IJobRepository
{
    Task<PagedResult<JobSummary>> GetJobsAsync(JobQuery query, CancellationToken ct);

    Task<IReadOnlyList<SkillCount>> GetTopSkillsAsync(int limit, CancellationToken ct);

    Task<MarketStats> GetStatsAsync(CancellationToken ct);

    /// <summary>True when the database is reachable and holds data.</summary>
    Task<bool> IsReadyAsync(CancellationToken ct);
}
