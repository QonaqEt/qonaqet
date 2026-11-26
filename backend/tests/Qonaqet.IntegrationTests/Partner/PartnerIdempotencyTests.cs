using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>Idempotency-Key və audit (E13-T03): başlıq məcburidir, eyni açar + body → saxlanmış cavab, fərqli body → 409, hər çağırış audit-də.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerIdempotencyTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Post_requires_the_key_replays_the_stored_response_and_rejects_a_different_body()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "pidem", [PartnerScopes.CodesRead]);
        var token = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        using (var missing = await partner.SendAsync(HttpMethod.Post, "/partner/v1/__echo", token, new { value = "a" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal("idempotency_key_required", (await api.JsonAsync(missing)).GetProperty("code").GetString());
        }

        string firstId;
        using (var first = await partner.SendAsync(HttpMethod.Post, "/partner/v1/__echo", token, new { value = "a" }, "key-1"))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            firstId = (await api.JsonAsync(first)).GetProperty("id").GetString()!;
        }

        var executions = PartnerEchoController.Executions;
        using (var replay = await partner.SendAsync(HttpMethod.Post, "/partner/v1/__echo", token, new { value = "a" }, "key-1"))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(firstId, (await api.JsonAsync(replay)).GetProperty("id").GetString());
            Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        }

        Assert.Equal(executions, PartnerEchoController.Executions); // əməliyyat təkrar işləmədi

        using (var reused = await partner.SendAsync(HttpMethod.Post, "/partner/v1/__echo", token, new { value = "b" }, "key-1"))
        {
            Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
            Assert.Equal("idempotency_key_reused", (await api.JsonAsync(reused)).GetProperty("code").GetString());
        }

        // Token endpoint-i (OAuth) başlıqsız işləyir.
        await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        Assert.Equal(1, await context.IdempotencyRecords.AsNoTracking().CountAsync(r => r.Key == "key-1", Ct));
    }

    [Fact]
    public async Task Every_authenticated_partner_call_is_audited_with_the_client_id_and_route_template()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "paudit", [PartnerScopes.CodesRead]);
        var token = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);
        var id = Guid.Parse(client.GetProperty("id").GetString()!);

        using (var me = await partner.SendAsync(HttpMethod.Get, "/partner/v1/me", token))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using (var echo = await partner.SendAsync(HttpMethod.Post, "/partner/v1/__echo", token, new { value = "x" }, "audit-1"))
        {
            Assert.Equal(HttpStatusCode.Created, echo.StatusCode);
        }

        await using var scope = api.Services.CreateAsyncScope();
        var audits = await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Set<AuditLogRecord>().AsNoTracking()
            .Where(a => a.ActorId == id && a.Action == "PartnerCall").ToListAsync(Ct);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("IntegrationClient", a.ActorType));
        Assert.Contains(audits, a => a.Data.Contains("partner/v1/me", StringComparison.Ordinal) && a.Data.Contains("200", StringComparison.Ordinal));
        Assert.DoesNotContain(audits, a => a.Data.Contains(token, StringComparison.Ordinal));
    }
}
