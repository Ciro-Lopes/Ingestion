# Task 3 — DTOs e Mappers da Camada de Aplicação

## Objetivo

Implementar todos os Data Transfer Objects (DTOs) e Mappers da camada de aplicação, que fazem a ponte entre as mensagens recebidas da fila, as entidades de domínio e as mensagens publicadas para o próximo microsserviço.

---

## Principais Entregas

- `TradeMessageDto` — representa a mensagem recebida da fila
- `TradeOutboundDto` — representa a mensagem publicada para o próximo microsserviço
- `QueueConsumerConfigDto` — contrato de configuração dinâmica de consumo lida do Redis
- `TradeMapper` — conversão de DTO de entrada para entidade de domínio
- Todos os tipos como `record` imutáveis; mapper como classe estática ou com interface para testabilidade

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando os DTOs e Mappers da camada de aplicação do microsserviço `ingestion`. O projeto envolvido é `Ingestion.Application`, que referencia `Ingestion.Domain`.

**1. DTOs de entrada (mensagens recebidas da fila)**

Crie `src/Ingestion.Application/DTOs/TradeMessageDto.cs`:

```csharp
public record TradeMessageDto(
    string Id,
    decimal Quantity,
    DateOnly ReferenceDate,
    string Type,
    string Status,
    DateTime UpdatedAt,
    string RawPayload,        // JSON completo da mensagem como string
    string MetadataPayload    // JSON dos headers/metadados como string
);
```

**2. DTOs de saída (publicados para o próximo microsserviço)**

Crie `src/Ingestion.Application/DTOs/TradeOutboundDto.cs`:
```csharp
public record TradeOutboundDto(
    string CompositeId,
    DateTime UpdatedAt
);
```

**3. DTO de configuração de consumo**

Crie `src/Ingestion.Application/DTOs/QueueConsumerConfigDto.cs`:
```csharp
public record QueueConsumerConfigDto(
    string QueueName,
    int BatchSize,
    int ParallelConsumers,
    bool IsEnabled
);
```

Atualize a interface `ICacheService` no projeto `Ingestion.Domain` para usar `QueueConsumerConfigDto` no método `GetQueueConfigAsync`, garantindo que o projeto Domain referencie Application — **ou**, se isso criar dependência circular, mova o `QueueConsumerConfigDto` para `Ingestion.Domain/Ports/Contracts/` para que Domain e Application possam usá-lo sem ciclo. Adote a segunda abordagem.

**4. Mappers**

Crie `src/Ingestion.Application/Mappers/TradeMapper.cs`:

Implemente uma classe `TradeMapper` com um método estático:
```csharp
public static Trade ToEntity(TradeMessageDto dto)
```

Este método deve:
- Construir o `CompositeId` usando `dto.Id`, `dto.ReferenceDate` e `dto.Type`
- Construir a entidade `Trade` com todos os campos mapeados
- Definir `CreatedAt` e `UpdatedAt` como `DateTime.UtcNow` apenas quando for uma criação nova; o `UpdatedAt` deve vir do `dto.UpdatedAt` convertido para UTC

**Boas práticas obrigatórias:**
- Todos os DTOs devem ser `record` imutáveis (sem setters)
- Mappers devem ser testáveis de forma isolada — não devem ter dependências de infraestrutura
- Não usar bibliotecas de automapeamento (sem AutoMapper); o mapeamento deve ser explícito
- Namespaces devem seguir o padrão `Ingestion.Application.{SubPasta}`
- Nenhum DTO deve expor tipos de domínio diretamente para camadas externas (Infrastructure, Worker)
