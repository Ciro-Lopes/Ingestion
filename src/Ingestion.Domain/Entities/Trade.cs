using Ingestion.Domain.ValueObjects;

namespace Ingestion.Domain.Entities;

public sealed class Trade
{
    public CompositeId CompositeId { get; }
    public decimal Quantity { get; }
    public DateOnly ReferenceDate { get; }
    public string Type { get; }
    public string Status { get; }
    public string RawMessage { get; }
    public string Metadata { get; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; }

    public Trade(
        CompositeId compositeId,
        decimal quantity,
        DateOnly referenceDate,
        string type,
        string status,
        string rawMessage,
        string metadata,
        DateTime createdAt,
        DateTime updatedAt)
    {
        CompositeId = compositeId;
        Quantity = quantity;
        ReferenceDate = referenceDate;
        Type = type;
        Status = status;
        RawMessage = rawMessage;
        Metadata = metadata;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }
}
