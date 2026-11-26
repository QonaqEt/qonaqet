using Qonaqet.Domain.Integration;

namespace Qonaqet.Domain.Tests.Integration;

public sealed class WebhookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

    private static WebhookSubscription Subscription() =>
        WebhookSubscription.Create(
            Guid.CreateVersion7(), new Uri("https://pos.example.az/hooks"), [WebhookEventTypes.RedemptionCreated, WebhookEventTypes.GiftRedeemed],
            "sealed", Now);

    [Fact]
    public void Signature_matches_the_documented_test_vector()
    {
        Assert.Equal(
            "sha256=38877139021993b830af32feea6e18a8da83eb2f6e49ee50bd9e4cf4ca4d3789",
            WebhookDelivery.Signature("whsec_test", "1700000000", """{"a":1}"""));
    }

    [Fact]
    public void Subscription_rejects_unknown_event_types()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookSubscription.Create(Guid.CreateVersion7(), new Uri("https://x.az/h"), ["gift.unknown"], "sealed", Now));
    }

    [Fact]
    public void Subscription_wants_only_its_event_types()
    {
        var subscription = Subscription();

        Assert.True(subscription.Wants(WebhookEventTypes.RedemptionCreated));
        Assert.False(subscription.Wants(WebhookEventTypes.PayoutCreated));
    }

    [Theory]
    [InlineData("https://pos.example.az/hooks", false, true)]
    [InlineData("http://localhost:5000/hooks", false, false)]
    [InlineData("http://localhost:5000/hooks", true, true)]
    [InlineData("https://user:pass@pos.example.az/hooks", false, false)]
    [InlineData("https://pos.example.az/hooks#x", false, false)]
    public void Url_must_be_https_without_credentials_or_fragment(string url, bool allowHttp, bool valid)
    {
        Assert.Equal(valid, WebhookSubscription.IsValidUrl(new Uri(url), allowHttp));
    }

    [Fact]
    public void Delivery_id_is_stable_for_the_same_event_and_subscription()
    {
        var eventId = Guid.CreateVersion7();
        var subscriptionId = Guid.CreateVersion7();

        Assert.Equal(
            WebhookDelivery.DeliveryIdFor(eventId, subscriptionId, WebhookEventTypes.GiftRedeemed),
            WebhookDelivery.DeliveryIdFor(eventId, subscriptionId, WebhookEventTypes.GiftRedeemed));
        Assert.NotEqual(
            WebhookDelivery.DeliveryIdFor(eventId, subscriptionId, WebhookEventTypes.GiftRedeemed),
            WebhookDelivery.DeliveryIdFor(eventId, subscriptionId, WebhookEventTypes.RedemptionCreated));
    }

    [Fact]
    public void Failed_attempts_follow_the_backoff_schedule_then_fail()
    {
        var delivery = WebhookDelivery.Pending(Subscription(), Guid.CreateVersion7(), WebhookEventTypes.RedemptionCreated, "{}", Now);
        var at = Now;
        TimeSpan[] expected =
        [
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12), TimeSpan.FromHours(24),
        ];

        foreach (var delay in expected)
        {
            delivery = delivery.Claim(at).Complete(false, 500, "boom", at);
            Assert.Equal(WebhookDeliveryStatus.Retrying, delivery.Status);
            Assert.Equal(at + delay, delivery.NextAttemptAt);
            at = delivery.NextAttemptAt;
        }

        delivery = delivery.Claim(at).Complete(false, 500, "boom", at);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(7, delivery.Attempt);
    }

    [Fact]
    public void Every_state_change_is_a_new_row_with_the_next_sequence()
    {
        var pending = WebhookDelivery.Pending(Subscription(), Guid.CreateVersion7(), WebhookEventTypes.RedemptionCreated, "{}", Now);
        var sending = pending.Claim(Now);
        var done = sending.Complete(true, 200, new string('x', 2000), Now);

        Assert.Equal([1, 2, 3], new[] { pending.Seq, sending.Seq, done.Seq });
        Assert.All(new[] { sending, done }, row => Assert.Equal(pending.DeliveryId, row.DeliveryId));
        Assert.NotEqual(pending.Id, sending.Id);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, done.Status);
        Assert.Equal(WebhookDelivery.SnippetMaxLength, done.ResponseBodySnippet!.Length);
    }

    [Fact]
    public void Redeliver_makes_a_failed_delivery_due_immediately()
    {
        var failed = WebhookDelivery.Pending(Subscription(), Guid.CreateVersion7(), WebhookEventTypes.RedemptionCreated, "{}", Now)
            .Abandon("subscription_deleted", Now);
        var later = Now.AddDays(3);

        var again = failed.Redeliver(later);

        Assert.Equal(WebhookDeliveryStatus.Pending, again.Status);
        Assert.True(again.IsDue(later, TimeSpan.FromMinutes(5)));
        Assert.False(failed.IsDue(later, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Stuck_sending_row_becomes_due_after_the_timeout()
    {
        var sending = WebhookDelivery.Pending(Subscription(), Guid.CreateVersion7(), WebhookEventTypes.RedemptionCreated, "{}", Now).Claim(Now);

        Assert.False(sending.IsDue(Now.AddMinutes(1), TimeSpan.FromMinutes(5)));
        Assert.True(sending.IsDue(Now.AddMinutes(5), TimeSpan.FromMinutes(5)));
    }
}
