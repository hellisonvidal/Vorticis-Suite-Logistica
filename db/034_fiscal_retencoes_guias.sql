-- =============================================================================
-- Vorticis Fiscal — Migração 034: retenções, guias de recolhimento e contas dos tributos
--   * fiscal.retencao: IRRF, CSRF (PIS, COFINS, CSLL), INSS e ISS retidos. SOFRIDA = retida por clientes sobre
--     as notas da empresa (crédito a compensar); EFETUADA = retida pela empresa de seus fornecedores (a recolher).
--     Gerada a partir das notas importadas (origem NOTA) ou lançada à mão (MANUAL).
--   * fiscal.guia: obrigações a pagar por competência e vencimento (DAS, DARF com código de receita, GPS/DCTFWeb
--     informativo, ISS), geradas das apurações e das retenções efetuadas, ou lançadas à mão.
--   * fiscal.conta_tributo: contas contábeis (despesa e passivo) de cada tributo para a contabilização das provisões.
-- =============================================================================

CREATE TABLE fiscal.retencao (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    sentido          VARCHAR(8)    NOT NULL CHECK (sentido IN ('SOFRIDA', 'EFETUADA')),
    tributo          VARCHAR(6)    NOT NULL CHECK (tributo IN ('IRRF', 'PIS', 'COFINS', 'CSLL', 'INSS', 'ISS')),
    competencia      DATE          NOT NULL CHECK (extract(day FROM competencia) = 1),
    documento_id     BIGINT        REFERENCES fiscal.documento (id) ON DELETE CASCADE,
    participante_id  BIGINT        REFERENCES core.participante (id) ON DELETE SET NULL,
    base_calculo     NUMERIC(15,2) NOT NULL DEFAULT 0,
    aliquota         NUMERIC(9,4)  NOT NULL DEFAULT 0,
    valor            NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    codigo_receita   VARCHAR(6),                                -- DARF/GPS (ex.: 1708, 5952, 2631)
    origem           VARCHAR(6)    NOT NULL CHECK (origem IN ('NOTA', 'MANUAL')),
    descricao        VARCHAR(200),
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por       BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL
);
CREATE INDEX ix_retencao_periodo ON fiscal.retencao (empresa_id, competencia, sentido);
CREATE INDEX ix_retencao_documento ON fiscal.retencao (documento_id);

CREATE TABLE fiscal.guia (
    id               BIGSERIAL     PRIMARY KEY,
    empresa_id       BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    tributo          VARCHAR(12)   NOT NULL,                    -- DAS, IRPJ, CSLL, PIS, COFINS, IRRF, CSRF, INSS, ISS...
    descricao        VARCHAR(200)  NOT NULL,
    competencia      DATE          NOT NULL CHECK (extract(day FROM competencia) = 1),
    vencimento       DATE          NOT NULL,
    valor            NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    codigo_receita   VARCHAR(6),                                -- DARF: código de receita; DAS e ISS: nulo
    documento        VARCHAR(10)   NOT NULL DEFAULT 'DARF' CHECK (documento IN ('DAS', 'DARF', 'GPS', 'DCTFWEB', 'ISS')),
    origem           VARCHAR(14)   NOT NULL CHECK (origem IN ('SIMPLES', 'LUCRO', 'PIS_COFINS', 'RETENCAO', 'MANUAL')),
    referencia       VARCHAR(60),                               -- chave da origem (ex.: SIMPLES:2026-03) para regerar sem duplicar
    situacao         VARCHAR(7)    NOT NULL DEFAULT 'ABERTA' CHECK (situacao IN ('ABERTA', 'PAGA')),
    data_pagamento   DATE,
    valor_pago       NUMERIC(15,2),
    observacao       VARCHAR(200),
    criado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    criado_por       BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    CONSTRAINT ck_guia_pagamento CHECK ((situacao = 'PAGA') = (data_pagamento IS NOT NULL))
);
CREATE UNIQUE INDEX uq_guia_referencia ON fiscal.guia (empresa_id, referencia, tributo, codigo_receita) NULLS NOT DISTINCT WHERE referencia IS NOT NULL;
CREATE INDEX ix_guia_vencimento ON fiscal.guia (empresa_id, vencimento);

CREATE TABLE fiscal.conta_tributo (
    empresa_id         BIGINT      NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    tributo            VARCHAR(12) NOT NULL,                    -- DAS, IRPJ, CSLL, PIS, COFINS
    conta_despesa_id   BIGINT      NOT NULL REFERENCES contabil.plano_contas (id) ON DELETE CASCADE,
    conta_passivo_id   BIGINT      NOT NULL REFERENCES contabil.plano_contas (id) ON DELETE CASCADE,
    PRIMARY KEY (empresa_id, tributo),
    CONSTRAINT ck_conta_tributo_contas CHECK (conta_despesa_id <> conta_passivo_id)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.guia
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.retencao
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
