# Task 2 — Camada de Domínio

## Objetivo

Implementar o núcleo do domínio do microsserviço `ingestion`: o Value Object `CompositeId`, a entidade `Trade` e todas as interfaces de ports (inbound e outbound). Esta camada não deve ter nenhuma dependência de infraestrutura ou frameworks externos.

---

## Principais Entregas

- `CompositeId` implementado como `sealed record` imutável com lógica de serialização
- Entidade `Trade` como classe rica de domínio sem acoplamento a infraestrutura
- Interfaces outbound: `ITradeRepository`, `ICacheService`, `IMessagePublisher`
- Interface inbound: `IProcessMessageUseCase<TDto>`
- Sem qualquer referência a pacotes de infraestrutura no projeto `Ingestion.Domain`

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando a camada de domínio de um microsserviço chamado `ingestion`, seguindo arquitetura hexagonal. O projeto `Ingestion.Domain` não deve referenciar nenhum pacote de infraestrutura (sem RabbitMQ, Redis, Dapper, Npgsql, etc.).

**1. Value Object `CompositeId`**

Crie o arquivo `src/Ingestion.Domain/ValueObjects/CompositeId.cs`.

Implemente `CompositeId` como um `sealed record` com os campos `string Id`, `DateOnly ReferenceDate` e `string Type`. Sobrescreva `ToString()` retornando `"{Id}_{ReferenceDate:yyyyMMdd}_{Type}"`. Este value object é usado como chave de identidade composta para a entidade `Trade`.

**2. Entidade `Trade`**

Crie `src/Ingestion.Domain/Entities/Trade.cs`.

A entidade deve ter as seguintes propriedades (somente getters públicos, sem setters públicos):
- `CompositeId CompositeId`
- `decimal Quantity`
- `DateOnly ReferenceDate`
- `string Type`
- `string Status`
- `string RawMessage` — JSON completo da mensagem recebida, armazenado como string
- `string Metadata` — JSON dos metadados (headers, etc.), armazenado como string
- `DateTime CreatedAt` — UTC
- `DateTime UpdatedAt` — UTC

Forneça um construtor público recebendo todos os campos. Não use ORM annotations nem atributos de serialização.

**3. Interfaces de Ports Outbound**

Crie os seguintes arquivos dentro de `src/Ingestion.Domain/Ports/Outbound/`:

`ITradeRepository.cs`:
```csharp
Task UpsertBatchAsync(IEnumerable<Trade> trades, CancellationToken cancellationToken);
```

`ICacheService.cs` — três métodos:
- `Task<DateTime?> GetLastUpdatedAtAsync(string compositeKey, CancellationToken cancellationToken)`
- `Task SetLastUpdatedAtAsync(string compositeKey, DateTime updatedAt, CancellationToken cancellationToken)`
- `Task<QueueConsumerConfigDto> GetQueueConfigAsync(string queueName, CancellationToken cancellationToken)`

> Nota: `QueueConsumerConfigDto` será definido na camada Application. Para evitar dependência circular, declare um tipo temporário ou use um tipo genérico `object` nesta interface e ajuste na Task 3 quando o DTO for criado.  
> Alternativa recomendada: mova o DTO `QueueConsumerConfigDto` para o projeto `Ingestion.Domain` dentro de uma pasta `Ports/Contracts/`, pois é um contrato do domínio, não um DTO de aplicação.

`IMessagePublisher.cs`:
```csharp
Task PublishAsync<T>(string exchange, string routingKey, T message, CancellationToken cancellationToken)
    where T : notnull;
```

**5. Interface de Port Inbound**

Crie `src/Ingestion.Domain/Ports/Inbound/IProcessMessageUseCase.cs`:
```csharp
Task ExecuteAsync(TDto message, CancellationToken cancellationToken);
```

**Boas práticas obrigatórias:**
- Nenhuma classe do domínio deve ter dependência de pacotes externos (sem `using` de RabbitMQ, Redis, Dapper, System.Text.Json de forma acoplada, etc.)
- Todas as interfaces devem usar `CancellationToken` em métodos assíncronos
- Nomes de namespaces devem seguir o padrão `Ingestion.Domain.{SubPasta}`
- Entidades devem ser imutáveis após construção (sem setters públicos)
- Não usar `abstract class` para entidades; preferir composição
