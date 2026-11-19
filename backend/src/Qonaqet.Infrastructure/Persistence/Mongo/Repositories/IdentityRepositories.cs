using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class UserRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<User, UserId>(context, tracker, outbox, clock, CollectionNames.Users), IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var found = await FindAsync(Filter.Eq(x => x.Email, EmailAddress.Normalize(email)), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }
}

public sealed class OtpCodeRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<OtpCode, OtpCodeId>(context, tracker, outbox, clock, CollectionNames.OtpCodes), IOtpCodeRepository
{
    public Task<long> CountIssuedSinceAsync(string email, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(Filter.Eq(x => x.Email, email), Filter.Gte(x => x.CreatedAt, since)));
        return Context.Session is { } session
            ? Collection.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task<OtpCode?> ConsumeAttemptAsync(string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var usable = Active(Filter.And(
            Filter.Eq(x => x.Email, email),
            Filter.Eq(x => x.UsedAt, null),
            Filter.Gt(x => x.ExpiresAt, now),
            Filter.Lt(x => x.Attempts, OtpPolicy.MaxAttempts)));
        var options = new FindOneAndUpdateOptions<OtpCode>
        {
            Sort = Builders<OtpCode>.Sort.Descending(x => x.CreatedAt),
            ReturnDocument = ReturnDocument.After,
        };

        // Qəsdən sessiyasız: cəhd sayğacı əmrin tranzaksiyası rollback olsa da qalmalıdır.
        return await Collection.FindOneAndUpdateAsync(usable, Builders<OtpCode>.Update.Inc(x => x.Attempts, 1), options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryMarkUsedAsync(OtpCodeId id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(ById(id), Filter.Eq(x => x.UsedAt, null)));
        var update = Builders<OtpCode>.Update.Set(x => x.UsedAt, now).Set(x => x.UpdatedAt, now);
        var result = Context.Session is { } session
            ? await Collection.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }
}

/// <summary>Mongo kolleksiya adları (<c>CLAUDE.md</c> → "Collections").</summary>
public static class CollectionNames
{
    public const string Users = "users";
    public const string OtpCodes = "otpCodes";
    public const string RefreshTokens = "refreshTokens";
}
