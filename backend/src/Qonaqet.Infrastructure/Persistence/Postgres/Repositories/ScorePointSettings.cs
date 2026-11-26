using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary><c>score_point_settings</c>: hər admin dəyişikliyi yeni sətirdir; <c>created_at ≤ at</c> olan ən sonuncu qüvvədədir.</summary>
public sealed class ScorePointSettings(QonaqetDbContext context, IOptions<LeaderboardOptions> options) : IScorePointSettings
{
    public Task<ScorePointsVersion> AtAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        AtAsync(context, options.Value.Points, at, cancellationToken);

    public async Task<ScorePointsVersion> SetAsync(ScorePoints points, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        var record = ScorePointSettingRecord.From(points, actorId, now);
        context.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record.ToVersion();
    }

    /// <summary>Consumer da bunu öz tranzaksiyasının kontekstində çağırır: təhvilə onun anında qüvvədə olan dəyərlər tətbiq olunur.</summary>
    public static async Task<ScorePointsVersion> AtAsync(QonaqetDbContext context, ScorePoints defaults, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var latest = await context.Set<ScorePointSettingRecord>().AsNoTracking()
            .Where(x => x.CreatedAt <= at)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return latest?.ToVersion() ?? new ScorePointsVersion(defaults, null, null);
    }
}
