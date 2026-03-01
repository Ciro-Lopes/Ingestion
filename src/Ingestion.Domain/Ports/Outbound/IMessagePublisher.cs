namespace Ingestion.Domain.Ports.Outbound;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken)
        where T : notnull;
}
