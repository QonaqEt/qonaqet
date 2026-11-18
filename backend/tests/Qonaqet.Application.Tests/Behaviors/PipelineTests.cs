using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Behaviors;
using Qonaqet.Infrastructure.Messaging;
using Qonaqet.Infrastructure.Persistence;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Tests.Behaviors;

public sealed class PipelineTests
{
    private static readonly Type[] RequestTypes =
    [
        typeof(CreateGift), typeof(CreateGiftHandler), typeof(CreateGiftValidator),
        typeof(Charge), typeof(ChargeHandler),
        typeof(AdminOnly), typeof(AdminOnlyHandler),
        typeof(PublicPing), typeof(PublicPingHandler),
        typeof(GetGift), typeof(GetGiftHandler),
        typeof(VerifyOtp), typeof(VerifyOtpHandler),
    ];

    [Fact]
    public void Behaviors_are_registered_in_the_documented_order()
    {
        var services = new ServiceCollection().AddApplicationPipeline();

        var registered = services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)).Select(d => d.ImplementationType).ToArray();

        Assert.Equal(
            [typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>), typeof(AuthorizationBehavior<,>), typeof(IdempotencyBehavior<,>), typeof(UnitOfWorkBehavior<,>)],
            registered);
    }

    [Fact]
    public async Task Validation_runs_before_authorization_and_nothing_reaches_the_transaction()
    {
        await using var app = TestApp.Create(authenticated: false);

        var result = await app.Send(new CreateGift("", 0));

        Assert.Equal("validation_failed", result.Error.Code);
        Assert.Empty(app.Trace);
    }

    [Fact]
    public async Task Unauthenticated_and_wrong_role_are_rejected_before_the_handler()
    {
        await using var anonymous = TestApp.Create(authenticated: false);
        await using var plainUser = TestApp.Create(roles: ["User"]);
        await using var admin = TestApp.Create(roles: ["Admin"]);

        Assert.Equal("unauthorized", (await anonymous.Send(new CreateGift("Leyla", 500))).Error.Code);
        Assert.Equal("forbidden", (await plainUser.Send(new AdminOnly())).Error.Code);
        Assert.True((await admin.Send(new AdminOnly())).IsSuccess);
        Assert.True((await anonymous.Send(new PublicPing())).IsSuccess);
        Assert.DoesNotContain("handler", anonymous.Trace.Take(1));
    }

    [Fact]
    public async Task Unit_of_work_writes_domain_events_to_the_outbox_inside_the_transaction()
    {
        await using var app = TestApp.Create();

        var result = await app.Send(new CreateGift("Leyla", 500));

        Assert.True(result.IsSuccess);
        Assert.Equal(["begin", "handler", "outbox:1", "commit"], app.Trace);
        var giftCreated = Assert.IsType<GiftCreated>(Assert.Single(app.Outbox.Events));
        Assert.Equal(result.Value, giftCreated.GiftId);
    }

    [Fact]
    public async Task Failed_command_rolls_back_and_writes_no_events()
    {
        await using var app = TestApp.Create();

        var result = await app.Send(new CreateGift("fail", 500));

        Assert.Equal("gift_rejected", result.Error.Code);
        Assert.Equal(["begin", "handler", "rollback"], app.Trace);
        Assert.Empty(app.Outbox.Events);
    }

    [Fact]
    public async Task Exception_rolls_back_and_propagates()
    {
        await using var app = TestApp.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Send(new CreateGift("throw", 500)));

        Assert.Equal(["begin", "handler", "rollback"], app.Trace);
        Assert.Empty(app.Outbox.Events);
    }

    [Fact]
    public async Task Queries_do_not_open_a_transaction()
    {
        await using var app = TestApp.Create();

        var result = await app.Send(new GetGift(5));

        Assert.Equal(5, result.Value);
        Assert.Equal(["handler"], app.Trace);
    }

    [Fact]
    public async Task Same_idempotency_key_and_body_replays_the_stored_response()
    {
        await using var app = TestApp.Create(idempotencyKey: "key-1");

        var first = await app.Send(new Charge(1500));
        var second = await app.Send(new Charge(1500));

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, app.Trace.Count(t => t == "handler"));
    }

    [Fact]
    public async Task Same_key_with_different_body_is_a_conflict()
    {
        await using var app = TestApp.Create(idempotencyKey: "key-1");

        await app.Send(new Charge(1500));
        var reused = await app.Send(new Charge(9999));

        Assert.Equal("idempotency_key_reused", reused.Error.Code);
        Assert.Equal(ErrorType.Conflict, reused.Error.Type);
        Assert.Equal(1, app.Trace.Count(t => t == "handler"));
    }

    [Fact]
    public async Task Idempotency_key_is_scoped_per_actor()
    {
        var store = new InMemoryIdempotencyStore();
        await using var leyla = TestApp.Create(idempotencyKey: "key-1", store: store);
        await using var orxan = TestApp.Create(idempotencyKey: "key-1", store: store);

        Assert.True((await leyla.Send(new Charge(1500))).IsSuccess);
        Assert.True((await orxan.Send(new Charge(9999))).IsSuccess);
    }

    [Fact]
    public async Task Idempotent_command_requires_a_key_and_failures_are_not_stored()
    {
        await using var noKey = TestApp.Create(idempotencyKey: null);
        await using var app = TestApp.Create(idempotencyKey: "key-2");

        Assert.Equal("idempotency_key_required", (await noKey.Send(new Charge(1500))).Error.Code);

        Assert.Equal("payment_declined", (await app.Send(new Charge(-1))).Error.Code);
        Assert.Empty(app.Store.Records);
        Assert.True((await app.Send(new Charge(1500))).IsSuccess);
    }

    [Fact]
    public async Task Non_idempotent_commands_ignore_the_key()
    {
        await using var app = TestApp.Create(idempotencyKey: "key-3");

        await app.Send(new CreateGift("Leyla", 500));
        await app.Send(new CreateGift("Leyla", 500));

        Assert.Equal(2, app.Trace.Count(t => t == "handler"));
        Assert.Empty(app.Store.Records);
    }

    [Fact]
    public async Task Logging_never_writes_otp_token_fin_or_card_data()
    {
        await using var app = TestApp.Create();

        await app.Send(new VerifyOtp("ali@example.az", "482913", "5ABC123", "4169738812345678", "eyJhbGciOiJIUzI1NiJ9.secret", Fail: false));
        await app.Send(new VerifyOtp("ali@example.az", "482913", "5ABC123", "4169738812345678", "eyJhbGciOiJIUzI1NiJ9.secret", Fail: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.Send(new VerifyOtp("ali@example.az", "482913", "5ABC123", "4169738812345678", "eyJhbGciOiJIUzI1NiJ9.secret", Fail: false, Throw: true)));

        var logged = string.Join('\n', app.Logs);
        Assert.Contains(nameof(VerifyOtp), logged, StringComparison.Ordinal);
        Assert.Contains("otp_invalid", logged, StringComparison.Ordinal);
        foreach (var secret in new[] { "482913", "5ABC123", "4169738812345678", "eyJhbGciOiJIUzI1NiJ9", "ali@example.az" })
        {
            Assert.DoesNotContain(secret, logged, StringComparison.Ordinal);
        }
    }

    // ---------- test tətbiqi ----------

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly ServiceProvider _root;
        private readonly AsyncServiceScope _scope;

        private TestApp(ServiceProvider root, List<string> trace, FakeOutbox outbox, InMemoryIdempotencyStore store, ConcurrentQueue<string> logs)
        {
            _root = root;
            _scope = root.CreateAsyncScope();
            Trace = trace;
            Outbox = outbox;
            Store = store;
            Logs = logs;
        }

        public List<string> Trace { get; }

        public FakeOutbox Outbox { get; }

        public InMemoryIdempotencyStore Store { get; }

        public ConcurrentQueue<string> Logs { get; }

        public static TestApp Create(bool authenticated = true, string[]? roles = null, string? idempotencyKey = null, InMemoryIdempotencyStore? store = null)
        {
            var trace = new List<string>();
            var outbox = new FakeOutbox(trace);
            store ??= new InMemoryIdempotencyStore();
            var logs = new ConcurrentQueue<string>();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(logs)).SetMinimumLevel(LogLevel.Trace));
            services.AddSingleton(trace);
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<ICurrentUser>(new FakeCurrentUser(authenticated, roles ?? ["User"]));
            services.AddSingleton<IRequestContext>(new FakeRequestContext(idempotencyKey));
            services.AddSingleton<IIdempotencyStore>(store);
            services.AddSingleton<IOutbox>(outbox);
            services.AddScoped<IUnitOfWork, FakeUnitOfWork>();
            services.AddScoped<IAggregateTracker, AggregateTracker>();
            services.AddMessaging(RequestTypes);
            services.AddApplicationPipeline();

            return new TestApp(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), trace, outbox, store, logs);
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request) =>
            _scope.ServiceProvider.GetRequiredService<ISender>().Send(request, TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _root.DisposeAsync();
        }
    }

    private sealed class FakeCurrentUser(bool authenticated, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated { get; } = authenticated;

        public Guid? ActorId { get; } = authenticated ? Guid.CreateVersion7() : null;

        public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();
    }

    private sealed class FakeRequestContext(string? key) : IRequestContext
    {
        public string? IdempotencyKey { get; } = key;
    }

    private sealed class FakeUnitOfWork(List<string> trace) : IUnitOfWork
    {
        public Task BeginAsync(CancellationToken cancellationToken) { trace.Add("begin"); return Task.CompletedTask; }

        public Task CommitAsync(CancellationToken cancellationToken) { trace.Add("commit"); return Task.CompletedTask; }

        public Task RollbackAsync(CancellationToken cancellationToken) { trace.Add("rollback"); return Task.CompletedTask; }
    }

    public sealed class FakeOutbox(List<string> trace) : IOutbox
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task AddAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            trace.Add($"outbox:{events.Count}");
            Events.AddRange(events);
            return Task.CompletedTask;
        }
    }

    public sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        public ConcurrentDictionary<(string, string), IdempotencyRecord> Records { get; } = new();

        public Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken) =>
            Task.FromResult(Records.GetValueOrDefault((scope, key)));

        public Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken cancellationToken) =>
            Task.FromResult(Records.GetOrAdd((record.Scope, record.Key), record));
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                sink.Enqueue(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    sink.Enqueue(string.Join(';', pairs.Select(p => $"{p.Key}={p.Value}")));
                }

                if (exception is not null)
                {
                    sink.Enqueue(exception.ToString());
                }
            }
        }
    }

    // ---------- nümunə sorğular ----------

    public readonly record struct GiftId(Guid Value) : IStronglyTypedId<GiftId>
    {
        public static GiftId From(Guid value) => new(value);
    }

    public sealed record GiftCreated(GiftId GiftId, DateTimeOffset At) : DomainEvent(At);

    private sealed class Gift : AggregateRoot<GiftId>
    {
        public Gift(DateTimeOffset now)
            : base(StronglyTypedId.New<GiftId>(), now)
        {
            Raise(new GiftCreated(Id, now));
        }
    }

    public sealed record CreateGift(string RecipientName, long AmountQepik) : ICommand<GiftId>;

    public sealed class CreateGiftValidator : Validator<CreateGift>
    {
        public CreateGiftValidator()
        {
            RuleFor(x => x.RecipientName).NotEmpty();
            RuleFor(x => x.AmountQepik).InRange(200, 20_000);
        }
    }

    public sealed class CreateGiftHandler(IAggregateTracker tracker, List<string> trace, IClock clock) : ICommandHandler<CreateGift, GiftId>
    {
        public Task<Result<GiftId>> Handle(CreateGift request, CancellationToken cancellationToken)
        {
            trace.Add("handler");
            if (request.RecipientName == "throw")
            {
                throw new InvalidOperationException("boom");
            }

            var gift = new Gift(clock.UtcNow);
            tracker.Track(gift);

            return Task.FromResult(request.RecipientName == "fail"
                ? Result<GiftId>.Failure(Error.Failure("gift_rejected", "Rədd edildi."))
                : Result<GiftId>.Success(gift.Id));
        }
    }

    public sealed record Charge(long AmountQepik) : ICommand<Guid>, IIdempotentCommand;

    public sealed class ChargeHandler(List<string> trace) : ICommandHandler<Charge, Guid>
    {
        public Task<Result<Guid>> Handle(Charge request, CancellationToken cancellationToken)
        {
            trace.Add("handler");
            return Task.FromResult(request.AmountQepik < 0
                ? Result<Guid>.Failure(Error.Failure("payment_declined", "Ödəniş rədd edildi."))
                : Result<Guid>.Success(Guid.CreateVersion7()));
        }
    }

    [RequiresRole("Admin")]
    public sealed record AdminOnly : ICommand;

    public sealed class AdminOnlyHandler : ICommandHandler<AdminOnly>
    {
        public Task<Result> Handle(AdminOnly request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    [PublicRequest]
    public sealed record PublicPing : ICommand;

    public sealed class PublicPingHandler : ICommandHandler<PublicPing>
    {
        public Task<Result> Handle(PublicPing request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    public sealed record GetGift(int Id) : IQuery<int>;

    public sealed class GetGiftHandler(List<string> trace) : IQueryHandler<GetGift, int>
    {
        public Task<Result<int>> Handle(GetGift request, CancellationToken cancellationToken)
        {
            trace.Add("handler");
            return Task.FromResult(Result<int>.Success(request.Id));
        }
    }

    [PublicRequest]
    public sealed record VerifyOtp(string Email, string Code, string Fin, string CardNumber, string Token, bool Fail, bool Throw = false) : ICommand;

    public sealed class VerifyOtpHandler : ICommandHandler<VerifyOtp>
    {
        public Task<Result> Handle(VerifyOtp request, CancellationToken cancellationToken)
        {
            if (request.Throw)
            {
                throw new InvalidOperationException($"OTP {request.Code} for {request.Email} with card {request.CardNumber}");
            }

            return Task.FromResult(request.Fail ? Result.Failure(Error.Validation("otp_invalid", "Kod yanlışdır.")) : Result.Success());
        }
    }
}
