using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

public static class FileErrors
{
    public static readonly Error Empty = Error.Validation("file_empty", "Fayl boşdur.");

    public static readonly Error TooLarge = Error.Validation("file_too_large", "Fayl icazə verilən ölçüdən böyükdür (ən çox 5 MB).");

    public static readonly Error TypeNotAllowed =
        Error.Validation("file_type_not_allowed", "Bu fayl növü qəbul olunmur.");

    public static readonly Error NotFound = Error.NotFound("file_not_found", "Fayl tapılmadı.");
}
