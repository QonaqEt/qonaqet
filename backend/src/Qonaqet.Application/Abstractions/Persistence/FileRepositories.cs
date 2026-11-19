using Qonaqet.Domain.Files;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>files</c> metadatası. Silinmiş fayl adi oxularda görünmür (yəni heç bir endpoint onu vermir).</summary>
public interface IStoredFileRepository : IRepository<StoredFile, FileId>;
