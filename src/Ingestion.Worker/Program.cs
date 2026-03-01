using Ingestion.Application.DTOs;
using Ingestion.Application.UseCases;
using Ingestion.Domain.Ports.Inbound;
using Ingestion.Domain.Ports.Outbound;
using Ingestion.Infrastructure.Cache;
using Ingestion.Infrastructure.Cache.Configuration;
using Ingestion.Infrastructure.Messaging.Configuration;
using Ingestion.Infrastructure.Messaging.Consumers;
using Ingestion.Infrastructure.Messaging.Publishers;
using Ingestion.Infrastructure.Persistence;
using Ingestion.Infrastructure.Persistence.Configuration;
using Ingestion.Infrastructure.Persistence.Repositories;
using Ingestion.Worker.Workers;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using StackExchange.Redis;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false);
        config.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        // -----------------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------------
        services.Configure<RabbitMqSettings>(ctx.Configuration.GetSection("RabbitMq"));
        services.Configure<RedisSettings>(ctx.Configuration.GetSection("Redis"));
        services.Configure<DatabaseSettings>(ctx.Configuration.GetSection("Database"));
        services.Configure<BatchSettings>(ctx.Configuration.GetSection("Batch"));
        services.Configure<OutboundSettings>(ctx.Configuration.GetSection("Outbound"));

        // -----------------------------------------------------------------------
        // RabbitMQ — one connection per application (Singleton)
        // RabbitMQ.Client v7: CreateConnectionAsync — resolved synchronously at
        // startup where await is not available in the DI factory lambda.
        // -----------------------------------------------------------------------
        services.AddSingleton<IConnection>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RabbitMqSettings>>().Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.Username,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost
            };
            // CreateConnectionAsync is used synchronously here because DI factories
            // cannot be async. This is a one-time startup operation.
            return factory.CreateConnectionAsync("ingestion-worker").GetAwaiter().GetResult();
        });

        // -----------------------------------------------------------------------
        // Redis — one multiplexer per application (Singleton)
        // -----------------------------------------------------------------------
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RedisSettings>>().Value;
            return ConnectionMultiplexer.Connect(settings.ConnectionString);
        });

        // -----------------------------------------------------------------------
        // Infrastructure — Adapters
        // Repositories are Singleton because they open a new connection per call
        // via IDbConnectionFactory; no scoped-lifetime concern.
        // -----------------------------------------------------------------------
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<ITradeRepository, TradeRepository>();

        // -----------------------------------------------------------------------
        // Queue Config Store — in-memory registry updated by the polling worker
        // -----------------------------------------------------------------------
        services.AddSingleton<QueueConsumerConfigStore>();

        // -----------------------------------------------------------------------
        // Application — Use Cases (Singleton; BatchProcessor is created internally)
        // -----------------------------------------------------------------------
        services.AddSingleton<IProcessMessageUseCase<TradeMessageDto>, ProcessTradeUseCase>();

        // -----------------------------------------------------------------------
        // Batch loop starters — each use case owns an internal BatchProcessor whose
        // loop must run as a hosted service alongside the host.
        // -----------------------------------------------------------------------
        services.AddHostedService(sp =>
            new BatchLoopHostedService(
                "TradeBatchProcessor",
                ct => ((ProcessTradeUseCase)sp.GetRequiredService<IProcessMessageUseCase<TradeMessageDto>>()).StartAsync(ct),
                sp.GetRequiredService<ILoggerFactory>()));

        // -----------------------------------------------------------------------
        // Infrastructure — Consumers (Singleton, one channel each)
        // -----------------------------------------------------------------------
        services.AddSingleton<TradeConsumer>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RabbitMqSettings>>().Value;
            var queueDef = settings.Queues.First(q => q.FlowName == "trade");
            return new TradeConsumer(
                sp.GetRequiredService<IConnection>(),
                sp.GetRequiredService<QueueConsumerConfigStore>(),
                sp.GetRequiredService<ILogger<TradeConsumer>>(),
                queueDef,
                settings.Exchanges,
                sp.GetRequiredService<IProcessMessageUseCase<TradeMessageDto>>());
        });

        // -----------------------------------------------------------------------
        // Hosted Services
        // -----------------------------------------------------------------------
        services.AddHostedService<TradeWorker>();
        services.AddHostedService<QueueConfigPollingWorker>();
    })
    .Build();

await host.RunAsync();

// ---------------------------------------------------------------------------
// Internal helper: starts and supervises a batch processor loop.
// ---------------------------------------------------------------------------
internal sealed class BatchLoopHostedService : BackgroundService
{
    private readonly string _name;
    private readonly Func<CancellationToken, Task> _loopFactory;
    private readonly ILogger _logger;

    public BatchLoopHostedService(string name, Func<CancellationToken, Task> loopFactory, ILoggerFactory loggerFactory)
    {
        _name = name;
        _loopFactory = loopFactory;
        _logger = loggerFactory.CreateLogger(name);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Name} starting", _name);
        try
        {
            await _loopFactory(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown.
        }
        _logger.LogInformation("{Name} stopped", _name);
    }
}
