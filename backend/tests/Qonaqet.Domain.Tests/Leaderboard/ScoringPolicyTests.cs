using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Social;

namespace Qonaqet.Domain.Tests.Leaderboard;

public sealed class ScoringPolicyTests
{
    private static readonly ScoringPolicy Policy = new(new ScorePoints());

    [Fact]
    public void Item_gift_gives_the_sender_one_point_per_redeemed_unit()
    {
        var sender = Guid.CreateVersion7();
        var recipient = Guid.CreateVersion7();
        var redemption = Guid.CreateVersion7();

        var award = Assert.Single(Policy.ForGift(new RedeemedGift(Guid.CreateVersion7(), redemption, GiftKind.Items, 12_60, 3, sender, null, recipient)));

        Assert.Equal(new ScoreAward(ScoreSubjectType.User, sender, ScoringPolicy.GiftRedemptionSource, redemption, 3, 12_60, recipient), award);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5_00, 1)]
    [InlineData(5_01, 2)]
    [InlineData(12_00, 3)]
    public void Voucher_gift_gives_one_point_per_started_five_azn(long valueQepik, int expected)
    {
        var award = Assert.Single(Policy.ForGift(new RedeemedGift(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GiftKind.Voucher, valueQepik, 0, Guid.CreateVersion7(), null, Guid.CreateVersion7())));

        Assert.Equal(expected, award.Points);
    }

    [Fact]
    public void Corporate_gift_scores_the_company_once_per_gift_not_per_redemption()
    {
        var gift = Guid.CreateVersion7();
        var corporate = Guid.CreateVersion7();

        var award = Assert.Single(Policy.ForGift(new RedeemedGift(gift, Guid.CreateVersion7(), GiftKind.Voucher, 20_00, 0, null, corporate, Guid.CreateVersion7())));

        Assert.Equal(new ScoreAward(ScoreSubjectType.Corporate, corporate, ScoringPolicy.GiftSource, gift, 1, 20_00), award);
    }

    [Theory]
    [InlineData(OfferType.BusinessDaily)]
    [InlineData(OfferType.Suspended)]
    public void In_person_claims_score_the_business(OfferType type)
    {
        var business = Guid.CreateVersion7();

        var award = Assert.Single(Policy.ForClaim(new RedeemedClaim(Guid.CreateVersion7(), business, type, null, null, null)));

        Assert.Equal((ScoreSubjectType.Business, business, 2, 0L), (award.SubjectType, award.SubjectId, award.Points, award.Value));
    }

    [Fact]
    public void Remote_claim_scores_the_donor_not_the_business()
    {
        var donor = Guid.CreateVersion7();

        var award = Assert.Single(Policy.ForClaim(new RedeemedClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), OfferType.Remote, 4_00, donor, null)));

        Assert.Equal((ScoreSubjectType.User, donor, 2, 4_00L), (award.SubjectType, award.SubjectId, award.Points, award.Value));
    }

    [Fact]
    public void Campaign_pool_claim_scores_the_funding_donor_and_the_company()
    {
        var donor = Guid.CreateVersion7();
        var corporate = Guid.CreateVersion7();

        var awards = Policy.ForClaim(new RedeemedClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), null, 3_00, donor, corporate));

        Assert.Equal([(ScoreSubjectType.User, donor, 2), (ScoreSubjectType.Corporate, corporate, 2)], awards.Select(a => (a.SubjectType, a.SubjectId, a.Points)));
    }

    [Fact]
    public void Pool_claim_without_a_donor_scores_nobody()
    {
        Assert.Empty(Policy.ForClaim(new RedeemedClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), null, 3_00, null, null)));
    }

    [Fact]
    public void Point_values_come_from_configuration()
    {
        var policy = new ScoringPolicy(new ScorePoints { GiftItemUnit = 2, BusinessClaim = 5 });

        Assert.Equal(6, Assert.Single(policy.ForGift(new RedeemedGift(Guid.CreateVersion7(), Guid.CreateVersion7(), GiftKind.Items, 9_00, 3, Guid.CreateVersion7(), null, Guid.CreateVersion7()))).Points);
        Assert.Equal(5, Assert.Single(policy.ForClaim(new RedeemedClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), OfferType.Suspended, null, null, null))).Points);
    }

    [Fact]
    public void Self_gift_scores_nobody()
    {
        var user = Guid.CreateVersion7();

        Assert.Empty(Policy.ForGift(new RedeemedGift(Guid.CreateVersion7(), Guid.CreateVersion7(), GiftKind.Voucher, 10_00, 0, user, null, user)));
    }

    [Fact]
    public void Link_only_gift_scores_the_sender_only_after_the_recipient_added_it_to_an_account()
    {
        var gift = Guid.CreateVersion7();
        var sender = Guid.CreateVersion7();

        Assert.Empty(Policy.ForGift(new RedeemedGift(gift, Guid.CreateVersion7(), GiftKind.Items, 4_00, 1, sender, null, null)));
        Assert.Single(Policy.ForGift(new RedeemedGift(gift, Guid.CreateVersion7(), GiftKind.Items, 4_00, 1, sender, null, Guid.CreateVersion7())));
    }

    [Fact]
    public void Corporate_gift_scores_the_company_only_after_the_recipient_has_an_account()
    {
        var gift = Guid.CreateVersion7();
        var corporate = Guid.CreateVersion7();

        Assert.Empty(Policy.ForGift(new RedeemedGift(gift, Guid.CreateVersion7(), GiftKind.Voucher, 20_00, 0, null, corporate, null)));
        var award = Assert.Single(Policy.ForGift(new RedeemedGift(gift, Guid.CreateVersion7(), GiftKind.Voucher, 20_00, 0, null, corporate, Guid.CreateVersion7())));
        Assert.Equal(ScoreSubjectType.Corporate, award.SubjectType);
    }

    [Theory]
    [InlineData(3, null, 0, 3)]
    [InlineData(5, 7, 0, 3)] // cütün ayda 10 xalından 3-ü qalıb
    [InlineData(5, 10, 0, 0)]
    [InlineData(5, null, 298, 2)] // gündə 300-dən 2-si qalıb
    [InlineData(5, null, 300, 0)]
    [InlineData(5, 9, 299, 1)]
    [InlineData(2, null, 320, 0)] // mənfi qalıq heç vaxt mənfi xal vermir
    public void Limits_cap_points_per_pair_per_month_and_per_user_per_day(int earned, int? pairThisMonth, int userToday, int expected)
    {
        Assert.Equal(expected, new ScoreLimits().Allow(earned, pairThisMonth, userToday));
    }

    [Fact]
    public void Reversal_writes_the_negative_of_the_original_once()
    {
        var at = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        var original = ScoreEvent.Award(
            ScoreSubjectType.User, Guid.CreateVersion7(), ScoringPolicy.GiftRedemptionSource, Guid.CreateVersion7(), 3, 1, 9_00, at, Guid.CreateVersion7());

        var reversal = ScoreEvent.Reverse(original);

        Assert.Equal(
            (original.SubjectId, "GiftRedemptionReversal", original.SourceId, -3, -1, -9_00L, original.RecipientUserId, at),
            (reversal.SubjectId, reversal.SourceType, reversal.SourceId, reversal.Points, reversal.Treats, reversal.Value, reversal.RecipientUserId, reversal.CreatedAt));
        Assert.True(reversal.IsReversal);
        Assert.Throws<InvalidOperationException>(() => ScoreEvent.Reverse(reversal));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScoreEvent.Award(ScoreSubjectType.User, Guid.CreateVersion7(), "Gift", Guid.CreateVersion7(), -1, 1, 0, at));
    }
}
