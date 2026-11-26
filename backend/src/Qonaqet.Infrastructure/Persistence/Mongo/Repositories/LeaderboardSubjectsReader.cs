using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Users;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

/// <summary>
/// Lövhə qazananlarının adları, görünmə qaydaları sorğunun özündədir (<c>isDeleted: false</c> həmişə). İstifadəçidə <c>leaderboardOptIn</c>
/// sahəsi olmayan köhnə sənəd açıq sayılır (<c>$ne: false</c>).
/// </summary>
public sealed class LeaderboardSubjectsReader(IMongoDatabase database) : ILeaderboardSubjects
{
    public async Task<IReadOnlyDictionary<Guid, LeaderboardSubject>> VisibleAsync(
        ScoreSubjectType subjectType, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, LeaderboardSubject>();
        }

        var filter = new BsonDocument
        {
            { "_id", new BsonDocument("$in", new BsonArray(ids.Select(id => new BsonBinaryData(id, GuidRepresentation.Standard)))) },
            { "isDeleted", false },
        };
        IReadOnlyList<LeaderboardSubject> subjects;
        switch (subjectType)
        {
            case ScoreSubjectType.User:
                filter.Add("isBlocked", new BsonDocument("$ne", true));
                filter.Add("leaderboardOptIn", new BsonDocument("$ne", false));
                var users = await database.GetCollection<User>(CollectionNames.Users).Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
                subjects = [.. users.Select(u => new LeaderboardSubject(u.Id.Value, u.LeaderboardDisplayName, null))];
                break;
            case ScoreSubjectType.Business:
                filter.Add("status", nameof(BusinessStatus.Active));
                filter.Add("isSandbox", new BsonDocument("$ne", true));
                var businesses = await database.GetCollection<Business>(BusinessCollectionNames.Businesses).Find(filter)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                subjects = [.. businesses.Select(b => new LeaderboardSubject(b.Id.Value, b.Name, b.Slug))];
                break;
            case ScoreSubjectType.Corporate:
                var corporates = await database.GetCollection<Corporate>(CorporateCollectionNames.Corporates).Find(filter)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                subjects = [.. corporates.Select(c => new LeaderboardSubject(c.Id.Value, c.Name, null))];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(subjectType), subjectType, null);
        }

        return subjects.ToDictionary(s => s.Id);
    }
}
