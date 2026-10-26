-- =============================================================================
-- Perseus Contábil — Migração 011: importação de lançamentos
--   * Layouts de planilha definidos pelo usuário (colunas → campos do lançamento),
--     compartilhados entre as empresas do escritório.
--   * Controle de duplicidade: cada transação importada (ex.: FITID do extrato OFX)
--     guarda o lançamento gerado; reimportar o mesmo arquivo não duplica.
-- =============================================================================

CREATE TABLE contabil.layout_importacao (
    id            BIGSERIAL    PRIMARY KEY,
    nome          VARCHAR(80)  NOT NULL,
    configuracao  JSONB        NOT NULL,
    criado_em     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    alterado_em   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uq_layout_importacao_nome ON contabil.layout_importacao (lower(nome));

CREATE TABLE contabil.lancamento_importado (
    empresa_id     BIGINT       NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    chave          VARCHAR(200) NOT NULL,
    lancamento_id  BIGINT       NOT NULL REFERENCES contabil.lancamento (id) ON DELETE CASCADE,
    importado_em   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    PRIMARY KEY (empresa_id, chave)
);
CREATE INDEX ix_lancamento_importado_lcto ON contabil.lancamento_importado (lancamento_id);
