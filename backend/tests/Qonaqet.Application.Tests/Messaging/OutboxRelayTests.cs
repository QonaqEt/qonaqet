using Microsoft.Extensions.Logging.Abstractions;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Tests.Messaging;

/// <summary>Outbox relay-in göndəriş/yenidən cəhd məntiqi (fake store və bus ilə).</summary>
public sealed class OutboxRelayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Published_messages_are_marked_processed_and_failures_are_rescheduled_with_backoff()
    {
        var ok = new PendingOutboxMessage(Guid.CreateVersion7(), "A", "{}", Now, 0);
        var bad = new PendingOutboxMessage(Guid.CreateVersion7(), "B", "{}", Now, 2);
        var store = new FakeStore([ok, bad]);
        var bus = new FakeBus(failOn: "B");
        var relay = new OutboxRelay([store], bus, new Clock(), new OutboxRelayOptions(), NullLogger<OutboxRelay>.Instance);

        var published = await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, published);
        Assert.Equal([ok.Id], bus.Published);
        Assert.Equal([ok.Id], store.Processed);
        var failure = Assert.Single(store.Failed);
        Assert.Equal(bad.Id, failure.Id);
        Assert.Equal(Now + TimeSpan.FromSeconds(8), failure.Next); // 3-cü cəhd → 2^3 s
        Assert.Contains("broker", failure.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(5, 32)]
    [InlineData(20, 600)]
    public void Backoff_grows_exponentially_up_to_the_maximum(int attempts, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), OutboxRelay.Backoff(attempts, TimeSpan.FromMinutes(10)));

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeBus(string failOn) : IEventBus
    {
        public List<Guid> Published { get; } = [];

        public Task PublishAsync(Guid messageId, string type, string payload, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            if (type == failOn)
            {
                throw new InvalidOperationException("broker əlçatan deyil");
            }

            Published.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore(IReadOnlyList<PendingOutboxMessage> pending) : IOutboxStore
    {
        public string Name => "fake";

        public List<Guid> Processed { get; } = [];

        public List<(Guid Id, string Error, DateTimeOffset Next)> Failed { get; } = [];

        public Task<IReadOnlyList<PendingOutboxMessage>> ClaimAsync(int batchSize, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken) =>
            Task.FromResult(pending);

        public Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken cancellationToken)
        {
            Processed.Add(id);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken)
        {
            Failed.Add((id, error, nextAttemptAt));
            return Task.CompletedTask;
        }
    }
}
