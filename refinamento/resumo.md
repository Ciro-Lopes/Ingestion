# Context Transfer — Microsserviço `ingestion`

**Workspace:** `C:\Development\Projects\data-distribution\ingestion`
**Solution:** `ingestion.sln` — 7 projetos — build: `0 errors, 0 warnings`
**Status:** Implementação 100% completa. Próxima etapa: testes.

---

## 1. Stack e Padrões de Arquitetura

- **.NET 8 Worker Service** — sem HTTP, puramente event-driven via `BackgroundService`
- **Arquitetura Hexagonal (Ports & Adapters)**: dependência unidirecional `Worker → Infrastructure → Application → Domain`
- **RabbitMQ.Client v7.2.1** — API async (`IChannel`, `CreateChannelAsync`, `BasicPublishAsync`, `AsyncEventingBasicConsumer`)
- **StackExchange.Redis v2.11.8** — `IConnectionMultiplexer` singleton
- **Dapper v2.1.66 + Npgsql v10.0.1** — SQL explícito, sem ORM
- **System.Threading.Channels** — buffer de batch in-box, sem pacote externo
- **xUnit v2.5.3 + Moq v4.20.72 + FluentAssertions v8.8.0** — stack de testes

---

## 2. Estrutura de Projetos

```
ingestion.sln
├── src/
│   ├── Ingestion.Domain          → Entities, ValueObjects, Ports (interfaces)
│   ├── Ingestion.Application     → UseCases, DTOs, Mappers, BatchProcessor
│   ├── Ingestion.Infrastructure  → RabbitMQ, Redis, PostgreSQL adapters
│   └── Ingestion.Worker          → Program.cs (DI root), BackgroundService workers, Dockerfile
└── tests/
    ├── Ingestion.Domain.Tests
    ├── Ingestion.Application.Tests
    └── Ingestion.Infrastructure.Tests   ← nenhum teste implementado ainda
```

---

## 3. Estado da Implementação

Todos os arquivos de produção estão criados e compilando. Nenhum arquivo de teste foi implementado.

### Domain (`Ingestion.Domain`)
- `ValueObjects/CompositeId.cs` — `sealed record CompositeId(string Id, DateOnly ReferenceDate, string Type)`, `ToString()` → `"{Id}_{yyyyMMdd}_{Type}"`
- `Entities/Trade.cs` — imutável, 9 propriedades: `CompositeId`, `Quantity`, `ReferenceDate`, `Type`, `Status`, `RawMessage` (string), `Metadata` (string), `CreatedAt`, `UpdatedAt`
- `Ports/Contracts/QueueConsumerConfigDto.cs` — `record(QueueName, BatchSize, ParallelConsumers, IsEnabled)` — no Domain para evitar dependência circular
- `Ports/Outbound/`: `ITradeRepository`, `ICacheService`, `IMessagePublisher`
- `Ports/Inbound/IProcessMessageUseCase<TDto>` — `Task ExecuteAsync(TDto, CancellationToken)`

### Application (`Ingestion.Application`)
- `DTOs/`: `TradeMessageDto` (entrada), `TradeOutboundDto` (saída: `CompositeId` + `UpdatedAt`), `BatchSettings` (`DefaultSize=100, FlushIntervalSeconds=5`), `OutboundSettings`
- `Mappers/TradeMapper.cs` — `static ToEntity(dto)`
- `UseCases/BatchProcessor<T>` — `Channel<(T Item, TaskCompletionSource Ack)>` bounded, flush por tamanho **ou** timer (`CancelAfter`), ACK propagado via `TaskCompletionSource`
- `UseCases/ProcessTradeUseCase`:
  - Constrói `BatchProcessor<T>` internamente (não via DI)
  - `StartAsync(ct)` → expõe `_batchProcessor.StartProcessingAsync(ct)` para ser chamado pelo `BatchLoopHostedService`
  - `ExecuteAsync` → idempotência via `ICacheService.GetLastUpdatedAtAsync`; enfileira no batch e aguarda `ack.Task` antes de retornar (ACK do RabbitMQ só após flush)
  - `FlushBatchAsync` → `UpsertBatchAsync` + `SetLastUpdatedAtAsync` por item + `PublishAsync` por item

### Infrastructure (`Ingestion.Infrastructure`)
- `Messaging/Configuration/RabbitMqSettings.cs` — `Host, Port, Username, Password, VirtualHost, ExchangeSettings, List<QueueDefinition>`
- `Messaging/Configuration/QueueDefinition.cs` — `Name, DeadLetterQueue, InboundRoutingKey, OutboundExchange, OutboundRoutingKey, FlowName`
- `Messaging/Configuration/QueueConsumerConfigStore.cs` — `ConcurrentDictionary<string, QueueConsumerConfigDto>`, `GetConfig()` retorna defaults em miss
- `Messaging/Consumers/BaseConsumer<TDto>` — cria `IChannel` por chamada de `StartConsumingAsync`; declara DLX exchange + DLQ + fila principal; `BasicQos` = `config.BatchSize`; `AsyncEventingBasicConsumer.ReceivedAsync`: deserialize → `ProcessAsync` → `BasicAck`; serialização falha → `BasicNack(requeue:false)`; exceção em processamento → `BasicNack(requeue:false)` → DLQ; `OperationCanceledException` → `BasicNack(requeue:true)` → rethrow
- `Messaging/Consumers/TradeConsumer.cs` — herda `BaseConsumer<TDto>`, `ProcessAsync` delega para `IProcessMessageUseCase<TDto>`
- `Messaging/Consumers/QueueConfigPollingWorker.cs` — `BackgroundService`, `PeriodicTimer`, polling Redis por fila, exceções por fila são swallowed como `Warning`
- `Messaging/Publishers/RabbitMqPublisher.cs` — `IMessagePublisher + IAsyncDisposable`; `IChannel` singleton com `SemaphoreSlim(1,1)`; `DeliveryModes.Persistent`; double-checked locking na criação do canal
- `Cache/RedisCacheService.cs` — fail-safe (exceções nunca propagam); `DateTime` serializado como ISO 8601 `"O"`; `GetQueueConfigAsync` retorna `BatchSize=100, ParallelConsumers=1, IsEnabled=true` em miss/falha
- `Cache/Configuration/RedisSettings.cs` — `ConnectionString, ConfigPollingIntervalSeconds=30, KeyPrefix="ingestion"`
- `Persistence/DbConnectionFactory.cs` — `IDbConnectionFactory` + `NpgsqlConnectionFactory`; caller abre conexão
- `Persistence/Repositories/TradeRepository.cs` — `sealed`; upsert em lote com transação; `INSERT ... ON CONFLICT DO UPDATE WHERE updated_at < EXCLUDED.updated_at`; `DateTime.SpecifyKind(..., Utc)`; rollback + rethrow em exceção
- `Persistence/Scripts/V1__create_trades_table.sql`

### Worker (`Ingestion.Worker`)
- `Workers/TradeWorker.cs` — `BackgroundService`, chama `consumer.StartConsumingAsync(ct)`, swallow `OperationCanceledException`
- `Program.cs` — composition root completo:
  - `IConnection` Singleton via `factory.CreateConnectionAsync(...).GetAwaiter().GetResult()`
  - `IConnectionMultiplexer` Singleton
  - `ICacheService, IMessagePublisher, IDbConnectionFactory, ITradeRepository, QueueConsumerConfigStore` → Singleton
  - `IProcessMessageUseCase<TradeMessageDto>` → Singleton
  - `BatchLoopHostedService` (classe file-level interna) → chama `use_case.StartAsync(ct)` para rodar o loop do `BatchProcessor<T>`
  - `TradeConsumer` → Singleton, wired com `QueueDefinition` filtrada por `FlowName == "trade"`
  - Hosted services registrados: `TradeWorker`, `QueueConfigPollingWorker`, `BatchLoopHostedService`
- `Dockerfile` — multi-stage: `mcr.microsoft.com/dotnet/sdk:8.0` → `mcr.microsoft.com/dotnet/runtime:8.0`
- `appsettings.json` — seções: `RabbitMq` (com array `Queues` para trade), `Redis`, `Database`, `Batch`, `Outbound`, `Logging`
- `appsettings.Development.json` — log level `Debug`

---

## 4. Dependências Críticas entre Arquivos

| Dependente | Depende de | Detalhe |
|---|---|---|
| `ProcessTradeUseCase` | `BatchProcessor<Trade>` | Criado internamente no construtor; não registrado no DI |
| `BatchLoopHostedService` | `ProcessTradeUseCase.StartAsync` | Cast explícito de `IProcessMessageUseCase<TradeMessageDto>` para `ProcessTradeUseCase` em `Program.cs` |
| `BaseConsumer<TDto>` | `QueueConsumerConfigStore` | Lê `BatchSize` para `BasicQos` e `IsEnabled` por mensagem |
| `TradeConsumer` | `QueueDefinition` | Injetado diretamente (não via `IOptions`); selecionado por `FlowName` em `Program.cs` |
| `RabbitMqPublisher` | `IChannel` | Criado lazily com double-checked locking; `SemaphoreSlim(1,1)` como guarda |
| `RedisCacheService` | `RedisSettings.KeyPrefix` | Todas as chaves são prefixadas como `"ingestion:{tipo}:{compositeId}:updated_at"` |
| `TradeRepository` | `IDbConnectionFactory` | Abre nova conexão por chamada; Singleton é seguro porque não há estado de conexão compartilhado |
| `QueueConsumerConfigDto` | `Ingestion.Domain` | Movido do Application para Domain para evitar referência circular `Infrastructure → Application → Domain` |

---

## 5. Chaves Redis

| Finalidade | Padrão |
|---|---|
| Versioning trade | `ingestion:trade:{compositeId}:updated_at` |
| Config de fila | `ingestion:config:{queueName}` |

---

## 6. Filas RabbitMQ

| Fluxo | Fila principal | DLQ |
|---|---|---|
| trade | `ingestion.trade` | `ingestion.trade.dead-letter` |

DLX exchange: `ingestion.dlx`

---

## 7. Próximo Passo Imediato

**Etapa 6 do plano: implementar testes.** Os projetos de teste existem na solution mas estão vazios.

Ordem recomendada:
1. **`Ingestion.Domain.Tests`** — `CompositeId` (ToString, igualdade por valor), construtores de `Trade`
2. **`Ingestion.Application.Tests`** — `ProcessTradeUseCase` com mocks de `ICacheService`, `ITradeRepository`, `IMessagePublisher`; `BatchProcessor<T>` (flush por tamanho, flush por timer, ACK propagation); `TradeMapper`
3. **`Ingestion.Infrastructure.Tests`** — integração com PostgreSQL em container (Testcontainers); integração com Redis em container; `RedisCacheService` (fail-safe, serialização DateTime)

**Ponto de atenção para testes de use case:** `ProcessTradeUseCase.StartAsync(ct)` precisa ser chamado (e rodando) antes de `ExecuteAsync`, pois o `BatchProcessor<T>` bloqueia em `_channel.Reader.ReadAsync` — em testes, usar `Task.Run(() => useCase.StartAsync(cts.Token))` antes de chamar `ExecuteAsync`.
