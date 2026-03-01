# Task 5 — Settings e Configurações de Infraestrutura

## Objetivo

Criar todas as classes de configuração fortemente tipadas utilizadas pela camada de infraestrutura, incluindo configurações de RabbitMQ, Redis, banco de dados e definições de filas. Estas classes serão vinculadas ao `appsettings.json` via `IOptions<T>` na camada Worker.

---

## Principais Entregas

- `RabbitMqSettings` — conexão e exchanges do RabbitMQ
- `QueueDefinition` — definição individual de fila (nome, DLQ, routing keys, exchange de saída)
- `RedisSettings` — conexão e intervalo de polling do Redis
- `DatabaseSettings` — connection string do PostgreSQL
- `appsettings.json` e `appsettings.Development.json` no projeto Worker com estrutura completa
- Todas as classes com valores default documentados e validação de campos obrigatórios

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior criando as classes de configuração do microsserviço `ingestion`. O projeto envolvido para as classes é `Ingestion.Infrastructure`, e o `appsettings.json` fica em `Ingestion.Worker`.

**1. RabbitMqSettings**

Crie `src/Ingestion.Infrastructure/Messaging/Configuration/RabbitMqSettings.cs`:

```csharp
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
```

**2. QueueDefinition**

Crie `src/Ingestion.Infrastructure/Messaging/Configuration/QueueDefinition.cs`:

```csharp
public class QueueDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DeadLetterQueue { get; set; } = string.Empty;
    public string InboundRoutingKey { get; set; } = string.Empty;
    public string OutboundExchange { get; set; } = string.Empty;
    public string OutboundRoutingKey { get; set; } = string.Empty;
    public string FlowName { get; set; } = string.Empty; // "trade"
}
```

**3. RedisSettings**

Crie `src/Ingestion.Infrastructure/Cache/Configuration/RedisSettings.cs`:

```csharp
public class RedisSettings
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public int ConfigPollingIntervalSeconds { get; set; } = 30;
    public string KeyPrefix { get; set; } = "ingestion";
}
```

**4. DatabaseSettings**

Crie `src/Ingestion.Infrastructure/Persistence/Configuration/DatabaseSettings.cs`:

```csharp
public class DatabaseSettings
{
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=ingestion;Username=postgres;Password=postgres";
}
```

**5. appsettings.json**

Crie `src/Ingestion.Worker/appsettings.json` com a seguinte estrutura completa:

```json
{
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest",
    "VirtualHost": "/",
    "Exchanges": {
      "Inbound": "ingestion.inbound",
      "Outbound": "ingestion.outbound",
      "DeadLetter": "ingestion.dlx"
    },
    "Queues": [
      {
        "Name": "ingestion.trade",
        "DeadLetterQueue": "ingestion.trade.dead-letter",
        "InboundRoutingKey": "trade",
        "OutboundExchange": "ingestion.outbound",
        "OutboundRoutingKey": "trade.processed",
        "FlowName": "trade"
      }
    ]
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConfigPollingIntervalSeconds": 30,
    "KeyPrefix": "ingestion"
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=ingestion;Username=postgres;Password=postgres"
  },
  "Batch": {
    "DefaultSize": 100,
    "FlushIntervalSeconds": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

Crie `src/Ingestion.Worker/appsettings.Development.json` com overrides para desenvolvimento local (mesmas configurações mas com nível de log `Debug`).

**Boas práticas obrigatórias:**
- Todas as classes de configuração devem ter valores default que funcionem localmente com o `docker-compose.yml`
- Nenhuma senha ou secret deve ser hardcoded para produção — documentar via comentário que devem vir de variáveis de ambiente em ambientes não-locais
- As classes de configuração não devem implementar nenhuma lógica de negócio
- Namespaces devem seguir `Ingestion.Infrastructure.{SubPasta}.Configuration`
