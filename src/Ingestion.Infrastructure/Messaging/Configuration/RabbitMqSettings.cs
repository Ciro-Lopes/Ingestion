// Production credentials must be supplied via environment variables (e.g. RabbitMq__Username, RabbitMq__Password).
// The values below are defaults that match the local docker-compose.yml setup only.
namespace Ingestion.Infrastructure.Messaging.Configuration;

public class RabbitMqSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public ExchangeSettings Exchanges { get; set; } = new();
    public List<QueueDefinition> Queues { get; set; } = new();
}

public class ExchangeSettings
{
    public string Inbound { get; set; } = "ingestion.inbound";
    public string Outbound { get; set; } = "ingestion.outbound";
    public string DeadLetter { get; set; } = "ingestion.dlx";
}
