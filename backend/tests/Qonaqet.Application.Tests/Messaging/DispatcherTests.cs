using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Infrastructure.Messaging;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Tests.Messaging;

public sealed class DispatcherTests
{
    private static readonly Type[] Handled =
    [
        typeof(CreateThing), typeof(CreateThingHandler),
        typeof(Ping), typeof(PingHandler),
        typeof(GetThing), typeof(GetThingHandler),
    ];

    [Fact]
    public async Task Command_with_result_is_dispatched_to_its_handler()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new CreateThing("çay"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("çay#1", result.Value);
    }

    [Fact]
    public async Task Command_without_value_and_query_are_dispatched()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var ping = await sender.Send(new Ping(), TestContext.Current.CancellationToken);
        var missing = await sender.Send(new GetThing(0), TestContext.Current.CancellationToken);
        var found = await sender.Send(new GetThing(7), TestContext.Current.CancellationToken);

        Assert.True(ping.IsSuccess);
        Assert.Equal("thing_not_found", missing.Error.Code);
        Assert.Equal(7, found.Value);
    }

    [Fact]
    public async Task Behaviors_run_in_registration_order_around_the_handler()
    {
        var log = new CallLog();
        var services = new ServiceCollection().AddSingleton(log);
        services.AddPipelineBehavior(typeof(OuterBehavior<,>));
        services.AddPipelineBehavior(typeof(InnerBehavior<,>));
        services.AddMessaging(Handled);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISender>().Send(new CreateThing("x"), TestContext.Current.CancellationToken);

        Assert.Equal(["outer:before", "inner:before", "handler", "inner:after", "outer:after"], log.Entries);
    }

    [Fact]
    public async Task Behavior_can_short_circuit_with_a_failure()
    {
        var services = new ServiceCollection().AddSingleton(new CallLog());
        services.AddPipelineBehavior(typeof(RejectingBehavior<,>));
        services.AddMessaging(Handled);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new GetThing(7), TestContext.Current.CancellationToken);

        Assert.Equal("rejected", result.Error.Code);
        Assert.Empty(provider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public void Request_without_handler_fails_at_registration_not_at_send()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddMessaging([.. Handled, typeof(Orphan)]));

        Assert.Contains(nameof(Orphan), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_handlers_for_one_request_fail_at_registration()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddMessaging([.. Handled, typeof(SecondPingHandler)]));

        Assert.Contains(nameof(SecondPingHandler), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assembly_scan_registers_handlers_and_sender()
    {
        var services = new ServiceCollection();

        services.AddMessaging(typeof(ApplicationAssembly).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(ISender));
    }

    [Fact]
    public void Non_behavior_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPipelineBehavior(typeof(List<>)));
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection().AddSingleton(new CallLog());
        services.AddMessaging(Handled);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public sealed class CallLog
    {
        public List<string> Entries { get; } = [];
    }

    public sealed record CreateThing(string Name) : ICommand<string>;

    public sealed class CreateThingHandler(CallLog log) : ICommandHandler<CreateThing, string>
    {
        public Task<Result<string>> Handle(CreateThing request, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.FromResult(Result<string>.Success($"{request.Name}#1"));
        }
    }

    public sealed record Ping : ICommand;

    public sealed class PingHandler : ICommandHandler<Ping>
    {
        public Task<Result> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    public sealed class SecondPingHandler : ICommandHandler<Ping>
    {
        public Task<Result> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    public sealed record GetThing(int Id) : IQuery<int>;

    public sealed class GetThingHandler : IQueryHandler<GetThing, int>
    {
        public Task<Result<int>> Handle(GetThing request, CancellationToken cancellationToken) =>
            Task.FromResult(request.Id == 0
                ? Result<int>.Failure(Error.NotFound("thing_not_found", "Tapılmadı."))
                : Result<int>.Success(request.Id));
    }

    public sealed record Orphan : ICommand;

    public sealed class OuterBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add("outer:before");
            var response = await next(cancellationToken);
            log.Entries.Add("outer:after");
            return response;
        }
    }

    public sealed class InnerBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add("inner:before");
            var response = await next(cancellationToken);
            log.Entries.Add("inner:after");
            return response;
        }
    }

    public sealed class RejectingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : Result
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var error = Error.Forbidden("rejected", "Rədd edildi.");
            var response = typeof(TResponse) == typeof(Result)
                ? Result.Failure(error)
                : (Result)typeof(TResponse).GetMethod(nameof(Result.Failure), [typeof(Error)])!.Invoke(null, [error])!;
            return Task.FromResult((TResponse)response);
        }
    }
}
