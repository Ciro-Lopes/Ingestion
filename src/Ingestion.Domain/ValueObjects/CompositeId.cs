namespace Ingestion.Domain.ValueObjects;

public sealed record CompositeId(string Id, DateOnly ReferenceDate, string Type)
{
    public override string ToString() => $"{Id}_{ReferenceDate:yyyyMMdd}_{Type}";
}
