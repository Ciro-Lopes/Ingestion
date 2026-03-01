namespace Ingestion.Application.DTOs;

public record TradeMessageDto(
    string Id,
    decimal Quantity,
    DateOnly ReferenceDate,
    string Type,
    string Status,
    DateTime UpdatedAt,
    string RawPayload,
    string MetadataPayload
);
