-- =============================================================================
-- Vorticis Suite Logística — Financiamentos e consórcios (módulo Financeiro › Contas)
--   * Contrato: financiamento (Price ou SAC, juros ao mês, entrada e encargos financiados) ou consórcio (carta de crédito,
--     taxa de administração, fundo de reserva, seguro e contemplação), com o credor (banco/administradora) e o bem do imobilizado.
--   * Parcelas com amortização (fundo comum no consórcio), juros (taxa de administração), outros (fundo de reserva), seguro e o pagamento.
--   * Rateio por placa: as despesas (juros, encargos, taxa de administração, seguro) vão ao centro de custo da frota de cada veículo.
--   * Contabilização opcional: contratação, pagamentos, contemplação e reclassificação do longo para o curto prazo; os saldos contabilizados
--     no curto (CP) e no longo prazo (LP) e no ativo do consórcio são a soma dos movimentos de frota.contrato_lancamento.
-- =============================================================================

CREATE TABLE frota.contrato (
    id                  BIGSERIAL     PRIMARY KEY,
    empresa_id          BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    tipo                VARCHAR(13)   NOT NULL,
    numero              VARCHAR(30)   NOT NULL,
    descricao           VARCHAR(150)  NOT NULL,
    credor_id           BIGINT        REFERENCES core.participante(id) ON DELETE SET NULL,
    data_contrato       DATE          NOT NULL,
    valor_bem           NUMERIC(15,2) NOT NULL CHECK (valor_bem > 0),
    entrada             NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (entrada >= 0),
    encargos            NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (encargos >= 0),
    taxa_juros          NUMERIC(8,4)  NOT NULL DEFAULT 0 CHECK (taxa_juros BETWEEN 0 AND 100),
    sistema             VARCHAR(5)    NOT NULL DEFAULT 'PRICE',
    taxa_administracao  NUMERIC(8,4)  NOT NULL DEFAULT 0 CHECK (taxa_administracao BETWEEN 0 AND 100),
    fundo_reserva       NUMERIC(8,4)  NOT NULL DEFAULT 0 CHECK (fundo_reserva BETWEEN 0 AND 100),
    seguro_mensal       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (seguro_mensal >= 0),
    parcelas            SMALLINT      NOT NULL CHECK (parcelas BETWEEN 1 AND 600),
    primeiro_vencimento DATE          NOT NULL,
    data_contemplacao   DATE,
    bem_id              BIGINT        REFERENCES imobilizado.bem(id) ON DELETE SET NULL,
    contabiliza         BOOLEAN       NOT NULL DEFAULT FALSE,
    conta_bem           VARCHAR(30),
    conta_pagamento     VARCHAR(30),
    conta_curto_prazo   VARCHAR(30),
    conta_longo_prazo   VARCHAR(30),
    conta_despesa       VARCHAR(30),
    conta_consorcio     VARCHAR(30),
    situacao            VARCHAR(9)    NOT NULL DEFAULT 'ATIVO',
    observacao          VARCHAR(300),
    CONSTRAINT uq_contrato UNIQUE (empresa_id, tipo, numero),
    CONSTRAINT ck_contrato_tipo CHECK (tipo IN ('FINANCIAMENTO', 'CONSORCIO')),
    CONSTRAINT ck_contrato_sistema CHECK (sistema IN ('PRICE', 'SAC')),
    CONSTRAINT ck_contrato_situacao CHECK (situacao IN ('ATIVO', 'QUITADO', 'CANCELADO')),
    CONSTRAINT ck_contrato_entrada CHECK (entrada < valor_bem),
    CONSTRAINT ck_contrato_contemplacao CHECK (data_contemplacao IS NULL OR tipo = 'CONSORCIO')
);

CREATE TABLE frota.contrato_parcela (
    id              BIGSERIAL     PRIMARY KEY,
    contrato_id     BIGINT        NOT NULL REFERENCES frota.contrato(id) ON DELETE CASCADE,
    numero          SMALLINT      NOT NULL,
    vencimento      DATE          NOT NULL,
    amortizacao     NUMERIC(15,2) NOT NULL CHECK (amortizacao >= 0),
    juros           NUMERIC(15,2) NOT NULL CHECK (juros >= 0),
    outros          NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (outros >= 0),
    seguro          NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (seguro >= 0),
    saldo           NUMERIC(15,2) NOT NULL CHECK (saldo >= 0),
    pago_em         DATE,
    valor_pago      NUMERIC(15,2) CHECK (valor_pago IS NULL OR valor_pago > 0),
    CONSTRAINT uq_contrato_parcela UNIQUE (contrato_id, numero),
    CONSTRAINT ck_contrato_parcela_pago CHECK ((pago_em IS NULL) = (valor_pago IS NULL))
);

CREATE TABLE frota.contrato_rateio (
    contrato_id     BIGINT        NOT NULL REFERENCES frota.contrato(id) ON DELETE CASCADE,
    veiculo_id      BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    percentual      NUMERIC(6,2)  NOT NULL CHECK (percentual > 0 AND percentual <= 100),
    PRIMARY KEY (contrato_id, veiculo_id)
);

-- Movimentos contabilizados: valores positivos aumentam o passivo (CP/LP) ou o ativo do consórcio.
CREATE TABLE frota.contrato_lancamento (
    id              BIGSERIAL     PRIMARY KEY,
    contrato_id     BIGINT        NOT NULL REFERENCES frota.contrato(id) ON DELETE CASCADE,
    tipo            VARCHAR(16)   NOT NULL,
    data            DATE          NOT NULL,
    parcela_id      BIGINT        REFERENCES frota.contrato_parcela(id) ON DELETE CASCADE,
    curto_prazo     NUMERIC(15,2) NOT NULL DEFAULT 0,
    longo_prazo     NUMERIC(15,2) NOT NULL DEFAULT 0,
    ativo_consorcio NUMERIC(15,2) NOT NULL DEFAULT 0,
    lancamento_id   BIGINT        REFERENCES contabil.lancamento(id) ON DELETE SET NULL,
    CONSTRAINT ck_contrato_lancamento_tipo CHECK (tipo IN ('CONTRATACAO', 'PAGAMENTO', 'CONTEMPLACAO', 'RECLASSIFICACAO'))
);
CREATE INDEX ix_contrato_lancamento ON frota.contrato_lancamento (contrato_id);
