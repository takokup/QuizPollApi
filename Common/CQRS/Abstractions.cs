namespace QuizPollApi.Common.CQRS;
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

public interface ICommand<TResult> { }

public interface IQuery<TResult> { }

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
public interface IEvent { }

public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task Handle(TEvent @event, CancellationToken ct);
}

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;
}

public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
