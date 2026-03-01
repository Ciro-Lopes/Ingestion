# Task 9 — RabbitMQ Consumers e QueueConfigPollingWorker

## Objetivo

Implementar os Consumers RabbitMQ na camada de infraestrutura: o `BaseConsumer<TDto>` abstrato que encapsula toda a lógica de conexão, declaração de filas, DLQ, deserialização e tratamento de erros, e o consumer concreto `TradeConsumer`. Implementar também o `QueueConfigPollingWorker`, que atualiza periodicamente a configuração de consumo a partir do Redis.

---

## Principais Entregas

- `BaseConsumer<TDto>` abstrato com toda a lógica compartilhada de consumo
- Declaração de fila principal e DLQ no RabbitMQ no startup do consumer
- Nack automático para DLQ em caso de exceção não tratada
- `TradeConsumer` como implementação concreta do `BaseConsumer`
- `QueueConsumerConfigStore` — singleton em memória com a configuração atual de todas as filas
- `QueueConfigPollingWorker` — `BackgroundService` que atualiza o `QueueConsumerConfigStore` periodicamente via Redis

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando os consumers RabbitMQ do microsserviço `ingestion`. Os arquivos ficam em `src/Ingestion.Infrastructure/Messaging/Consumers/`. Use a versão do `RabbitMQ.Client` compatível com .NET 8.

**1. QueueConsumerConfigStore**

Crie `src/Ingestion.Infrastructure/Messaging/Configuration/QueueConsumerConfigStore.cs`:

Uma classe `sealed` que funciona como um registry thread-safe (use `ConcurrentDictionary`) para armazenar as configurações de consumo por nome de fila:

```csharp
public sealed class QueueConsumerConfigStore
{
    public void UpdateConfig(string queueName, QueueConsumerConfigDto config);
    public QueueConsumerConfigDto GetConfig(string queueName);
}
```

Se a fila não estiver no store, retorne uma configuração default segura: `BatchSize = 100`, `ParallelConsumers = 1`, `IsEnabled = true`.

**2. QueueConfigPollingWorker**

Crie `src/Ingestion.Infrastructure/Messaging/Consumers/QueueConfigPollingWorker.cs`:

`BackgroundService` que:
- Recebe `ICacheService`, `QueueConsumerConfigStore`, `IOptions<RabbitMqSettings>` e `ILogger` no construtor
- Em `ExecuteAsync`, executa um loop com `PeriodicTimer` usando `RedisSettings.ConfigPollingIntervalSeconds`
- A cada iteração, para cada `QueueDefinition` do `RabbitMqSettings.Queues`:
  - Chama `cacheService.GetQueueConfigAsync(queue.Name, ct)`
  - Chama `store.UpdateConfig(queue.Name, config)`
- Loga cada atualização em `Debug`
- Em caso de exceção: loga como `Warning` e continua o loop (não interromper o polling por falha temporária)

**3. BaseConsumer<TDto>**

Crie `src/Ingestion.Infrastructure/Messaging/Consumers/BaseConsumer.cs`:

Classe abstrata e genérica `BaseConsumer<TDto>` que:
- Recebe no construtor:
  - `IConnection connection`
  - `QueueConsumerConfigStore configStore`
  - `ILogger logger`
  - `QueueDefinition queueDefinition`
- Método público `Task StartConsumingAsync(CancellationToken ct)` que:
  1. Cria um canal RabbitMQ a partir da `IConnection`
  2. Declara o DLX exchange (`ingestion.dlx`) como `direct` e durável
  3. Declara a DLQ com os argumentos corretos
  4. Declara a fila principal com `x-dead-letter-exchange` e `x-dead-letter-routing-key` apontando para a DLQ
  5. Configura `BasicQos` com `prefetchCount` = `configStore.GetConfig(queueDefinition.Name).BatchSize`
  6. Registra um `AsyncEventingBasicConsumer` que:
     - Verifica `configStore.GetConfig().IsEnabled` — se falso: nack + requeue e retorna
     - Chama `DeserializeMessage(byte[] body)` para obter o `TDto`
     - Chama o método abstrato `ProcessAsync(TDto dto, CancellationToken ct)`
     - Em caso de sucesso: `BasicAck`
     - Em caso de exceção: loga o erro + `BasicNack` com `requeue: false` (vai para DLQ)
- Método abstrato protegido `Task ProcessAsync(TDto dto, CancellationToken ct)`
- Método protegido `TDto DeserializeMessage(byte[] body)` usando `System.Text.Json.JsonSerializer.Deserialize` com opções camelCase

**4. TradeConsumer**

Crie `src/Ingestion.Infrastructure/Messaging/Consumers/TradeConsumer.cs`:

```csharp
public sealed class TradeConsumer : BaseConsumer<TradeMessageDto>
```

- Recebe adicionalmente `IProcessMessageUseCase<TradeMessageDto> useCase` no construtor
- Implementa `ProcessAsync` chamando `useCase.ExecuteAsync(dto, ct)`

**Boas práticas obrigatórias:**
- Sempre usar `async`/`await` nos event handlers do RabbitMQ — configurar o canal com `DispatchConsumersAsync = true`
- O `BasicNack` com `requeue: false` deve ser chamado mesmo em caso de erro de deserialização (mensagem inválida não deve ser recolocada na fila principal)
- Os canais RabbitMQ não são thread-safe — cada consumer deve ter seu próprio canal
- Não capturar `OperationCanceledException` junto com exceções genéricas — deixar o cancelamento propagar
- Namespace: `Ingestion.Infrastructure.Messaging.Consumers`
