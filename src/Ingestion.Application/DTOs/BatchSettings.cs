namespace Ingestion.Application.DTOs;

public class BatchSettings
{
    public int DefaultSize { get; set; } = 100;
    public int FlushIntervalSeconds { get; set; } = 5;
}
