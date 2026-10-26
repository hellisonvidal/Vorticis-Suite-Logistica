-- =============================================================================
-- 075 — Naturezas das notas de entrada: o CFOP de saída que vem no XML do emitente (completo ou só o início, ex.: "51")
--   vira o CFOP de entrada da escrituração (ex.: 5102 → 1102). A regra mais específica (o CFOP mais longo) vence.
-- =============================================================================
CREATE TABLE fiscal.natureza_entrada (
    id             BIGSERIAL     PRIMARY KEY,
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo         INTEGER       NOT NULL CHECK (codigo > 0),
    descricao      VARCHAR(100)  NOT NULL,
    cfop_saida     VARCHAR(4)    NOT NULL CHECK (cfop_saida ~ '^[567][0-9]{0,3}$'),
    cfop_entrada   CHAR(4)       NOT NULL CHECK (cfop_entrada ~ '^[123][0-9]{3}$'),
    ativa          BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_natureza_entrada_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT uq_natureza_entrada_cfop UNIQUE (empresa_id, cfop_saida)
);
