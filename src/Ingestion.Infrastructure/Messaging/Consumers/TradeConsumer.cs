using Ingestion.Application.DTOs;
using Ingestion.Domain.Ports.Inbound;
using Ingestion.Infrastructure.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Ingestion.Infrastructure.Messaging.Consumers;

public sealed class TradeConsumer : BaseConsumer<TradeMessageDto>
{
    private readonly IProcessMessageUseCase<TradeMessageDto> _useCase;

    public TradeConsumer(
        IConnection connection,
        QueueConsumerConfigStore configStore,
        ILogger<TradeConsumer> logger,
        QueueDefinition queueDefinition,
        ExchangeSettings exchanges,
        IProcessMessageUseCase<TradeMessageDto> useCase)
        : base(connection, configStore, logger, queueDefinition, exchanges)
    {
        _useCase = useCase;
    }

    protected override Task ProcessAsync(TradeMessageDto dto, CancellationToken cancellationToken)
        => _useCase.ExecuteAsync(dto, cancellationToken);
}
