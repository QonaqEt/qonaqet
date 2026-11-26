using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Queries.GetPartnerClient;

/// <summary><c>GET /partner/v1/me</c>: token-in client-i — ad, scope-lar, bizneslər, sandbox, rate limit (secret yox).</summary>
[RequiresRole(PartnerScopes.ClientRole)]
public sealed record GetPartnerClientQuery : IQuery<IntegrationClientDto>;

public sealed class GetPartnerClientHandler(PartnerAccess access) : IQueryHandler<GetPartnerClientQuery, IntegrationClientDto>
{
    public async Task<Result<IntegrationClientDto>> Handle(GetPartnerClientQuery request, CancellationToken cancellationToken)
    {
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        return client.IsFailure ? client.Error : client.Value.ToDto();
    }
}
