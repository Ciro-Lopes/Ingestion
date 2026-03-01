# Refinamento Técnico — Microsserviço `ingestion`

## 1. Visão Geral

O microsserviço `ingestion` é o ponto de entrada do fluxo de distribuição de dados. Ele consome mensagens de filas RabbitMQ, valida versioning via Redis, persiste em lote no PostgreSQL com Dapper e publica no próximo microsserviço.

A arquitetura adotada é **Hexagonal (Ports & Adapters)**, garantindo que o domínio e as regras de negócio sejam completamente independentes de infraestrutura, facilitando testabilidade e manutenção.

---

## 2. Arquitetura Hexagonal

```
┌───────────────────────────────────────────────────────────┐
│                        HOST / WORKER                      │
│   (composition root, DI, configuração, background workers)│
└───────────────┬──────────────────────────────┬────────────┘
                │ aciona                       │ aciona
┌───────────────▼──────────────────────────────▼────────────┐
│                    ADAPTERS (Infrastructure)              │
│  ┌─────────────────┐  ┌──────────┐  ┌──────────────────┐  │
│  │ RabbitMQ        │  │  Redis   │  │  PostgreSQL      │  │
│  │ Consumer/       │  │  Cache   │  │  Repository      │  │
│  │ Publisher       │  │  Adapter │  │  (Dapper)        │  │
│  └────────┬────────┘  └────┬─────┘  └────────┬─────────┘  │
└───────────┼────────────────┼─────────────────┼────────────┘
            │ implementa     │ implementa      │ implementa
┌───────────▼────────────────▼─────────────────▼────────────┐
│                    PORTS (Interfaces do Domínio)          │
│  IMessageConsumer  ICacheService  ITradeRepository        │
│  IMessagePublisher                                        │
└───────────────────────────┬───────────────────────────────┘
                            │ usa
┌───────────────────────────▼───────────────────────────────┐
│                    APPLICATION (Use Cases)                │
│                   ProcessTradeUseCase                     │
└───────────────────────────┬───────────────────────────────┘
                            │ usa
┌───────────────────────────▼───────────────────────────────┐
│                      DOMAIN                               │
│      Entities · Value Objects · Domain Rules              │
└───────────────────────────────────────────────────────────┘
```

---

## 3. Estrutura de Pastas

```
ingestion/
├── src/
│   ├── Ingestion.Domain/
│   │   ├── Entities/
│   │   │   └── Trade.cs
│   │   ├── ValueObjects/
│   │   │   └── CompositeId.cs
│   │   └── Ports/
│   │       ├── Outbound/
│   │       │   ├── ITradeRepository.cs
│   │       │   ├── ICacheService.cs
│   │       │   └── IMessagePublisher.cs
│   │       └── Inbound/
│   │           └── IProcessMessageUseCase.cs
│   │
│   ├── Ingestion.Application/
│   │   ├── UseCases/
│   │   │   └── ProcessTradeUseCase.cs
│   │   ├── DTOs/
│   │   │   ├── TradeMessageDto.cs
│   │   │   └── QueueConsumerConfigDto.cs
│   │   └── Mappers/
│   │       └── TradeMapper.cs
│   │
│   ├── Ingestion.Infrastructure/
│   │   ├── Messaging/
│   │   │   ├── Consumers/
│   │   │   │   ├── BaseConsumer.cs
│   │   │   │   └── TradeConsumer.cs
│   │   │   ├── Publishers/
│   │   │   │   └── RabbitMqPublisher.cs
│   │   │   └── Configuration/
│   │   │       ├── RabbitMqSettings.cs
│   │   │       └── QueueDefinition.cs
│   │   ├── Cache/
│   │   │   ├── RedisCacheService.cs
│   │   │   └── Configuration/
│   │   │       └── RedisSettings.cs
│   │   └── Persistence/
│   │       ├── Repositories/
│   │       │   └── TradeRepository.cs
│   │       ├── Scripts/
│   │       │   └── trade_table.sql
│   │       └── Configuration/
│   │           └── DatabaseSettings.cs
│   │
│   └── Ingestion.Worker/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Workers/
│           ├── TradeWorker.cs
│           └── QueueConfigPollingWorker.cs
│
├── tests/
│   ├── Ingestion.Domain.Tests/
│   ├── Ingestion.Application.Tests/
│   └── Ingestion.Infrastructure.Tests/
│
├── docker-compose.yml
├── docker-compose.override.yml
└── ingestion.sln
```

---

## 4. Camada de Domínio

### 4.1 Value Object — `CompositeId`

O ID da entidade `Trade` é composto pelos campos `id + reference_date + type`. Deve ser modelado como um Value Object imutável, encapsulando a lógica de composição e comparação de igualdade.

```csharp
// Ingestion.Domain/ValueObjects/CompositeId.cs
public sealed record CompositeId(string Id, DateOnly ReferenceDate, string Type)
{
    public override string ToString() => $"{Id}_{ReferenceDate:yyyyMMdd}_{Type}";
}
```

### 4.2 Entidade

A entidade `Trade` deve ser uma classe rica de domínio, sem dependência de infraestrutura.

| Campo          | Tipo           | Observação                              |
|----------------|----------------|-----------------------------------------|
| `CompositeId`  | `CompositeId`  | Value Object (chave composta)           |
| `Quantity`     | `decimal`      |                                         |
| `ReferenceDate`| `DateOnly`     |                                         |
| `Type`         | `string`       |                                         |
| `Status`       | `string`       |                                         |
| `RawMessage`   | `JsonDocument` | Mensagem completa recebida              |
| `Metadata`     | `JsonDocument` | Metadados da mensagem (headers, etc.)   |
| `CreatedAt`    | `DateTime`     | UTC                                     |
| `UpdatedAt`    | `DateTime`     | UTC — campo usado para versioning       |

### 4.3 Ports (Interfaces)

```csharp
// Outbound
public interface ITradeRepository
{
    Task UpsertBatchAsync(IEnumerable<Trade> trades, CancellationToken ct);
}

public interface ICacheService
{
    Task<DateTime?> GetLastUpdatedAtAsync(string compositeKey, CancellationToken ct);
    Task SetLastUpdatedAtAsync(string compositeKey, DateTime updatedAt, CancellationToken ct);
    Task<QueueConsumerConfigDto> GetQueueConfigAsync(string queueName, CancellationToken ct);
}

public interface IMessagePublisher
{
    Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken ct);
}

// Inbound
public interface IProcessMessageUseCase<TDto>
{
    Task ExecuteAsync(TDto dto, CancellationToken ct);
}
```

---

## 5. Camada de Aplicação

### 5.1 Use Cases

O use case do fluxo `trade` é responsável por:

1. Receber o DTO mapeado da mensagem;
2. Verificar no cache a versão vigente do dado;
3. Acumular no batch (canal interno);
4. Persistir em lote quando atingir limite de tamanho **ou** timeout;
5. Atualizar o cache após persistência;
6. Publicar no próximo microsserviço.

```
ProcessTradeUseCase
  ├── Recebe TradeMessageDto
  ├── Verifica ICacheService.GetLastUpdatedAtAsync(compositeKey)
  │     └── Se dto.UpdatedAt <= cached → descarta (ACK sem persistência)
  ├── Mapeia para entidade Trade (via TradeMapper)
  ├── Enfileira no BatchChannel<Trade>
  ├── [quando batch pronto]
  │     ├── ITradeRepository.UpsertBatchAsync(batch)
  │     ├── ICacheService.SetLastUpdatedAtAsync(key, updatedAt)  [por item]
  │     └── IMessagePublisher.PublishAsync(exchange, routingKey, outboundDto)
  └── ACK da mensagem no RabbitMQ
```

### 5.2 Controle de Batch

O batch deve ser controlado por um `Channel<T>` combinado com dois gatilhos:

- **Tamanho**: quando acumular N mensagens (configurável via Redis/appsettings);
- **Tempo**: quando o timer de flush disparar (ex.: a cada 5 segundos), mesmo que o batch esteja incompleto.

Isso evita mensagens presas em buffers em períodos de baixo volume.

### 5.3 DTOs

```csharp
// Entrada — mensagem recebida da fila
public record TradeMessageDto(
    string Id,
    decimal Quantity,
    DateOnly ReferenceDate,
    string Type,
    string Status,
    DateTime UpdatedAt,
    JsonElement RawPayload,
    IDictionary<string, string> Headers
);

// Saída — publicação para o próximo microsserviço
public record TradeOutboundDto(
    string CompositeId,
    DateTime UpdatedAt
);

// Configuração de consumo (lida do Redis)
public record QueueConsumerConfigDto(
    string QueueName,
    int BatchSize,
    int ParallelConsumers,
    bool IsEnabled
);
```

---

## 6. Camada de Infraestrutura

### 6.1 RabbitMQ — Consumers

#### `BaseConsumer<TDto>`

Classe abstrata que encapsula a lógica comum:
- Conecta ao RabbitMQ via `IConnection` (singleton);
- Cria canal e declara fila principal + DLQ;
- Respeita configuração dinâmica de `PrefetchCount` (lida do Redis via `QueueConfigPollingWorker`);
- Em caso de exceção não tratada → nack com `requeue: false` (mensagem vai para a DLQ);
- Chama método abstrato `ProcessAsync(TDto dto)` implementado pelo consumer concreto.

```
DLQ naming convention: {fila-principal}.dead-letter
DLX exchange: ingestion.dlx
```

#### Declaração de filas

| Fluxo      | Fila principal              | DLQ                                   |
|------------|-----------------------------|---------------------------------------|
| `trade`    | `ingestion.trade`           | `ingestion.trade.dead-letter`         |

#### Consumers paralelos

O `BaseConsumer` deve suportar múltiplos canais RabbitMQ em execução simultânea. O número de consumers paralelos é lido do Redis através do `QueueConfigPollingWorker` e aplicado dinamicamente via semáforo ou criação/destruição de canais adicionais.

### 6.2 Redis — `RedisCacheService`

Responsabilidades:
- `GetLastUpdatedAtAsync` / `SetLastUpdatedAtAsync`: controle de versioning de entidades;
- `GetQueueConfigAsync`: leitura de configuração dinâmica de consumo.

**Padrão de chaves Redis:**

| Finalidade                 | Chave                                       |
|----------------------------|---------------------------------------------|
| Versioning de trade        | `ingestion:trade:{compositeId}:updated_at`  |
| Config da fila             | `ingestion:config:{queueName}`              |

### 6.3 PostgreSQL — Repositories com Dapper

O upsert em lote deve ser feito com uma única query por batch usando `INSERT ... ON CONFLICT DO UPDATE`.

```sql
-- Exemplo para trade
INSERT INTO trades (id, quantity, reference_date, type, status, raw_message, metadata, created_at, updated_at)
VALUES (@Id, @Quantity, @ReferenceDate, @Type, @Status, @RawMessage::jsonb, @Metadata::jsonb, @CreatedAt, @UpdatedAt)
ON CONFLICT (id) DO UPDATE
SET quantity       = EXCLUDED.quantity,
    status         = EXCLUDED.status,
    raw_message    = EXCLUDED.raw_message,
    metadata       = EXCLUDED.metadata,
    updated_at     = EXCLUDED.updated_at
WHERE trades.updated_at < EXCLUDED.updated_at;
```

> A cláusula `WHERE trades.updated_at < EXCLUDED.updated_at` é uma segunda camada de proteção contra dados desatualizados a nível de banco.

#### Scripts de criação das tabelas

```sql
-- trade_table.sql
CREATE TABLE IF NOT EXISTS trades (
    id              VARCHAR(255) PRIMARY KEY,
    quantity        NUMERIC(18, 8) NOT NULL,
    reference_date  DATE NOT NULL,
    type            VARCHAR(100) NOT NULL,
    status          VARCHAR(100) NOT NULL,
    raw_message     JSONB NOT NULL,
    metadata        JSONB NOT NULL,
    created_at      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP WITH TIME ZONE NOT NULL
);
```

> O campo `id` armazena o `CompositeId.ToString()` serializado.

### 6.4 `QueueConfigPollingWorker`

`BackgroundService` que executa em loop com intervalo configurável (ex.: 30 segundos). A cada iteração:

1. Lê do Redis as configurações de todas as filas registradas;
2. Atualiza um `IOptionsMonitor<QueueConsumerConfigDto>` ou um singleton de configuração compartilhado;
3. Os consumers consultam esse singleton para ajustar `PrefetchCount` e número de canais paralelos sem reiniciar.

---

## 7. Camada Worker (Host)

### 7.1 `Program.cs` — Composition Root

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        // Infrastructure
        services.AddSingleton<IConnection>(RabbitMqConnectionFactory.Create(ctx.Configuration));
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(ctx.Configuration["Redis:ConnectionString"]));

        // Ports → Adapters
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<ITradeRepository, TradeRepository>();

        // Use Cases
        services.AddSingleton<IProcessMessageUseCase<TradeMessageDto>, ProcessTradeUseCase>();

        // Workers
        services.AddHostedService<TradeWorker>();
        services.AddHostedService<QueueConfigPollingWorker>();

        // Settings
        services.Configure<RabbitMqSettings>(ctx.Configuration.GetSection("RabbitMq"));
        services.Configure<RedisSettings>(ctx.Configuration.GetSection("Redis"));
        services.Configure<DatabaseSettings>(ctx.Configuration.GetSection("Database"));
    })
    .Build();

await host.RunAsync();
```

### 7.2 `appsettings.json` — Estrutura de Configuração

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
    }
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConfigPollingIntervalSeconds": 30
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=ingestion;Username=postgres;Password=postgres"
  },
  "Batch": {
    "DefaultSize": 100,
    "FlushIntervalSeconds": 5
  }
}
```

---

## 8. Docker Compose

### `docker-compose.yml`

```yaml
version: "3.9"

services:
  rabbitmq:
    image: rabbitmq:3.13-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7.2-alpine
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  postgres:
    image: postgres:16-alpine
    ports:
      - "5432:5432"
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: ingestion
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./src/Ingestion.Infrastructure/Persistence/Scripts:/docker-entrypoint-initdb.d
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  ingestion:
    build:
      context: .
      dockerfile: src/Ingestion.Worker/Dockerfile
    depends_on:
      rabbitmq:
        condition: service_healthy
      redis:
        condition: service_healthy
      postgres:
        condition: service_healthy
    environment:
      - DOTNET_ENVIRONMENT=Production
    env_file:
      - .env

volumes:
  postgres_data:
```

---

## 9. Passos de Desenvolvimento

> Os itens a seguir representam a ordem lógica recomendada para implementação, garantindo que cada camada seja validável antes de avançar para a próxima.

### Etapa 1 — Setup do projeto

- [ ] Criar a solution `ingestion.sln` com os 5 projetos (Domain, Application, Infrastructure, Worker, Tests × 3)
- [ ] Configurar referências entre projetos (Worker → Infrastructure → Application → Domain)
- [ ] Adicionar NuGet packages essenciais:
  - `RabbitMQ.Client` (Infrastructure)
  - `StackExchange.Redis` (Infrastructure)
  - `Dapper` + `Npgsql` (Infrastructure)
  - `Microsoft.Extensions.Hosting` (Worker)
  - `Microsoft.Extensions.Options.ConfigurationExtensions` (Worker)
- [ ] Subir o ambiente com `docker-compose up -d` e validar conectividade

### Etapa 2 — Domínio

- [ ] Implementar `CompositeId` (Value Object)
- [ ] Implementar entidade `Trade`
- [ ] Definir todas as interfaces de ports (inbound e outbound)

### Etapa 3 — Aplicação

- [ ] Implementar DTOs e Mappers
- [ ] Implementar `ProcessTradeUseCase` com lógica de versioning e orquestração de batch

### Etapa 4 — Infraestrutura

- [ ] Implementar `RedisCacheService`
- [ ] Implementar `TradeRepository` com Dapper (upsert em lote)
- [ ] Implementar `RabbitMqPublisher`
- [ ] Implementar `BaseConsumer<T>` com suporte a DLQ e consumers paralelos
- [ ] Implementar `TradeConsumer`
- [ ] Implementar `QueueConfigPollingWorker`

### Etapa 5 — Worker / Host

- [ ] Configurar `Program.cs` com DI completa
- [ ] Implementar `TradeWorker` como `BackgroundService`
- [ ] Configurar `appsettings.json` e variáveis de ambiente
- [ ] Criar `Dockerfile` para o Worker

### Etapa 6 — Testes *(próxima etapa)*

- [ ] Testes unitários do domínio (`CompositeId`, entidade `Trade`)
- [ ] Testes unitários dos use cases (com mocks das ports)
- [ ] Testes unitários dos mappers (`TradeMapper`)
- [ ] Testes de integração dos repositórios (PostgreSQL em container)
- [ ] Testes de integração do cache (Redis em container)
- [ ] Testes end-to-end do fluxo completo (RabbitMQ + Redis + PostgreSQL)

---

## 10. Fluxo de Processamento Detalhado

```
[RabbitMQ Queue]
      │
      ▼
[BaseConsumer]
  ├─ Deserializa mensagem → TradeMessageDto
  ├─ Verifica IsEnabled (config Redis) → se false: nack + requeue
  │
  ▼
[IProcessMessageUseCase.ExecuteAsync(dto)]
  ├─ Constrói CompositeId a partir do dto
  ├─ ICacheService.GetLastUpdatedAtAsync(compositeId)
  │     ├─ null → prossegue (dado novo)
  │     └─ dto.UpdatedAt <= cached → descarta (ACK, sem persistência)
  │
  ├─ Mapper → entidade Domain (Trade)
  ├─ Enfileira no BatchChannel<T>
  │
  ▼
[Batch Processor] (disparado por tamanho OU timer)
  ├─ IRepository.UpsertBatchAsync(batch)
  ├─ Para cada item no batch:
  │     └─ ICacheService.SetLastUpdatedAtAsync(compositeId, updatedAt)
  ├─ IMessagePublisher.PublishAsync(outboundExchange, routingKey, outboundDtos)
  └─ ACK individual de cada mensagem do batch
```

---

## 11. Decisões Técnicas e Justificativas

| Decisão | Justificativa |
|---|---|
| Arquitetura Hexagonal | Isola domínio e regras de negócio de frameworks e infraestrutura; facilita testes com mocks |
| Worker Service (sem API HTTP) | Serviço puramente orientado a eventos; não expõe endpoints HTTP |
| `Channel<T>` para batch | Thread-safe, baixa latência, sem polling ativo; integra naturalmente com `BackgroundService` |
| Upsert com `ON CONFLICT DO UPDATE WHERE` | Idempotência a nível de banco; protege contra reprocessamento de mensagens duplicadas |
| Configuração dinâmica via Redis | Permite ajustar comportamento do consumidor sem redeploy |
| Consumers paralelos por fila | Escalabilidade horizontal por fila configurável em tempo de execução |
| DLQ por convenção de nome | Rastreabilidade de mensagens com falha; facilita reprocessamento manual |
| Dapper (sem ORM) | Controle explícito sobre queries; necessário para upsert em lote eficiente |
