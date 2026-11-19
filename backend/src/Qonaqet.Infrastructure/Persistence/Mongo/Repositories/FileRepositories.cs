using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class StoredFileRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<StoredFile, FileId>(context, tracker, outbox, clock, FileCollectionNames.Files), IStoredFileRepository;

public static class FileCollectionNames
{
    public const string Files = "files";
}
