-- =============================================================================
-- Vorticis Fiscal — Migração 033: Lucro Presumido e Lucro Real (IRPJ, CSLL, PIS e COFINS)
--   * fiscal.apuracao_lucro: IRPJ e CSLL por trimestre (Presumido e Real), com as colunas principais e o
--     resultado completo em JSONB (memória de cálculo). Dados que a ECF lerá: bases, adicional, deduções,
--     compensações e prejuízos do período (blocos N e demonstrativos do Lucro Presumido).
--   * fiscal.lalur_lancamento: LALUR/LACS Parte A — adições e exclusões (registros M300 do IRPJ e M350 da CSLL);
--     codigo_ecf guarda o código do lançamento na tabela dinâmica da ECF.
--   * fiscal.lalur_parte_b: razão da Parte B — saldo inicial, prejuízo fiscal / base negativa gerados (+) e
--     compensados (−) por tributo (M010/M410 da ECF). Saldo = soma dos valores.
--   * fiscal.apuracao_pis_cofins: PIS e COFINS mensais, regime cumulativo (0,65% e 3%) ou não cumulativo (1,65% e 7,6%).
--   Percentuais de presunção, alíquotas e limites ficam em core.parametro_legal (chave LUCRO_PRESUMIDO_REAL).
-- =============================================================================

CREATE TABLE fiscal.apuracao_lucro (
    id                   BIGSERIAL     PRIMARY KEY,
    empresa_id           BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    regime               VARCHAR(10)   NOT NULL CHECK (regime IN ('PRESUMIDO', 'REAL')),
    ano                  SMALLINT      NOT NULL,
    trimestre            SMALLINT      NOT NULL CHECK (trimestre BETWEEN 1 AND 4),
    periodo_inicio       DATE          NOT NULL,
    periodo_fim          DATE          NOT NULL,
    receita_bruta        NUMERIC(15,2) NOT NULL DEFAULT 0,
    lucro_contabil       NUMERIC(15,2),                           -- Real: resultado contábil antes do IRPJ e da CSLL
    adicoes_irpj         NUMERIC(15,2) NOT NULL DEFAULT 0,
    exclusoes_irpj       NUMERIC(15,2) NOT NULL DEFAULT 0,
    adicoes_csll         NUMERIC(15,2) NOT NULL DEFAULT 0,
    exclusoes_csll       NUMERIC(15,2) NOT NULL DEFAULT 0,
    compensacao_irpj     NUMERIC(15,2) NOT NULL DEFAULT 0,        -- prejuízo fiscal compensado (limite de 30%)
    compensacao_csll     NUMERIC(15,2) NOT NULL DEFAULT 0,        -- base negativa compensada (limite de 30%)
    prejuizo_irpj        NUMERIC(15,2) NOT NULL DEFAULT 0,        -- prejuízo fiscal do período (vai à Parte B)
    prejuizo_csll        NUMERIC(15,2) NOT NULL DEFAULT 0,
    base_irpj            NUMERIC(15,2) NOT NULL DEFAULT 0,        -- base de cálculo após compensação (Real: lucro real)
    base_csll            NUMERIC(15,2) NOT NULL DEFAULT 0,
    irpj_normal          NUMERIC(15,2) NOT NULL DEFAULT 0,
    irpj_adicional       NUMERIC(15,2) NOT NULL DEFAULT 0,
    irpj_deducoes        NUMERIC(15,2) NOT NULL DEFAULT 0,        -- IRRF compensável e incentivos
    irpj_a_pagar         NUMERIC(15,2) NOT NULL DEFAULT 0,
    csll_devida          NUMERIC(15,2) NOT NULL DEFAULT 0,
    csll_deducoes        NUMERIC(15,2) NOT NULL DEFAULT 0,
    csll_a_pagar         NUMERIC(15,2) NOT NULL DEFAULT 0,
    vencimento           DATE          NOT NULL,                  -- cota única: último dia útil do mês seguinte ao trimestre
    resultado            JSONB         NOT NULL,
    lancamento_id        BIGINT        REFERENCES contabil.lancamento (id) ON DELETE SET NULL,   -- provisão contabilizada
    criado_em            TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por           BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    CONSTRAINT uq_apuracao_lucro UNIQUE (empresa_id, regime, ano, trimestre)
);

CREATE TABLE fiscal.lalur_lancamento (
    id            BIGSERIAL     PRIMARY KEY,
    empresa_id    BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    ano           SMALLINT      NOT NULL,
    trimestre     SMALLINT      NOT NULL CHECK (trimestre BETWEEN 1 AND 4),
    tributo       VARCHAR(5)    NOT NULL CHECK (tributo IN ('IRPJ', 'CSLL', 'AMBOS')),   -- AMBOS = M300 e M350
    tipo          VARCHAR(8)    NOT NULL CHECK (tipo IN ('ADICAO', 'EXCLUSAO')),
    descricao     VARCHAR(200)  NOT NULL,
    valor         NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    codigo_ecf    VARCHAR(20),
    conta_id      BIGINT        REFERENCES contabil.plano_contas (id) ON DELETE SET NULL,
    criado_em     TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por    BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL
);
CREATE INDEX ix_lalur_lancamento_periodo ON fiscal.lalur_lancamento (empresa_id, ano, trimestre);

CREATE TABLE fiscal.lalur_parte_b (
    id            BIGSERIAL     PRIMARY KEY,
    empresa_id    BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    tributo       VARCHAR(5)    NOT NULL CHECK (tributo IN ('IRPJ', 'CSLL')),
    tipo          VARCHAR(13)   NOT NULL CHECK (tipo IN ('SALDO_INICIAL', 'PREJUIZO', 'COMPENSACAO')),
    ano           SMALLINT,                                     -- período de origem (NULL no saldo inicial)
    trimestre     SMALLINT      CHECK (trimestre BETWEEN 1 AND 4),
    valor         NUMERIC(15,2) NOT NULL,                       -- + gerado/saldo inicial, − compensado
    apuracao_id   BIGINT        REFERENCES fiscal.apuracao_lucro (id) ON DELETE CASCADE,
    descricao     VARCHAR(200),
    criado_em     TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT ck_parte_b_sinal CHECK ((tipo = 'COMPENSACAO' AND valor < 0) OR (tipo <> 'COMPENSACAO' AND valor > 0))
);
CREATE INDEX ix_lalur_parte_b ON fiscal.lalur_parte_b (empresa_id, tributo);

CREATE TABLE fiscal.apuracao_pis_cofins (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    competencia      DATE          NOT NULL CHECK (extract(day FROM competencia) = 1),
    regime           VARCHAR(14)   NOT NULL CHECK (regime IN ('CUMULATIVO', 'NAO_CUMULATIVO')),
    receita_base     NUMERIC(15,2) NOT NULL DEFAULT 0,
    pis_debito       NUMERIC(15,2) NOT NULL DEFAULT 0,
    cofins_debito    NUMERIC(15,2) NOT NULL DEFAULT 0,
    base_credito     NUMERIC(15,2) NOT NULL DEFAULT 0,
    pis_credito      NUMERIC(15,2) NOT NULL DEFAULT 0,
    cofins_credito   NUMERIC(15,2) NOT NULL DEFAULT 0,
    pis_retido       NUMERIC(15,2) NOT NULL DEFAULT 0,
    cofins_retido    NUMERIC(15,2) NOT NULL DEFAULT 0,
    pis_a_pagar      NUMERIC(15,2) NOT NULL DEFAULT 0,
    cofins_a_pagar   NUMERIC(15,2) NOT NULL DEFAULT 0,
    vencimento       DATE          NOT NULL,
    resultado        JSONB         NOT NULL,
    lancamento_id    BIGINT        REFERENCES contabil.lancamento (id) ON DELETE SET NULL,
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por       BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    CONSTRAINT uq_apuracao_pis_cofins UNIQUE (empresa_id, competencia)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.apuracao_lucro
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.lalur_lancamento
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.lalur_parte_b
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.apuracao_pis_cofins
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
