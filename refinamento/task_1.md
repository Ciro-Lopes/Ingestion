# Task 1 — Setup da Solution e Projetos .NET

## Objetivo

Criar a estrutura inicial da solution `ingestion`, com todos os projetos .NET organizados por camada seguindo arquitetura hexagonal, referências entre projetos configuradas e pacotes NuGet instalados. O ambiente de infraestrutura local também deve estar operacional via Docker Compose.

---

## Principais Entregas

- Solution `ingestion.sln` criada com os projetos:
  - `Ingestion.Domain` (classlib)
  - `Ingestion.Application` (classlib)
  - `Ingestion.Infrastructure` (classlib)
  - `Ingestion.Worker` (worker service)
  - `Ingestion.Domain.Tests` (xunit)
  - `Ingestion.Application.Tests` (xunit)
  - `Ingestion.Infrastructure.Tests` (xunit)
- Referências entre projetos configuradas respeitando a direção da dependência hexagonal
- Pacotes NuGet adicionados a cada projeto
- Estrutura de pastas criada conforme a arquitetura definida
- `docker-compose.yml` com RabbitMQ, Redis e PostgreSQL operacionais e com healthchecks
- `.gitignore` configurado para .NET
- `Readme.md` adequado descrevendo a arquitetura e como rodar.

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior configurando a estrutura base de um microsserviço chamado `ingestion`.

Crie uma solution .NET chamada `ingestion.sln` na raiz do projeto com os seguintes projetos:

**Projetos de produção:**
- `src/Ingestion.Domain` — classlib (.NET 8)
- `src/Ingestion.Application` — classlib (.NET 8)
- `src/Ingestion.Infrastructure` — classlib (.NET 8)
- `src/Ingestion.Worker` — worker service (.NET 8)

**Projetos de teste:**
- `tests/Ingestion.Domain.Tests` — xunit (.NET 8)
- `tests/Ingestion.Application.Tests` — xunit (.NET 8)
- `tests/Ingestion.Infrastructure.Tests` — xunit (.NET 8)

**Referências entre projetos (respeitar sentido hexagonal, nunca invertido):**
- `Ingestion.Application` → `Ingestion.Domain`
- `Ingestion.Infrastructure` → `Ingestion.Application`
- `Ingestion.Worker` → `Ingestion.Infrastructure`
- `Ingestion.Domain.Tests` → `Ingestion.Domain`
- `Ingestion.Application.Tests` → `Ingestion.Application`
- `Ingestion.Infrastructure.Tests` → `Ingestion.Infrastructure`

**Pacotes NuGet por projeto:**

`Ingestion.Infrastructure`:
- `RabbitMQ.Client` (versão estável mais recente compatível com .NET 8)
- `StackExchange.Redis`
- `Dapper`
- `Npgsql`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Logging.Abstractions`

`Ingestion.Application`:
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Options`

`Ingestion.Worker`:
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Configuration.Json`
- `Microsoft.Extensions.Configuration.EnvironmentVariables`

`Ingestion.Domain.Tests`, `Ingestion.Application.Tests`, `Ingestion.Infrastructure.Tests`:
- `xunit`
- `xunit.runner.visualstudio`
- `Moq`
- `FluentAssertions`
- `Microsoft.NET.Test.Sdk`

**Estrutura de pastas a criar dentro de cada projeto (apenas os diretórios, sem arquivos ainda):**

```
src/Ingestion.Domain/
  Entities/
  ValueObjects/
  Ports/
    Inbound/
    Outbound/

src/Ingestion.Application/
  UseCases/
  DTOs/
  Mappers/

src/Ingestion.Infrastructure/
  Messaging/
    Consumers/
    Publishers/
    Configuration/
  Cache/
    Configuration/
  Persistence/
    Repositories/
    Scripts/
    Configuration/

src/Ingestion.Worker/
  Workers/
```

**Docker Compose:**

Crie o arquivo `docker-compose.yml` na raiz com os serviços abaixo. Todos devem ter healthchecks configurados:
- `rabbitmq`: imagem `rabbitmq:3.13-management`, portas 5672 e 15672, usuário/senha `guest`
- `redis`: imagem `redis:7.2-alpine`, porta 6379
- `postgres`: imagem `postgres:16-alpine`, porta 5432, banco `ingestion`, usuário `postgres`, senha `postgres`, volume persistente e mount do diretório `src/Ingestion.Infrastructure/Persistence/Scripts` para `/docker-entrypoint-initdb.d`

Crie também um `docker-compose.override.yml` com variáveis de ambiente para desenvolvimento local.

Crie um `.gitignore` adequado para projetos .NET (incluindo `bin/`, `obj/`, `.env`, `*.user`).

Crie um `Readme.md` adequado para projeto `ingestion` descrevendo sua arquitetura e como rodar.

**Boas práticas obrigatórias:**
- Nenhum projeto de produção deve referenciar um projeto de nível inferior ao dele na hierarquia
- Nenhum projeto de domínio deve referenciar pacotes de infraestrutura
- Os projetos de teste devem depender apenas dos projetos que testam, sem depender do Worker
