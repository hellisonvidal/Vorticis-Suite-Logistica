-- =============================================================================
-- 077 — Layouts de importação de planilha dos cadastros: o de-para (campo do cadastro → nome da coluna da planilha)
--   guardado por empresa e por cadastro, para a próxima importação já vir pronta.
-- =============================================================================
CREATE TABLE core.layout_planilha (
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    cadastro       VARCHAR(40)   NOT NULL,
    mapeamento     JSONB         NOT NULL,
    atualizado_em  TIMESTAMPTZ   NOT NULL DEFAULT now(),
    PRIMARY KEY (empresa_id, cadastro)
);
