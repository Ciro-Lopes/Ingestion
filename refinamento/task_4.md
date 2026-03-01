# Task 4 — Use Cases e Controle de Batch

## Objetivo

Implementar o Use Case `ProcessTradeUseCase` na camada de aplicação, incluindo a lógica de versioning via cache, o controle de acúmulo de mensagens em lote via `Channel<T>` e a orquestração de persistência + publicação quando o batch for disparado.

---

## Principais Entregas

- `ProcessTradeUseCase` implementando `IProcessMessageUseCase<TradeMessageDto>`
- `BatchProcessor<T>` — componente genérico reutilizável para acumulação e flush de lotes
- Lógica de versioning (comparação de `UpdatedAt` com cache) aplicada antes de enfileirar
- Dois gatilhos de flush: por tamanho máximo e por timer de intervalo
- Todas as dependências externas injetadas via interfaces (ports), sem acoplamento direto à infraestrutura

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando os Use Cases da camada de aplicação do microsserviço `ingestion`. O projeto é `Ingestion.Application`, que referencia `Ingestion.Domain`.

**1. BatchProcessor<T> genérico**

Crie `src/Ingestion.Application/UseCases/BatchProcessor.cs`.

Implemente uma classe genérica `BatchProcessor<T>` que:
- Usa `System.Threading.Channels.Channel<(T Item, TaskCompletionSource Ack)>` internamente para receber itens de forma thread-safe
- Expõe um método `Task EnqueueAsync(T item, TaskCompletionSource ack, CancellationToken ct)` para adicionar itens
- Executa internamente um loop de leitura (`Task StartProcessingAsync`) que descobre o momento de flush usando dois critérios:
  - **Por tamanho**: quando acumula `BatchSize` itens
  - **Por tempo**: quando o timer de `FlushIntervalSeconds` dispara, mesmo com batch incompleto
- Ao fazer flush, invoca um `Func<IReadOnlyList<T>, CancellationToken, Task>` fornecido via construtor (o delegate de processamento)
- Recebe `BatchSize` e `FlushIntervalSeconds` via construtor
- Deve ser completamente independente de infraestrutura — apenas `System.Threading.Channels` e tipos primitivos

**2. ProcessTradeUseCase**

Crie `src/Ingestion.Application/UseCases/ProcessTradeUseCase.cs`.

Implemente a classe `ProcessTradeUseCase` que:
- Implementa `IProcessMessageUseCase<TradeMessageDto>`
- Recebe via injeção de dependência no construtor:
  - `ICacheService cacheService`
  - `ITradeRepository tradeRepository`
  - `IMessagePublisher messagePublisher`
  - `BatchProcessor<Trade> batchProcessor`
  - `IOptions<BatchSettings> batchSettings`
  - `ILogger<ProcessTradeUseCase> logger`
- No método `ExecuteAsync(TradeMessageDto dto, CancellationToken ct)`:
  1. Constrói o `CompositeId` a partir do DTO
  2. Chama `cacheService.GetLastUpdatedAtAsync(compositeId.ToString(), ct)`
  3. Se o valor retornado não for `null` e `dto.UpdatedAt <= cachedUpdatedAt` → loga como descartado e retorna (não enfileira)
  4. Mapeia o DTO para entidade `Trade` via `TradeMapper.ToEntity(dto)`
  5. Cria um `TaskCompletionSource` e enfileira no `BatchProcessor<Trade>`
  6. Aguarda o `TaskCompletionSource.Task` para garantir que o ACK só ocorra após a persistência
- No delegate de flush do `BatchProcessor`:
  1. Chama `tradeRepository.UpsertBatchAsync(batch, ct)`
  2. Para cada item do batch, chama `cacheService.SetLastUpdatedAtAsync(compositeId, updatedAt, ct)`
  3. Publica no próximo microsserviço via `messagePublisher.PublishAsync(exchange, routingKey, outboundDto, ct)` — os valores de `exchange` e `routingKey` devem vir de `IOptions<RabbitMqOutboundSettings>` injetado
  4. Completa o `TaskCompletionSource` de cada item sinalizando o ACK

**3. BatchSettings**

Crie `src/Ingestion.Application/DTOs/BatchSettings.cs`:
```csharp
public class BatchSettings
{
    public int DefaultSize { get; set; } = 100;
    public int FlushIntervalSeconds { get; set; } = 5;
}
```

**Boas práticas obrigatórias:**
- Os Use Cases não devem importar nenhum namespace de infraestrutura (`RabbitMQ.Client`, `StackExchange.Redis`, `Dapper`, etc.)
- O `BatchProcessor<T>` deve ser genérico e reutilizável para qualquer tipo de entidade
- Toda lógica de retry ou tolerância a falhas no batch deve ser tratada no nível do Use Case ou do BatchProcessor, nunca na camada de infraestrutura
- Logging deve usar `ILogger<T>` — jamais `Console.WriteLine`
- CancellationToken deve ser propagado em todas as chamadas assíncronas
- Namespaces seguem o padrão `Ingestion.Application.{SubPasta}`
