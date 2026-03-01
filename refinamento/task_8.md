# Task 8 — RabbitMQ Publisher

## Objetivo

Implementar o adapter `RabbitMqPublisher` na camada de infraestrutura, que concretiza a interface `IMessagePublisher` do domínio. O publisher deve serializar a mensagem como JSON, publicar em um exchange específico com um routing key e garantir tratamento de erros com logging adequado.

---

## Principais Entregas

- `RabbitMqPublisher` implementando `IMessagePublisher`
- Serialização de mensagens com `System.Text.Json`
- Publicação via `IChannel` (RabbitMQ.Client v7+) ou `IModel` (v6) em exchange com routing key
- Properties básicas de mensagem configuradas (content type, delivery mode persistente)
- Tratamento de erros com logging e rethrow controlado
- Design que permita mock de `IChannel`/`IModel` em testes

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando o publisher RabbitMQ do microsserviço `ingestion`. O arquivo fica em `src/Ingestion.Infrastructure/Messaging/Publishers/RabbitMqPublisher.cs`. Utilize a versão do pacote `RabbitMQ.Client` compatível com .NET 8 (verifique se é v6.x ou v7.x e use a API correta).

**Implementação de `RabbitMqPublisher`:**

A classe deve:
- Implementar `IMessagePublisher` do namespace `Ingestion.Domain.Ports.Outbound`
- Ser `sealed`
- Receber via construtor:
  - `IConnection connection` — singleton de conexão RabbitMQ
  - `ILogger<RabbitMqPublisher> logger`

**Método `PublishAsync<T>`:**

```csharp
public async Task PublishAsync<T>(
    string exchange,
    string routingKey,
    T message,
    CancellationToken cancellationToken) where T : notnull
```

Implementação:
1. Serialize `message` para JSON usando `System.Text.Json.JsonSerializer.Serialize(message)` com opções de camelCase
2. Converta para `byte[]` usando `Encoding.UTF8`
3. Crie (ou reutilize) um canal de publicação. Use um canal por instância de publisher armazenado como campo privado, criado no construtor ou lazy
4. Configure as properties básicas da mensagem:
   - `ContentType = "application/json"`
   - `DeliveryMode = 2` (persistente)
5. Publique via `BasicPublish(exchange, routingKey, properties, body)` (ou equivalente assíncrono se disponível na versão do cliente)
6. Logue a publicação em nível `Debug` com exchange, routingKey e tipo de mensagem
7. Em caso de exceção: logue como `Error` e faça rethrow para que o Use Case possa tratar

**Configuração de JsonSerializerOptions:**

Crie as opções como campo `static readonly`:
```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};
```

**Implementação de IDisposable:**

Se o publisher mantiver um canal interno, implemente `IDisposable` para fechar o canal adequadamente. Registre-o como `Singleton` no DI para evitar múltiplas criações/destruições.

**Boas práticas obrigatórias:**
- Nunca criar uma nova `IConnection` dentro do publisher — sempre injetar a conexão singleton
- O canal de publicação deve ser thread-safe ou um novo canal deve ser criado por publicação caso a versão do cliente não suporte reuso thread-safe
- Nunca bloquear com `.GetAwaiter().GetResult()` — usar `async/await` ou a API síncrona do RabbitMQ corretamente conforme a versão do client
- Namespace: `Ingestion.Infrastructure.Messaging.Publishers`
