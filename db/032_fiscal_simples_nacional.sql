-- =============================================================================
-- Vorticis Fiscal — Migração 032: Simples Nacional (apuração do DAS)
--   * fiscal.base_mensal: receita bruta e folha de pagamento por mês, a base do RBT12 e do Fator R.
--     Meses anteriores ao uso do sistema são digitados (origem MANUAL); a apuração grava a receita do
--     mês (origem APURACAO); o módulo de Folha poderá alimentar a folha (origem FOLHA).
--   * fiscal.simples_apuracao: uma por empresa e competência, com o resultado completo (JSONB: segmentos por
--     anexo/atividade, repartição dos tributos e memória de cálculo) e as colunas principais para consulta.
--   As tabelas do Simples (anexos, faixas, repartição) ficam em core.parametro_legal, chave SIMPLES_NACIONAL
--   (semeada na primeira consulta a partir dos parâmetros embutidos), por vigência.
-- =============================================================================

CREATE TABLE fiscal.base_mensal (
    empresa_id    BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    competencia   DATE          NOT NULL CHECK (extract(day FROM competencia) = 1),
    receita_bruta NUMERIC(15,2) CHECK (receita_bruta IS NULL OR receita_bruta >= 0),
    folha         NUMERIC(15,2) CHECK (folha IS NULL OR folha >= 0),    -- salários, pró-labore e encargos (CPP, FGTS)
    origem_receita VARCHAR(10)  NOT NULL DEFAULT 'MANUAL' CHECK (origem_receita IN ('MANUAL', 'APURACAO')),
    origem_folha   VARCHAR(10)  NOT NULL DEFAULT 'MANUAL' CHECK (origem_folha IN ('MANUAL', 'FOLHA')),
    alterado_em   TIMESTAMPTZ   NOT NULL DEFAULT now(),
    alterado_por  BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    PRIMARY KEY (empresa_id, competencia)
);

CREATE TABLE fiscal.simples_apuracao (
    id             BIGSERIAL     PRIMARY KEY,
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    competencia    DATE          NOT NULL CHECK (extract(day FROM competencia) = 1),
    rbt12          NUMERIC(15,2) NOT NULL,
    folha_12m      NUMERIC(15,2) NOT NULL DEFAULT 0,
    fator_r        NUMERIC(8,4)  NOT NULL DEFAULT 0,
    receita_mes    NUMERIC(15,2) NOT NULL,
    valor_das      NUMERIC(15,2) NOT NULL,
    vencimento     DATE          NOT NULL,
    resultado      JSONB         NOT NULL,               -- ResultadoSimples: segmentos, tributos, avisos, memória
    lancamento_id  BIGINT        REFERENCES contabil.lancamento (id) ON DELETE SET NULL,   -- provisão contabilizada
    criado_em      TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por     BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    CONSTRAINT uq_simples_apuracao UNIQUE (empresa_id, competencia)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.simples_apuracao
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
