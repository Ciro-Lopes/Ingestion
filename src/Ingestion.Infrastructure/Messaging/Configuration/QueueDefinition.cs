namespace Ingestion.Infrastructure.Messaging.Configuration;

public class QueueDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DeadLetterQueue { get; set; } = string.Empty;
    public string InboundRoutingKey { get; set; } = string.Empty;
    public string OutboundExchange { get; set; } = string.Empty;
    public string OutboundRoutingKey { get; set; } = string.Empty;

    /// <summary>
    /// Identifies the processing flow bound to this queue. Accepted values: "trade", "position".
    /// </summary>
    public string FlowName { get; set; } = string.Empty;
}
