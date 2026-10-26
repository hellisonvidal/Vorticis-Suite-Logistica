-- =============================================================================
-- Vorticis Fiscal — Migração 031: contabilização automática das notas fiscais
--   Regras configuráveis por CFOP (prefixo ou código completo), sentido e, opcionalmente, participante,
--   que apontam as contas de débito e crédito do plano da empresa. A nota contabilizada guarda o
--   lançamento (fiscal.documento.lancamento_id, criado na migração 030); desfazer estorna o lançamento.
--   Exclusão da empresa: as regras saem por ON DELETE CASCADE (empresa, plano de contas, participante).
-- =============================================================================

CREATE TABLE fiscal.regra_contabilizacao (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    prioridade       INTEGER       NOT NULL DEFAULT 100,           -- menor = avaliada antes (desempate entre regras igualmente específicas)
    tipo_operacao    CHAR(1)       CHECK (tipo_operacao IN ('E', 'S')),
    cfop             VARCHAR(10),                                  -- código completo, prefixo de 1 a 3 dígitos ou 'NFSE'; NULL = qualquer
    participante_id  BIGINT        REFERENCES core.participante (id) ON DELETE CASCADE,
    conta_debito_id  BIGINT        NOT NULL REFERENCES contabil.plano_contas (id) ON DELETE CASCADE,
    conta_credito_id BIGINT        NOT NULL REFERENCES contabil.plano_contas (id) ON DELETE CASCADE,
    historico        VARCHAR(200)  NOT NULL DEFAULT 'NF {DOCUMENTO} - {PARTICIPANTE}',   -- aceita as variáveis do histórico padrão
    ativo            BOOLEAN       NOT NULL DEFAULT TRUE,
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT ck_regra_contas CHECK (conta_debito_id <> conta_credito_id)
);
CREATE INDEX ix_regra_contabilizacao_empresa ON fiscal.regra_contabilizacao (empresa_id, ativo);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.regra_contabilizacao
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
