-- =============================================================================
-- Vorticis Folha — Migração 050: configuração da folha por empresa (Cadastros › Parâmetros)
--   Dia de pagamento dos salários e padrões do eSocial (ambiente e transmissor) que a tela de transmissão
--   já abre selecionados. RAT, FAP e terceiros continuam em folha.parametro_empresa.
-- =============================================================================

CREATE TABLE folha.configuracao_empresa (
    empresa_id          BIGINT       PRIMARY KEY REFERENCES core.empresa (id) ON DELETE CASCADE,
    dia_pagamento       SMALLINT     NOT NULL DEFAULT 5 CHECK (dia_pagamento BETWEEN 1 AND 31),
    esocial_ambiente    SMALLINT     NOT NULL DEFAULT 2 CHECK (esocial_ambiente IN (1, 2)),                       -- 1 produção | 2 produção restrita
    esocial_transmissor VARCHAR(12)  NOT NULL DEFAULT 'EMPREGADOR' CHECK (esocial_transmissor IN ('EMPREGADOR', 'PROCURADOR', 'CONTADOR')),
    atualizado_em       TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.configuracao_empresa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
