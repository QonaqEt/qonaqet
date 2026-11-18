using MongoDB.Driver;

namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary>
/// Sorğu (scope) səviyyəli Mongo konteksti: verilənlər bazası və cari tranzaksiya sessiyası.
/// <see cref="MongoUnitOfWork"/> sessiyanı açır; repository və outbox eyni sessiyada yazır ki,
/// dəyişiklik və onun hadisəsi bir tranzaksiyada commit olunsun.
/// </summary>
public sealed class MongoContext(IMongoClient client, IMongoDatabase database)
{
    public IMongoClient Client { get; } = client;

    public IMongoDatabase Database { get; } = database;

    /// <summary>Aktiv tranzaksiya sessiyası; yoxdursa <c>null</c>.</summary>
    public IClientSessionHandle? Session { get; private set; }

    public bool InTransaction => Session is { IsInTransaction: true };

    public IMongoCollection<T> Collection<T>(string name) => Database.GetCollection<T>(name);

    internal void Attach(IClientSessionHandle session) =>
        Session = Session is null ? session : throw new InvalidOperationException("Mongo tranzaksiyası artıq açıqdır.");

    internal void Detach() => Session = null;

    /// <summary>
    /// <paramref name="work"/>-ü tranzaksiyada icra edir: açıq tranzaksiya varsa ona qoşulur (commit-i sahibi
    /// edir), yoxdursa öz tranzaksiyasını açıb commit edir.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (InTransaction)
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }

        using var session = await Client.StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        session.StartTransaction();
        Attach(session);
        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);
            await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (session.IsInTransaction)
            {
                await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            Detach();
        }
    }
}
