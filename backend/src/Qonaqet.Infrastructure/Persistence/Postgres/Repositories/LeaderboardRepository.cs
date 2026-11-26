using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Leaderboard.Common;
using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary><c>leaderboard_entries</c> EF Core üzərində; <c>(subject_type, period_type, period_key, points DESC)</c> indeksindən istifadə edir.</summary>
public sealed class LeaderboardRepository(QonaqetDbContext context) : ILeaderboardRepository
{
    public async Task<IReadOnlyList<LeaderboardScore>> ScoresAsync(LeaderboardBoard board, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        return await context.Set<LeaderboardEntry>().AsNoTracking()
            .Where(e => e.SubjectType == board.SubjectType && e.PeriodType == board.PeriodType && e.PeriodKey == board.PeriodKey)
            .OrderByDescending(e => e.Points)
            .Select(e => new LeaderboardScore(e.SubjectId, e.Points))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Reytinq sahəsinin DI qeydiyyatı (keş store-u <c>AddValkey</c>-dədir; adlar Mongo-dan oxunur).</summary>
public static class LeaderboardRegistration
{
    public static IServiceCollection AddLeaderboard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ILeaderboardRepository, LeaderboardRepository>();
        services.AddScoped<LeaderboardReader>();
        services.AddScoped<ILeaderboardSubjects, Mongo.Repositories.LeaderboardSubjectsReader>();
        return services;
    }
}
