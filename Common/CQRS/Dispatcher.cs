using System.Collections.Concurrent;

namespace QuizPollApi.Common.CQRS;

public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> CommandWrappers = new();
    private static readonly ConcurrentDictionary<Type, object> QueryWrappers = new();

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var wrapper = (CommandWrapper<TResult>)CommandWrappers.GetOrAdd(
            command.GetType(),
            t => Activator.CreateInstance(typeof(CommandWrapper<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(command, services, ct);
    }

    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
    {
        var wrapper = (QueryWrapper<TResult>)QueryWrappers.GetOrAdd(
            query.GetType(),
            t => Activator.CreateInstance(typeof(QueryWrapper<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(query, services, ct);
    }
}

internal abstract class CommandWrapper<TResult>
{
    public abstract Task<TResult> Handle(ICommand<TResult> command, IServiceProvider sp, CancellationToken ct);
}

internal sealed class CommandWrapper<TCommand, TResult> : CommandWrapper<TResult>
    where TCommand : ICommand<TResult>
{
    public override Task<TResult> Handle(ICommand<TResult> command, IServiceProvider sp, CancellationToken ct) =>
        sp.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle((TCommand)command, ct);
}

internal abstract class QueryWrapper<TResult>
{
    public abstract Task<TResult> Handle(IQuery<TResult> query, IServiceProvider sp, CancellationToken ct);
}

internal sealed class QueryWrapper<TQuery, TResult> : QueryWrapper<TResult>
    where TQuery : IQuery<TResult>
{
    public override Task<TResult> Handle(IQuery<TResult> query, IServiceProvider sp, CancellationToken ct) =>
        sp.GetRequiredService<IQueryHandler<TQuery, TResult>>().Handle((TQuery)query, ct);
}

public sealed class EventPublisher(IServiceProvider services, ILogger<EventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            try
            {
                await handler.Handle(@event, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Event handler {Handler} failed for {Event}", handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}
