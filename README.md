# ingestion

Microsserviço responsável pela **ingestão de dados** no fluxo de distribuição. Consome mensagens de filas RabbitMQ, aplica controle de versionamento via Redis, persiste em lote no PostgreSQL e publica para o próximo microsserviço do pipeline.

---

## Arquitetura

O projeto segue a **Arquitetura Hexagonal (Ports & Adapters)**, isolando completamente o domínio e as regras de negócio de qualquer detalhe de infraestrutura.

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
               │ implementa
┌──────────────▼──────────────────────────────┐
│     PORTS (Interfaces do Domínio)           │
│  ICacheService, ITradeRepository,           │
│  IMessagePublisher                          │
└──────────────┬──────────────────────────────┘
               │ usa
┌──────────────▼──────────────────────────────┐
│       APPLICATION (Use Cases)               │
│  ProcessTradeUseCase                        │
└──────────────┬──────────────────────────────┘
               │ usa
┌──────────────▼──────────────────────────────┐
│           DOMAIN                            │
│  Entities · Value Objects · Ports           │
└─────────────────────────────────────────────┘
```

### Estrutura de Projetos

| Projeto | Tipo | Responsabilidade |
|---|---|---|
| `Ingestion.Domain` | classlib | Entidades, Value Objects, interfaces de Ports |
| `Ingestion.Application` | classlib | Use Cases, DTOs, Mappers, lógica de batch |
| `Ingestion.Infrastructure` | classlib | Adapters: RabbitMQ, Redis, PostgreSQL/Dapper |
| `Ingestion.Worker` | worker service | Composition root, DI, BackgroundService hosts |
| `Ingestion.Domain.Tests` | xunit | Testes unitários do domínio |
| `Ingestion.Application.Tests` | xunit | Testes unitários dos use cases e mappers |
| `Ingestion.Infrastructure.Tests` | xunit | Testes de integração dos adapters |

---

## Fluxos

| Fluxo | Fila de consumo | DLQ | Tabela |
|---|---|---|---|
| `trade` | `ingestion.trade` | `ingestion.trade.dead-letter` | `trades` |

### Fluxo de Processamento

```
[RabbitMQ Queue]
      │
      ▼
[BaseConsumer] — deserializa mensagem
      │
      ▼
[ProcessXxxUseCase]
  ├─ Verifica versão no Redis (UpdatedAt)
  │     └─ se dado desatualizado → ACK sem persistência
  ├─ Mapeia para entidade de domínio
  ├─ Acumula no BatchProcessor<T>
  │
  ▼
[Batch flush — por tamanho OU por timer]
  ├─ UpsertBatch no PostgreSQL
  ├─ Atualiza Redis (versioning)
  └─ Publica no próximo microsserviço (RabbitMQ)
```

---

## Tecnologias

- **.NET 8** — Worker Service
- **RabbitMQ** — mensageria (consumo e publicação)
- **Redis** — cache distribuído (versioning + config dinâmica de consumo)
- **PostgreSQL** — persistência
- **Dapper** — acesso ao banco (sem ORM)
- **Docker / Docker Compose** — ambiente local

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

---

## Como Rodar

### 1. Subir a infraestrutura local

```bash
docker-compose up -d rabbitmq redis postgres
```

Aguarde os healthchecks ficarem `healthy`:

```bash
docker-compose ps
```

### 2. Configurar variáveis de ambiente

```bash
cp .env.example .env
# edite o .env conforme necessário para ambiente local
```

### 3. Rodar o Worker localmente

```bash
dotnet run --project src/Ingestion.Worker/Ingestion.Worker.csproj
```

Ou via Docker Compose (build + run):

```bash
docker-compose up --build ingestion
```

### 4. Rodar os testes

```bash
dotnet test
```

---

## Portas Locais

| Serviço | Porta |
|---|---|
| RabbitMQ AMQP | 5672 |
| RabbitMQ Management UI | 15672 |
| Redis | 6379 |
| PostgreSQL | 5432 |

**RabbitMQ Management:** http://localhost:15672 (usuário: `guest` / senha: `guest`)

---

## Configuração Dinâmica via Redis

O microsserviço consulta periodicamente o Redis para ajustar o comportamento de consumo **sem necessidade de redeploy**:

| Chave Redis | Descrição |
|---|---|
| `ingestion:config:{queueName}` | JSON com `batchSize`, `parallelConsumers`, `isEnabled` |
| `ingestion:trade:{compositeId}:updated_at` | Controle de versioning por registro |
