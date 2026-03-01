# Task 10 — Worker Host, Injeção de Dependências e Dockerfile

## Objetivo

Montar o ponto de composição (composition root) do microsserviço: configurar o `Program.cs` com o registro completo de todas as dependências via DI, implementar os `BackgroundService` Workers para cada fluxo e criar o `Dockerfile` para containerização da aplicação.

---

## Principais Entregas

- `Program.cs` com todo o container de DI configurado, fazendo o bind entre ports e adapters
- `TradeWorker` como `BackgroundService` que inicia o consumer
- Registro do `QueueConfigPollingWorker` como hosted service
- `Dockerfile` multi-stage para build e publicação do Worker
- Validação de configuração no startup (fail-fast se configurações obrigatórias estiverem ausentes)
- `docker-compose.yml` atualizado com o serviço `ingestion` incluído

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior finalizando a composição do microsserviço `ingestion`. O projeto principal é `Ingestion.Worker`.

**1. TradeWorker**

Crie `src/Ingestion.Worker/Workers/TradeWorker.cs`:

```csharp
public sealed class TradeWorker : BackgroundService
```

- Recebe no construtor:
  - `TradeConsumer consumer`
  - `ILogger<TradeWorker> logger`
- Em `ExecuteAsync(CancellationToken stoppingToken)`:
  - Loga o início do worker
  - Chama `await consumer.StartConsumingAsync(stoppingToken)`
  - Loga parada ao término

**2. Program.cs — Composition Root**

Crie `src/Ingestion.Worker/Program.cs` com o seguinte registro completo:

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false);
        config.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        // Settings
        services.Configure<RabbitMqSettings>(ctx.Configuration.GetSection("RabbitMq"));
        services.Configure<RedisSettings>(ctx.Configuration.GetSection("Redis"));
        services.Configure<DatabaseSettings>(ctx.Configuration.GetSection("Database"));
        services.Configure<BatchSettings>(ctx.Configuration.GetSection("Batch"));

        // RabbitMQ Connection (Singleton)
        services.AddSingleton<IConnection>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RabbitMqSettings>>().Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.Username,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                DispatchConsumersAsync = true
            };
            return factory.CreateConnection("ingestion-worker");
        });

        // Redis Connection (Singleton)
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RedisSettings>>().Value;
            return ConnectionMultiplexer.Connect(settings.ConnectionString);
        });

        // Infrastructure — Adapters
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<ITradeRepository, TradeRepository>();

        // Queue Config Store (Singleton em memória)
        services.AddSingleton<QueueConsumerConfigStore>();

        // Application — Use Cases
        services.AddSingleton<IProcessMessageUseCase<TradeMessageDto>, ProcessTradeUseCase>();

        // Infrastructure — Consumers
        services.AddSingleton<TradeConsumer>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RabbitMqSettings>>().Value;
            var queueDef = settings.Queues.First(q => q.FlowName == "trade");
            return new TradeConsumer(
                sp.GetRequiredService<IConnection>(),
                sp.GetRequiredService<QueueConsumerConfigStore>(),
                sp.GetRequiredService<ILogger<TradeConsumer>>(),
                queueDef,
                sp.GetRequiredService<IProcessMessageUseCase<TradeMessageDto>>()
            );
        });
        // Hosted Services
        services.AddHostedService<TradeWorker>();
        services.AddHostedService<QueueConfigPollingWorker>();
    })
    .Build();

await host.RunAsync();
```

> **Nota sobre wiring do BatchProcessor:** Se o delegate de flush precisar de serviços do DI (ex: `ITradeRepository`), o `BatchProcessor<Trade>` deve ser criado dentro do `ProcessTradeUseCase` como composição interna, ou o delegate pode ser passado como parâmetro via factory. Adote a abordagem onde o Use Case recebe o `BatchProcessor` já configurado via DI e o delegate de flush é um método interno do Use Case.

**3. Dockerfile**

Crie `src/Ingestion.Worker/Dockerfile` com build multi-stage:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar solution e projetos para restaurar dependências com cache de layer
COPY ingestion.sln .
COPY src/Ingestion.Domain/Ingestion.Domain.csproj src/Ingestion.Domain/
COPY src/Ingestion.Application/Ingestion.Application.csproj src/Ingestion.Application/
COPY src/Ingestion.Infrastructure/Ingestion.Infrastructure.csproj src/Ingestion.Infrastructure/
COPY src/Ingestion.Worker/Ingestion.Worker.csproj src/Ingestion.Worker/
RUN dotnet restore src/Ingestion.Worker/Ingestion.Worker.csproj

# Copiar e publicar
COPY . .
RUN dotnet publish src/Ingestion.Worker/Ingestion.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Ingestion.Worker.dll"]
```

**4. .env de exemplo**

Crie `.env.example` na raiz com as variáveis de ambiente necessárias para produção (sem valores reais):

```env
RabbitMq__Host=
RabbitMq__Username=
RabbitMq__Password=
Redis__ConnectionString=
Database__ConnectionString=
```

**Boas práticas obrigatórias:**
- A `IConnection` do RabbitMQ deve ser registrada como `Singleton` — uma conexão por aplicação
- O `IConnectionMultiplexer` do Redis deve ser `Singleton`
- Repositories com Dapper que abrem conexões por operação devem ser `Scoped` ou `Transient`
- Nunca registrar o `IConnection` dentro de um `Scoped` service — vazamento de canal
- O Dockerfile deve usar imagens `mcr.microsoft.com/dotnet/runtime` (não SDK) na etapa final
- Sempre copiar apenas os `.csproj` antes do `dotnet restore` para aproveitar o cache de layers do Docker
- Namespace de todos os arquivos do Worker: `Ingestion.Worker.Workers`
