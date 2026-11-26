using Qonaqet.Domain.Integration;

namespace Qonaqet.Domain.Tests.Integration;

public sealed class IntegrationClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

    private static IntegrationClient Client() =>
        IntegrationClient.Create(
            "Kassa POS", "qc_live_abc", "hash-1", IntegrationOwnerType.Business, Guid.CreateVersion7(), [Guid.CreateVersion7()],
            [PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite], isSandbox: false, 120, Now, null);

    [Fact]
    public void Empty_scope_request_grants_every_scope_of_the_client()
    {
        Assert.Equal([PartnerScopes.CodesRead, PartnerScopes.RedemptionsWrite], Client().GrantScopes([]));
    }

    [Fact]
    public void Requested_scopes_must_be_a_subset_of_the_client_scopes()
    {
        var client = Client();

        Assert.Equal([PartnerScopes.CodesRead], client.GrantScopes([PartnerScopes.CodesRead]));
        Assert.Null(client.GrantScopes([PartnerScopes.CodesRead, PartnerScopes.GiftsIssue]));
    }

    [Fact]
    public void Previous_secret_is_accepted_for_24_hours_after_rotation()
    {
        var client = Client();
        Assert.False(client.AcceptsPreviousSecretAt(Now));

        client.RotateSecret("hash-2", Now, null);

        Assert.Equal(("hash-2", "hash-1"), (client.SecretHash, client.PreviousSecretHash));
        Assert.True(client.AcceptsPreviousSecretAt(Now.AddHours(23)));
        Assert.False(client.AcceptsPreviousSecretAt(Now.AddHours(24)));
    }

    [Theory]
    [InlineData("", 120, "codes:read")]
    [InlineData("POS", 5, "codes:read")]
    [InlineData("POS", 120, "admin:all")]
    public void Invalid_clients_are_rejected(string name, int rateLimit, string scope) =>
        Assert.False(IntegrationClient.IsValid(name, [], [scope], rateLimit));

    [Fact]
    public void Business_access_is_limited_to_the_listed_businesses()
    {
        var business = Guid.CreateVersion7();
        var client = IntegrationClient.Create(
            "Şəbəkə", "qc_live_x", "h", IntegrationOwnerType.Business, Guid.CreateVersion7(), [business], [PartnerScopes.CodesRead], false, 60, Now, null);

        Assert.True(client.CanAccessBusiness(business));
        Assert.False(client.CanAccessBusiness(Guid.CreateVersion7()));
    }
}
