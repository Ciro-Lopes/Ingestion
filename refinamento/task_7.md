# Task 7 — Repositórios PostgreSQL com Dapper

## Objetivo

Implementar o adapter de persistência `TradeRepository` na camada de infraestrutura, concretizando a interface do domínio. A persistência deve ser realizada em lote via upsert com Dapper e Npgsql, com idempotência garantida a nível de banco. O script SQL de criação da tabela também deve ser entregue nesta etapa.

---

## Principais Entregas

- `TradeRepository` implementando `ITradeRepository` com upsert em lote via Dapper
- Scripts SQL para criação da tabela `trades`
- Upsert idempotente com `INSERT ... ON CONFLICT DO UPDATE WHERE`
- Conexão gerenciada de forma eficiente via `NpgsqlConnection`
- Classe `DbConnectionFactory` para abstração da criação de conexões (facilita testes)

---

## Prompt de Execução

Você é um desenvolvedor .NET sênior implementando a camada de persistência do microsserviço `ingestion` usando Dapper e PostgreSQL via Npgsql. Os arquivos ficam em `src/Ingestion.Infrastructure/Persistence/`.

**1. DbConnectionFactory**

Crie `src/Ingestion.Infrastructure/Persistence/DbConnectionFactory.cs`:

Crie uma interface `IDbConnectionFactory` com:
```csharp
IDbConnection CreateConnection();
```

Implemente `NpgsqlConnectionFactory` que:
- Recebe `IOptions<DatabaseSettings>` no construtor
- Cria e retorna uma nova `NpgsqlConnection` com a connection string configurada
- A conexão é aberta pelo chamador (não abre no factory)

**2. Scripts SQL**

Crie `src/Ingestion.Infrastructure/Persistence/Scripts/V1__create_trades_table.sql`:

```sql
CREATE TABLE IF NOT EXISTS trades (
    id              VARCHAR(255) PRIMARY KEY,
    quantity        NUMERIC(18, 8) NOT NULL,
    reference_date  DATE NOT NULL,
    type            VARCHAR(100) NOT NULL,
    status          VARCHAR(100) NOT NULL,
    raw_message     JSONB NOT NULL,
    metadata        JSONB NOT NULL,
    created_at      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_trades_updated_at ON trades (updated_at);
CREATE INDEX IF NOT EXISTS idx_trades_reference_date ON trades (reference_date);
```

> Prefixe o script com `V1__` para compatibilidade futura com Flyway ou Liquibase.

**3. TradeRepository**

Crie `src/Ingestion.Infrastructure/Persistence/Repositories/TradeRepository.cs`:

A classe deve:
- Implementar `ITradeRepository`
- Ser `sealed`
- Receber `IDbConnectionFactory connectionFactory` e `ILogger<TradeRepository> logger` no construtor

No método `UpsertBatchAsync(IEnumerable<Trade> trades, CancellationToken ct)`:
1. Converta as entidades para um DTO anônimo ou record de persistência com os campos mapeados para colunas (`Id` = `compositeId.ToString()`, `Quantity`, `ReferenceDate`, `Type`, `Status`, `RawMessage`, `Metadata`, `CreatedAt`, `UpdatedAt`)
2. Abra uma conexão via `connectionFactory.CreateConnection()`
3. Abra uma transação
4. Execute o upsert via Dapper `ExecuteAsync` com a seguinte query:

```sql
INSERT INTO trades (id, quantity, reference_date, type, status, raw_message, metadata, created_at, updated_at)
VALUES (@Id, @Quantity, @ReferenceDate, @Type, @Status, @RawMessage::jsonb, @Metadata::jsonb, @CreatedAt, @UpdatedAt)
ON CONFLICT (id) DO UPDATE
SET quantity       = EXCLUDED.quantity,
    status         = EXCLUDED.status,
    raw_message    = EXCLUDED.raw_message,
    metadata       = EXCLUDED.metadata,
    updated_at     = EXCLUDED.updated_at
WHERE trades.updated_at < EXCLUDED.updated_at;
```

5. Passe o `IEnumerable` de objetos de parâmetro diretamente ao Dapper (batch nativo via `ExecuteAsync` com lista)
6. Commite a transação
7. Em caso de exceção: rollback + rethrow + log de erro com contagem de itens do batch

**Boas práticas obrigatórias:**
- Nunca usar `DbContext` ou ORM — apenas Dapper com SQL explícito
- Usar transações para garantir atomicidade do batch
- Mapear `DateTime` como UTC explicitamente ao enviar para o Npgsql (`Kind = DateTimeKind.Utc`)
- A query de upsert deve usar `::jsonb` para os campos JSON do PostgreSQL
- `IDbConnectionFactory` permite mock nos testes — não instanciar `NpgsqlConnection` diretamente nos repositories
- A cláusula `WHERE trades.updated_at < EXCLUDED.updated_at` é obrigatória para garantir idempotência de segunda linha
- Namespace: `Ingestion.Infrastructure.Persistence.Repositories`
