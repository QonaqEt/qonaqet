using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Files.Common;
using Qonaqet.Application.Tests.Fakes;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Tests.Files;

public sealed class FileUploaderTests
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, 1, 2, 3];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7];

    private readonly InMemoryFileStorage _storage = new();
    private readonly InMemoryFileRepository _files = new();
    private readonly FileUploader _uploader;

    public FileUploaderTests() =>
        _uploader = new FileUploader(_storage, _files, new FixedTestClock(DateTimeOffset.Parse("2026-11-19T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task Valid_document_is_stored_with_metadata_and_hash()
    {
        var owner = Guid.NewGuid();
        var result = await _uploader.UploadAsync(new FileUpload(FilePurpose.VerificationDocument, "application/pdf", Pdf), FileOwnerType.User, owner, owner, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var file = Assert.Single(_files.Added);
        Assert.Equal(_storage.Bucket, file.Bucket);
        Assert.Equal(Assert.Single(_storage.Keys), file.ObjectKey);
        Assert.StartsWith("verificationdocument/2026/11/", file.ObjectKey, StringComparison.Ordinal);
        Assert.Equal(FilePolicy.Pdf, file.ContentType);
        Assert.Equal(Pdf.Length, file.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Pdf)), file.Sha256);
        Assert.Equal((FileOwnerType.User, owner, owner), (file.OwnerType, file.OwnerId, file.CreatedBy));
    }

    [Fact]
    public async Task Oversized_file_is_rejected()
    {
        var big = new byte[FilePolicy.FiveMegabytes + 1];
        Pdf.CopyTo(big, 0);

        var result = await Upload(FilePurpose.VerificationDocument, "application/pdf", big);

        Assert.Equal(FileErrors.TooLarge, result.Error);
        Assert.Empty(_storage.Keys);
    }

    [Theory]
    [InlineData("image/png", FilePurpose.VerificationDocument, true)]
    [InlineData("application/octet-stream", FilePurpose.Logo, true)]
    [InlineData("application/pdf", FilePurpose.Logo, false)]
    [InlineData("image/png", FilePurpose.Logo, true)]
    [InlineData("image/jpeg", FilePurpose.Logo, false)]
    public async Task Declared_type_must_match_content_and_purpose(string declared, FilePurpose purpose, bool ok)
    {
        var result = await Upload(purpose, declared, Png);

        Assert.Equal(ok, result.IsSuccess);
        if (!ok)
        {
            Assert.Equal(FileErrors.TypeNotAllowed, result.Error);
        }
    }

    [Fact]
    public async Task Pdf_disguised_as_image_and_empty_files_are_rejected()
    {
        Assert.Equal(FileErrors.TypeNotAllowed, (await Upload(FilePurpose.CatalogImage, "image/png", Pdf)).Error);
        Assert.Equal(FileErrors.Empty, (await Upload(FilePurpose.CatalogImage, "image/png", [])).Error);
    }

    private Task<Result<StoredFile>> Upload(FilePurpose purpose, string type, byte[] content) =>
        _uploader.UploadAsync(new FileUpload(purpose, type, content), FileOwnerType.User, Guid.NewGuid(), null, CancellationToken.None);

    private sealed class InMemoryFileRepository : IStoredFileRepository
    {
        public List<StoredFile> Added { get; } = [];

        public Task<StoredFile?> GetAsync(FileId id, CancellationToken cancellationToken) => Task.FromResult(Added.Find(f => f.Id == id));

        public Task AddAsync(StoredFile aggregate, CancellationToken cancellationToken)
        {
            Added.Add(aggregate);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(StoredFile aggregate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> SoftDeleteAsync(FileId id, Guid? userId, string? reason, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RestoreAsync(FileId id, Guid? userId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FixedTestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
