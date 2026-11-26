using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Businesses.Common;

/// <summary>Bir günün iş saatları (sorğu/cavab).</summary>
public sealed record DailyHoursDto(DayOfWeek Day, string Opens, string Closes);

/// <summary>
/// Biznesin daxili görünüşü — yalnız sahib, işçi və admin üçün; VÖEN və IBAN yalnız sahib və adminə. İctimai siyahılar başqa DTO işlədir.
/// </summary>
public sealed record BusinessDto(
    BusinessId Id,
    string Name,
    string Slug,
    BusinessType Type,
    string? Voen,
    string Address,
    double Latitude,
    double Longitude,
    string Phone,
    string Email,
    string? Iban,
    IReadOnlyList<DailyHoursDto> WorkingHours,
    Guid? LogoFileId,
    Guid? CoverFileId,
    bool AcceptsPool,
    bool AcceptsNetworkVouchers,
    long VoucherMinQepik,
    long VoucherMaxQepik,
    BusinessStatus Status,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? SuspendedAt,
    bool IsDeleted,
    bool IsSandbox);

public static class BusinessMapping
{
    /// <summary><paramref name="includeFinancials"/> = false olanda VÖEN və IBAN boş qaytarılır (işçilər üçün).</summary>
    public static BusinessDto ToDto(this Business business, bool includeFinancials = true)
    {
        ArgumentNullException.ThrowIfNull(business);
        return new BusinessDto(
            business.Id, business.Name, business.Slug, business.Type, includeFinancials ? business.Voen : null, business.Address,
            business.Location.Latitude, business.Location.Longitude, business.Phone, business.Email, includeFinancials ? business.Iban : null,
            [.. business.WorkingHours.Select(h => new DailyHoursDto(h.Day, h.Opens, h.Closes))],
            business.LogoFileId, business.CoverFileId, business.AcceptsPool, business.AcceptsNetworkVouchers,
            business.VoucherMinQepik, business.VoucherMaxQepik, business.Status, business.OwnerUserId,
            business.CreatedAt, business.ActivatedAt, business.SuspendedAt, business.IsDeleted, business.IsSandbox);
    }

    public static IReadOnlyList<DailyHours> ToDomain(this IReadOnlyList<DailyHoursDto> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        return [.. hours.Select(h => DailyHours.Create(h.Day, h.Opens, h.Closes))];
    }
}

/// <summary>Müraciət və profil yeniləməsinin ortaq sahələri və onların yoxlaması.</summary>
public interface IBusinessProfileInput
{
    string Name { get; }

    BusinessType Type { get; }

    string Voen { get; }

    string Address { get; }

    double Latitude { get; }

    double Longitude { get; }

    string Phone { get; }

    string Email { get; }

    string Iban { get; }

    IReadOnlyList<DailyHoursDto> WorkingHours { get; }

    Guid? LogoFileId { get; }

    Guid? CoverFileId { get; }
}

public static class BusinessProfileInput
{
    public static BusinessProfile ToProfile(this IBusinessProfileInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new BusinessProfile(
            input.Name, input.Type, input.Voen, input.Address, GeoPoint.At(input.Latitude, input.Longitude),
            input.Phone, input.Email, input.Iban, input.WorkingHours.ToDomain(), input.LogoFileId, input.CoverFileId);
    }
}

/// <summary>Profil sahələrinin qaydaları (müraciət və yeniləmə validatorları bundan törəyir).</summary>
public abstract class BusinessProfileValidator<T> : Validator<T>
    where T : IBusinessProfileInput
{
    protected BusinessProfileValidator()
    {
        // Generik T üzərində RuleFor selector-u interfeys çevrilməsi daşıyır, ona görə sahə adları açıq verilir.
        Rule("name", x => !string.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= Business.NameMaxLength, "name_invalid", $"Ad boş ola bilməz, ən çox {Business.NameMaxLength} simvol.");
        Rule("type", x => Enum.IsDefined(x.Type), "business_type_invalid", "Naməlum biznes növü.");
        Rule("voen", x => BusinessRules.IsValidVoen(x.Voen), "voen_invalid", "VÖEN 10 rəqəmdən ibarət olmalıdır.");
        Rule("address", x => !string.IsNullOrWhiteSpace(x.Address) && x.Address.Length <= Business.AddressMaxLength, "address_invalid", $"Ünvan boş ola bilməz, ən çox {Business.AddressMaxLength} simvol.");
        Rule("location", x => GeoPoint.IsValid(x.Latitude, x.Longitude), "location_invalid", "Xəritə nöqtəsi düzgün deyil.");
        Rule("phone", x => BusinessRules.NormalizePhone(x.Phone) is not null, "phone_invalid", "Telefon +994XXXXXXXXX formatında olmalıdır.");
        Rule("email", x => BusinessRules.IsEmail(x.Email), ValidationCodes.InvalidEmail, "E-poçt ünvanı düzgün deyil.");
        Rule("iban", x => BusinessRules.NormalizeIban(x.Iban) is not null, "iban_invalid", "IBAN düzgün deyil (AZ ilə başlayan 28 simvol).");
        Rule(
            "workingHours",
            x => x.WorkingHours is { Count: <= 7 } h && h.All(d => d is not null && DailyHours.IsValid(d.Day, d.Opens, d.Closes)) && h.Select(d => d.Day).Distinct().Count() == h.Count,
            "working_hours_invalid",
            "İş saatları düzgün deyil (hər gün bir dəfə, HH:mm).");
        Rule("logoFileId", x => x.LogoFileId != Guid.Empty, "business_file_invalid", "Loqo faylı düzgün deyil.");
        Rule("coverFileId", x => x.CoverFileId != Guid.Empty, "business_file_invalid", "Örtük faylı düzgün deyil.");
    }
}

/// <summary>Loqo/örtük faylının yoxlaması: mövcuddur, təyinatı uyğundur və onu cari istifadəçi (və ya admin) yükləyib.</summary>
public sealed class BusinessImageCheck(IStoredFileRepository files, ICurrentUser currentUser)
{
    public async Task<bool> IsUsableAsync(Guid? fileId, FilePurpose purpose, Guid? currentValue, CancellationToken cancellationToken)
    {
        if (fileId is null || fileId == currentValue)
        {
            return true;
        }

        var file = await files.GetAsync(FileId.From(fileId.Value), cancellationToken).ConfigureAwait(false);
        return file is not null && file.Purpose == purpose && Files.Common.FileAccessRules.CanManage(currentUser, file);
    }
}

/// <summary>İctimai görünüş (axtarış, biznes səhifəsi): VÖEN, IBAN, email və sahib yoxdur.</summary>
public sealed record PublicBusinessDto(
    BusinessId Id,
    string Name,
    string Slug,
    BusinessType Type,
    string Address,
    double Latitude,
    double Longitude,
    string Phone,
    IReadOnlyList<DailyHoursDto> WorkingHours,
    Guid? LogoFileId,
    Guid? CoverFileId,
    bool AcceptsPool,
    bool AcceptsNetworkVouchers,
    long VoucherMinQepik,
    long VoucherMaxQepik,
    double? DistanceMeters);

public static class PublicBusinessMapping
{
    public static PublicBusinessDto ToPublicDto(this Business business, double? distanceMeters)
    {
        ArgumentNullException.ThrowIfNull(business);
        return new PublicBusinessDto(
            business.Id, business.Name, business.Slug, business.Type, business.Address, business.Location.Latitude, business.Location.Longitude,
            business.Phone, [.. business.WorkingHours.Select(h => new DailyHoursDto(h.Day, h.Opens, h.Closes))], business.LogoFileId, business.CoverFileId,
            business.AcceptsPool, business.AcceptsNetworkVouchers, business.VoucherMinQepik, business.VoucherMaxQepik,
            distanceMeters is { } d ? Math.Round(d, 1) : null);
    }
}
