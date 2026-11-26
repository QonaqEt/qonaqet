using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Users;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Businesses;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>Lövhə API-si və məxfilik (E11-T04).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class LeaderboardApiTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task People_board_shows_only_opted_in_active_users_by_nickname_or_first_name_and_initial()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var aysel = await PersonAsync(host, "lb-aysel@example.az", "Aysel", "Məmmədova", points: 10);
        var orxan = await PersonAsync(host, "lb-orxan@example.az", "Orxan", null, points: 8);
        var blocked = await PersonAsync(host, "lb-blocked@example.az", "Kamran", "Əliyev", points: 12);
        var nick = await PersonAsync(host, "lb-nick@example.az", "Leyla", "Həsənova", points: 5);
        using (var optOut = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/leaderboard", Access(orxan), new { optIn = false }))
        {
            Assert.Equal(HttpStatusCode.OK, optOut.StatusCode);
        }

        using (var nickname = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/leaderboard", Access(nick), new { nickname = " Səxavətli " }))
        {
            var json = await host.JsonAsync(nickname);
            Assert.Equal(("Səxavətli", "Səxavətli", true), (json.GetProperty("nickname").GetString(), json.GetProperty("displayName").GetString(), json.GetProperty("optIn").GetBoolean()));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = (await users.GetAsync(Domain.Users.UserId.From(UserId(blocked)), Ct))!;
            user.Block("test", host.Clock.UtcNow, null);
            await users.UpdateAsync(user, Ct);
        }

        using var response = await host.GetAsync("/api/v1/leaderboard?subjectType=User&periodType=AllTime", Access(orxan));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var board = JsonDocument.Parse(body).RootElement;

        Assert.Equal("all", board.GetProperty("periodKey").GetString());
        Assert.Equal(
            [(1, "Aysel M.", 10), (2, "Səxavətli", 5)],
            board.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("rank").GetInt32(), i.GetProperty("displayName").GetString(), i.GetProperty("points").GetInt32())));
        var me = board.GetProperty("me");
        Assert.Equal((2, 8), (me.GetProperty("rank").GetInt32(), me.GetProperty("points").GetInt32())); // gizli olsa da öz yerini görür

        foreach (var login in new[] { aysel, orxan, blocked, nick })
        {
            Assert.DoesNotContain(UserId(login).ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("@example.az", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Məmmədova", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Business_board_hides_suspended_businesses_and_shows_the_owner_their_place()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var open = await SocialTestData.ShopAsync(host, "lb-open");
        var (_, suspendedId) = await BusinessTestData.ActiveBusinessAsync(
            host, open.Admin, "lb-closed@example.az", BusinessTestData.ApplicationBody("Bağlı Kafe", "3100000002"));
        await ScoreAsync(host, ScoreSubjectType.Business, Guid.Parse(open.BusinessId), 4);
        await ScoreAsync(host, ScoreSubjectType.Business, Guid.Parse(suspendedId), 6);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var businesses = scope.ServiceProvider.GetRequiredService<IBusinessRepository>();
            var business = (await businesses.GetAsync(BusinessId.From(Guid.Parse(suspendedId)), Ct))!;
            business.Suspend("test", host.Clock.UtcNow, null);
            await businesses.UpdateAsync(business, Ct);
        }

        var month = LeaderboardPeriod.Current(LeaderboardPeriodType.Month, host.Clock.UtcNow).Key;
        using var response = await host.GetAsync($"/api/v1/leaderboard?subjectType=Business&periodType=Month&periodKey={month}", Access(open.Owner));
        var board = await host.JsonAsync(response);

        var item = Assert.Single(board.GetProperty("items").EnumerateArray());
        Assert.Equal(1, item.GetProperty("rank").GetInt32());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("slug").GetString()));
        Assert.Equal((1, 4), (board.GetProperty("me").GetProperty("rank").GetInt32(), board.GetProperty("me").GetProperty("points").GetInt32()));
    }

    [Fact]
    public async Task Invalid_period_key_and_nickname_are_rejected()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var user = await host.LoginAsync("lb-invalid@example.az");

        using (var period = await host.GetAsync("/api/v1/leaderboard?subjectType=User&periodType=Week&periodKey=2026-11", Access(user)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, period.StatusCode);
        }

        using (var nickname = await host.SendAsync(HttpMethod.Patch, "/api/v1/me/leaderboard", Access(user), new { nickname = "x" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, nickname.StatusCode);
        }

        using var anonymous = await host.GetAsync("/api/v1/leaderboard?subjectType=User&periodType=AllTime", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<JsonElement> PersonAsync(ApiTestHost host, string email, string firstName, string? lastName, int points)
    {
        var login = await host.LoginAsync(email);
        using (var profile = await host.SendAsync(HttpMethod.Patch, "/api/v1/me", Access(login), new { firstName, lastName }))
        {
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        }

        await ScoreAsync(host, ScoreSubjectType.User, UserId(login), points);
        return login;
    }

    private static async Task ScoreAsync(ApiTestHost host, ScoreSubjectType subjectType, Guid subjectId, int points)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var scoreEvent = ScoreEvent.Award(subjectType, subjectId, ScoringPolicy.ClaimSource, Guid.CreateVersion7(), points, 1, 0, host.Clock.UtcNow);
        await LeaderboardAggregates.AddAsync(context, scoreEvent, Ct);
        await context.SaveChangesAsync(Ct);
    }
}
