# Task 6 — Redis Cache Service

## Objetivo

Implementar o adapter `RedisCacheService` na camada de infraestrutura, que concretiza a interface `ICacheService` do domínio utilizando `StackExchange.Redis`. A implementação deve encapsular todo o acesso ao Redis, incluindo serialização de valores, padrão de chaves, tratamento de erros e logging.

---

## Principais Entregas

- `RedisCacheService` implementando `ICacheService`
- Padrão de chaves Redis definido e encapsulado dentro do serviço
- Serialização/deserialização de `DateTime` e `QueueConsumerConfigDto` para Redis
- Tratamento de erros com logging e comportamento fail-safe (não interromper o fluxo por falha de cache)
- Classe completamente testável via mock de `IConnectionMultiplexer`

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando o adapter de cache do microsserviço `ingestion`. O arquivo fica em `src/Ingestion.Infrastructure/Cache/RedisCacheService.cs` e o projeto referencia `StackExchange.Redis` e `Ingestion.Domain`.

**Implementação de `RedisCacheService`:**

A classe deve:
- Implementar `ICacheService` do namespace `Ingestion.Domain.Ports.Outbound`
- Receber via injeção de dependência no construtor:
  - `IConnectionMultiplexer redis`
  - `IOptions<RedisSettings> settings`
  - `ILogger<RedisCacheService> logger`

**Padrão de chaves Redis (encapsulado em métodos privados):**

| Finalidade | Chave |
|---|---|
| Versioning de trade | `{prefix}:trade:{compositeId}:updated_at` |
| Config da fila | `{prefix}:config:{queueName}` |

O `prefix` vem de `RedisSettings.KeyPrefix` (default: `"ingestion"`).

**Método `GetLastUpdatedAtAsync`:**
- Chave: o `compositeKey` passado como parâmetro já é a chave completa formatada pelo Use Case
- Busca o valor via `IDatabase.StringGetAsync(key)`
- Se não existir, retorna `null`
- Se existir, parseia como `DateTime` em formato ISO 8601 UTC e retorna
- Em caso de exceção do Redis: loga o erro como `Warning` e retorna `null` (fail-safe — cache miss não deve interromper o fluxo)

**Método `SetLastUpdatedAtAsync`:**
- Serializa o `DateTime` para string ISO 8601 UTC
- Persiste via `IDatabase.StringSetAsync(key, value)` — sem TTL (os dados são permanentes como controle de versioning)
- Em caso de exceção: loga como `Error` mas não propaga a exceção

**Método `GetQueueConfigAsync`:**
- Busca o valor JSON do Redis via `IDatabase.StringGetAsync(key)`
- Se não existir, retorna um `QueueConsumerConfigDto` com valores default seguros: `BatchSize = 100`, `ParallelConsumers = 1`, `IsEnabled = true`
- Se existir, desserializa o JSON para `QueueConsumerConfigDto` usando `System.Text.Json.JsonSerializer`
- Em caso de exceção: loga como `Warning` e retorna o DTO com valores default

**Método privado auxiliar:**
```csharp
private IDatabase GetDatabase() => _redis.GetDatabase();
```

**Boas práticas obrigatórias:**
- Nunca deixar exceções do Redis propagarem para a camada de aplicação — cache é um auxiliary service
- Usar `async/await` corretamente (não usar `.Result` ou `.Wait()`)
- Logar sempre o contexto da operação (nome da chave, tipo da operação)
- Não expor detalhes do Redis (chaves, configurações internas) para fora da classe
- A classe deve ser `sealed` para evitar herança não intencional
- CancellationToken deve ser repassado onde a API do StackExchange.Redis aceitar (usar overloads com `CommandFlags` quando não houver suporte nativo ao token)
- Namespace: `Ingestion.Infrastructure.Cache`
