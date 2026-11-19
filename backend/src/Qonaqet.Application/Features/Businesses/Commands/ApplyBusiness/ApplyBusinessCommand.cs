using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Businesses.Commands.ApplyBusiness;

/// <summary>
/// <c>POST businesses/apply</c>: istənilən aktiv istifadəçi biznes üçün müraciət edir. Biznes <c>Pending</c> yaranır,
/// admin aktivləşdirəndə müraciətçi sahib olur. VÖEN silinməmiş bizneslər arasında unikaldır, slug addan yaranır.
/// Bir istifadəçinin eyni anda yalnız bir baxılmamış müraciəti ola bilər (şəbəkə sahibi növbəti biznesini sonra əlavə edir).
/// </summary>
[RequiresActiveAccount]
public sealed record ApplyBusinessCommand(
    string Name,
    BusinessType Type,
    string Voen,
    string Address,
    double Latitude,
    double Longitude,
    string Phone,
    string Email,
    string Iban,
    IReadOnlyList<DailyHoursDto> WorkingHours,
    Guid? LogoFileId = null,
    Guid? CoverFileId = null) : ICommand<BusinessDto>, IBusinessProfileInput;

public sealed class ApplyBusinessValidator : BusinessProfileValidator<ApplyBusinessCommand>;

public sealed class ApplyBusinessHandler(IBusinessRepository businesses, BusinessImageCheck images, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ApplyBusinessCommand, BusinessDto>
{
    /// <summary>Slug toqquşanda <c>-2</c>, <c>-3</c> … sınanır.</summary>
    private const int MaxSlugAttempts = 50;

    public async Task<Result<BusinessDto>> Handle(ApplyBusinessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var applicant = currentUser.ActorId!.Value;

        if (await businesses.HasPendingApplicationAsync(applicant, cancellationToken).ConfigureAwait(false))
        {
            return BusinessErrors.ApplicationPending;
        }

        if (await businesses.VoenExistsAsync(request.Voen, cancellationToken).ConfigureAwait(false))
        {
            return BusinessErrors.VoenTaken;
        }

        if (!await images.IsUsableAsync(request.LogoFileId, FilePurpose.Logo, null, cancellationToken).ConfigureAwait(false)
            || !await images.IsUsableAsync(request.CoverFileId, FilePurpose.Banner, null, cancellationToken).ConfigureAwait(false))
        {
            return BusinessErrors.FileInvalid;
        }

        var slug = await FreeSlugAsync(BusinessRules.Slugify(request.Name), cancellationToken).ConfigureAwait(false);
        if (slug is null)
        {
            return BusinessErrors.SlugUnavailable;
        }

        var business = Business.Submit(request.ToProfile(), slug, applicant, clock.UtcNow);
        await businesses.AddAsync(business, cancellationToken).ConfigureAwait(false);
        return business.ToDto();
    }

    private async Task<string?> FreeSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxSlugAttempts; attempt++)
        {
            var candidate = attempt == 1 ? baseSlug : $"{baseSlug}-{attempt}";
            if (!await businesses.SlugExistsAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return null;
    }
}
