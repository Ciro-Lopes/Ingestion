namespace Ingestion.Domain.Ports.Contracts;

public record QueueConsumerConfigDto(
    string QueueName,
    int BatchSize,
    int ParallelConsumers,
    bool IsEnabled
);
