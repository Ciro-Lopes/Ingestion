namespace Ingestion.Application.DTOs;

/// <summary>
/// Settings for outbound message publishing to the next microservice.
/// Bound from the "Outbound" configuration section.
/// In production, override via environment variables (Outbound__TradeExchange, etc.).
/// </summary>
public class OutboundSettings
{
    public string TradeExchange { get; set; } = "ingestion.outbound";
    public string TradeRoutingKey { get; set; } = "trade.processed";
}
