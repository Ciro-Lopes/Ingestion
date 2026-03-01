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
