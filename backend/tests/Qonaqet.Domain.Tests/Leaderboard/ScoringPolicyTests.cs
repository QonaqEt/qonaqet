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
        var redemption = Guid.CreateVersion7();

        var award = Assert.Single(Policy.ForGift(new RedeemedGift(Guid.CreateVersion7(), redemption, GiftKind.Items, 12_60, 3, sender, null)));

        Assert.Equal(new ScoreAward(ScoreSubjectType.User, sender, ScoringPolicy.GiftRedemptionSource, redemption, 3, 12_60), award);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5_00, 1)]
    [InlineData(5_01, 2)]
    [InlineData(12_00, 3)]
    public void Voucher_gift_gives_one_point_per_started_five_azn(long valueQepik, int expected)
    {
        var award = Assert.Single(Policy.ForGift(new RedeemedGift(
            Guid.CreateVersion7(), Guid.CreateVersion7(), GiftKind.Voucher, valueQepik, 0, Guid.CreateVersion7(), null)));

        Assert.Equal(expected, award.Points);
    }

    [Fact]
    public void Corporate_gift_scores_the_company_once_per_gift_not_per_redemption()
    {
        var gift = Guid.CreateVersion7();
        var corporate = Guid.CreateVersion7();

        var award = Assert.Single(Policy.ForGift(new RedeemedGift(gift, Guid.CreateVersion7(), GiftKind.Voucher, 20_00, 0, null, corporate)));

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

        Assert.Equal(6, Assert.Single(policy.ForGift(new RedeemedGift(Guid.CreateVersion7(), Guid.CreateVersion7(), GiftKind.Items, 9_00, 3, Guid.CreateVersion7(), null))).Points);
        Assert.Equal(5, Assert.Single(policy.ForClaim(new RedeemedClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), OfferType.Suspended, null, null, null))).Points);
    }
}
