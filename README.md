
# ingestion

Microservice responsible for **data ingestion** and notification. It consumes messages from RabbitMQ queues, applies version control via Redis, persists in batch to PostgreSQL, and publishes to the next microservice in the pipeline.

---

## Architecture

The project follows the **Hexagonal Architecture (Ports & Adapters)**, completely isolating the domain and business rules from any infrastructure details.

```
┌─────────────────────────────────────────────┐
│              HOST / WORKER                  │
│  (composition root, DI, BackgroundServices) │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│         ADAPTERS (Infrastructure)           │
│  RabbitMQ Consumer/Publisher                │
│  Redis Cache                                │
│  PostgreSQL (Dapper)                        │
└──────────────┬──────────────────────────────┘
               │ implements
┌──────────────▼──────────────────────────────┐
│     PORTS (Domain Interfaces)               │
│  ICacheService, ITradeRepository,           │
│  IMessagePublisher                          │
└──────────────┬──────────────────────────────┘
               │ uses
┌──────────────▼──────────────────────────────┐
│       APPLICATION (Use Cases)               │
│  ProcessTradeUseCase                        │
└──────────────┬──────────────────────────────┘
               │ uses
┌──────────────▼──────────────────────────────┐
│           DOMAIN                            │
│  Entities · Value Objects · Ports           │
└─────────────────────────────────────────────┘
```

### Project Structure

| Project | Type | Responsibility |
|---|---|---|
| `Ingestion.Domain` | classlib | Entities, Value Objects, Port interfaces |
| `Ingestion.Application` | classlib | Use Cases, DTOs, Mappers, batch logic |
| `Ingestion.Infrastructure` | classlib | Adapters: RabbitMQ, Redis, PostgreSQL/Dapper |
| `Ingestion.Worker` | worker service | Composition root, DI, BackgroundService hosts |
| `Ingestion.Domain.Tests` | xunit | Domain unit tests |
| `Ingestion.Application.Tests` | xunit | Use case and mapper unit tests |
| `Ingestion.Infrastructure.Tests` | xunit | Adapter integration tests |

---

## Flows

| Flow | Consumption Queue | DLQ | Table |
|---|---|---|---|
| `trade` | `ingestion.trade` | `ingestion.trade.dead-letter` | `trades` |

### Processing Flow

```
[RabbitMQ Queue]
      │
      ▼
[BaseConsumer] — deserializes message
      │
      ▼
[ProcessXxxUseCase]
  ├─ Checks version in Redis (UpdatedAt)
  │     └─ if data is outdated → ACK without persistence
  ├─ Maps to domain entity
  ├─ Accumulates in BatchProcessor<T>
  │
  ▼
[Batch flush — by size OR by timer]
  ├─ UpsertBatch in PostgreSQL
  ├─ Updates Redis (versioning)
  └─ Publishes to the next microservice (RabbitMQ)
```

---

## Technologies

- **.NET 8** — Worker Service
- **RabbitMQ** — messaging (consume and publish)
- **Redis** — distributed cache (versioning + dynamic consumption config)
- **PostgreSQL** — persistence
- **Dapper** — database access (no ORM)
- **Docker / Docker Compose** — local environment

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

---

## How to Run

### 1. Start the local infrastructure

```bash
docker-compose up -d rabbitmq redis postgres
```

Wait for the healthchecks to become `healthy`:

```bash
docker-compose ps
```

### 2. Configure environment variables

```bash
cp .env.example .env
# edit the .env as needed for your local environment
```

### 3. Run the Worker locally

```bash
dotnet run --project src/Ingestion.Worker/Ingestion.Worker.csproj
```

Or via Docker Compose (build + run):

```bash
docker-compose up --build ingestion
```

### 4. Run the tests

```bash
dotnet test
```

---

## Local Ports

| Service | Port |
|---|---|
| RabbitMQ AMQP | 5672 |
| RabbitMQ Management UI | 15672 |
| Redis | 6379 |
| PostgreSQL | 5432 |

**RabbitMQ Management:** http://localhost:15672 (user: `guest` / password: `guest`)

---

## Dynamic Configuration via Redis

The microservice periodically queries Redis to adjust consumption behavior **without the need for redeploy**:

| Redis Key | Description |
|---|---|
| `ingestion:config:{queueName}` | JSON with `batchSize`, `parallelConsumers`, `isEnabled` |
| `ingestion:trade:{compositeId}:updated_at` | Versioning control per record |
