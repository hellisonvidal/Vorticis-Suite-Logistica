-- =============================================================================
-- Vorticis Suite Logística — NF-e transportadas pelo CT-e (anexadas pelo XML)
--   Cada nota guarda o que o transporte usa: chave, número, emitente (remetente) e destinatário com os municípios, valor, pesos,
--   volumes e o produto principal; o CT-e soma as notas (painel de quantidades e valores) e guarda as chaves em tms.cte.chaves_nfe.
-- =============================================================================

CREATE TABLE tms.cte_nfe (
    id                   BIGSERIAL     PRIMARY KEY,
    cte_id               BIGINT        NOT NULL REFERENCES tms.cte(id) ON DELETE CASCADE,
    chave                CHAR(44)      NOT NULL CHECK (chave ~ '^[0-9]{44}$'),
    numero               INTEGER       NOT NULL DEFAULT 0,
    serie                SMALLINT      NOT NULL DEFAULT 0,
    emissao              DATE          NOT NULL,
    emitente_documento   VARCHAR(14),
    emitente_nome        VARCHAR(150)  NOT NULL,
    origem_uf            CHAR(2),
    origem_ibge          CHAR(7),
    origem_municipio     VARCHAR(100),
    destinatario_documento VARCHAR(14),
    destinatario_nome    VARCHAR(150)  NOT NULL,
    destino_uf           CHAR(2),
    destino_ibge         CHAR(7),
    destino_municipio    VARCHAR(100),
    valor                NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor >= 0),
    peso_bruto           NUMERIC(12,3) NOT NULL DEFAULT 0 CHECK (peso_bruto >= 0),
    peso_liquido         NUMERIC(12,3) NOT NULL DEFAULT 0 CHECK (peso_liquido >= 0),
    volumes              INTEGER       NOT NULL DEFAULT 0 CHECK (volumes >= 0),
    produto              VARCHAR(120),
    CONSTRAINT uq_cte_nfe UNIQUE (cte_id, chave)
);
CREATE INDEX ix_cte_nfe_chave ON tms.cte_nfe (chave);
