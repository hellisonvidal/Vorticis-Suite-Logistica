-- =============================================================================
-- Vorticis Folha de Pagamento — Migração 041: movimento da folha
--   * Competências (aberta/fechada), eventos variáveis, cálculos com memória por rubrica.
--   * Férias, rescisões, afastamentos e alterações contratuais (base dos eventos S-2206/S-2230/S-2299).
-- =============================================================================

CREATE TABLE folha.competencia (
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    competencia     DATE         NOT NULL,                    -- sempre o dia 1 do mês
    status          VARCHAR(10)  NOT NULL DEFAULT 'ABERTA',   -- ABERTA | FECHADA
    fechada_em      TIMESTAMPTZ,
    fechada_por     BIGINT       REFERENCES core.usuario(id),
    reaberta_em     TIMESTAMPTZ,
    reaberta_por    BIGINT       REFERENCES core.usuario(id),
    PRIMARY KEY (empresa_id, competencia),
    CONSTRAINT ck_folha_comp_dia1 CHECK (competencia = date_trunc('month', competencia)::date),
    CONSTRAINT ck_folha_comp_status CHECK (status IN ('ABERTA', 'FECHADA'))
);
CREATE TRIGGER trg_auditoria_competencia AFTER INSERT OR UPDATE OR DELETE ON folha.competencia
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

-- Eventos variáveis lançados para o mês (horas extras, faltas, comissões, adiantamentos...).
CREATE TABLE folha.evento_variavel (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id  BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    competencia     DATE         NOT NULL,
    rubrica_id      BIGINT       NOT NULL REFERENCES folha.rubrica(id),
    referencia      NUMERIC(12,4) NOT NULL DEFAULT 0,         -- horas, dias ou quantidade
    valor           NUMERIC(18,2),                            -- valor informado (comissão, adiantamento...); nulo = calculado
    observacao      VARCHAR(200),
    criado_em       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_folha_evento_ref CHECK (referencia >= 0 AND (valor IS NULL OR valor >= 0))
);
CREATE INDEX ix_folha_evento_comp ON folha.evento_variavel (empresa_id, competencia);

CREATE TABLE folha.ferias (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id      BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    aquisitivo_inicio   DATE         NOT NULL,
    aquisitivo_fim      DATE         NOT NULL,
    gozo_inicio         DATE         NOT NULL,
    dias_gozo           SMALLINT     NOT NULL,
    abono_dias          SMALLINT     NOT NULL DEFAULT 0,
    faltas_injustificadas SMALLINT   NOT NULL DEFAULT 0,
    data_pagamento      DATE         NOT NULL,
    status              VARCHAR(10)  NOT NULL DEFAULT 'CALCULADA',   -- CALCULADA | CANCELADA
    CONSTRAINT ck_folha_ferias_dias CHECK (dias_gozo BETWEEN 5 AND 30 AND abono_dias BETWEEN 0 AND 10),
    CONSTRAINT ck_folha_ferias_status CHECK (status IN ('CALCULADA', 'CANCELADA'))
);
CREATE INDEX ix_folha_ferias_func ON folha.ferias (empresa_id, funcionario_id);

CREATE TABLE folha.rescisao (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id      BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    motivo              VARCHAR(20)  NOT NULL,   -- SEM_JUSTA_CAUSA | PEDIDO_DEMISSAO | JUSTA_CAUSA | ACORDO_484A | TERMINO_CONTRATO
    data_desligamento   DATE         NOT NULL,
    aviso_tipo          VARCHAR(12)  NOT NULL DEFAULT 'INDENIZADO',   -- INDENIZADO | TRABALHADO | NAO_CUMPRIDO | DISPENSADO
    dias_aviso          SMALLINT     NOT NULL DEFAULT 0,
    saldo_fgts          NUMERIC(18,2) NOT NULL DEFAULT 0,             -- saldo da conta vinculada (base da multa)
    data_pagamento      DATE         NOT NULL,
    status              VARCHAR(10)  NOT NULL DEFAULT 'CALCULADA',    -- CALCULADA | CANCELADA
    CONSTRAINT ck_folha_resc_motivo CHECK (motivo IN ('SEM_JUSTA_CAUSA', 'PEDIDO_DEMISSAO', 'JUSTA_CAUSA', 'ACORDO_484A', 'TERMINO_CONTRATO')),
    CONSTRAINT ck_folha_resc_status CHECK (status IN ('CALCULADA', 'CANCELADA'))
);
CREATE INDEX ix_folha_resc_func ON folha.rescisao (empresa_id, funcionario_id);

-- Resultado de um cálculo (um funcionário, uma competência, um tipo de folha) com totais e encargos patronais.
CREATE TABLE folha.calculo (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id      BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    competencia         DATE         NOT NULL,
    tipo                VARCHAR(10)  NOT NULL,       -- MENSAL | FERIAS | DECIMO_1 | DECIMO_2 | RESCISAO
    origem_id           BIGINT,                      -- folha.ferias.id ou folha.rescisao.id
    data_pagamento      DATE,
    centro_custo_id     BIGINT       REFERENCES contabil.centro_custo(id) ON DELETE SET NULL,
    total_proventos     NUMERIC(18,2) NOT NULL DEFAULT 0,
    total_descontos     NUMERIC(18,2) NOT NULL DEFAULT 0,
    liquido             NUMERIC(18,2) NOT NULL DEFAULT 0,
    base_inss           NUMERIC(18,2) NOT NULL DEFAULT 0,
    base_irrf           NUMERIC(18,2) NOT NULL DEFAULT 0,
    base_fgts           NUMERIC(18,2) NOT NULL DEFAULT 0,
    inss                NUMERIC(18,2) NOT NULL DEFAULT 0,
    irrf                NUMERIC(18,2) NOT NULL DEFAULT 0,
    fgts                NUMERIC(18,2) NOT NULL DEFAULT 0,
    salario_familia     NUMERIC(18,2) NOT NULL DEFAULT 0,
    enc_inss_patronal   NUMERIC(18,2) NOT NULL DEFAULT 0,
    enc_rat             NUMERIC(18,2) NOT NULL DEFAULT 0,
    enc_terceiros       NUMERIC(18,2) NOT NULL DEFAULT 0,
    calculado_em        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    calculado_por       BIGINT       REFERENCES core.usuario(id),
    CONSTRAINT ck_folha_calc_tipo CHECK (tipo IN ('MENSAL', 'FERIAS', 'DECIMO_1', 'DECIMO_2', 'RESCISAO'))
);
CREATE UNIQUE INDEX uq_folha_calculo ON folha.calculo (empresa_id, funcionario_id, competencia, tipo, COALESCE(origem_id, 0));
CREATE INDEX ix_folha_calculo_comp ON folha.calculo (empresa_id, competencia);

CREATE TABLE folha.calculo_item (
    id                  BIGSERIAL PRIMARY KEY,
    calculo_id          BIGINT       NOT NULL REFERENCES folha.calculo(id) ON DELETE CASCADE,
    sequencia           SMALLINT     NOT NULL,
    rubrica_codigo      VARCHAR(10)  NOT NULL,
    descricao           VARCHAR(100) NOT NULL,
    tipo                CHAR(1)      NOT NULL,        -- P | D | I
    referencia          NUMERIC(12,4) NOT NULL DEFAULT 0,
    valor               NUMERIC(18,2) NOT NULL,
    memoria             VARCHAR(500) NOT NULL DEFAULT '',   -- memória de cálculo (fórmula e valores usados)
    CONSTRAINT uq_folha_item UNIQUE (calculo_id, sequencia)
);

CREATE TABLE folha.afastamento (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id  BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    data_inicio     DATE         NOT NULL,
    data_fim        DATE,
    codigo_motivo   CHAR(2)      NOT NULL,            -- eSocial Tabela 18
    observacao      VARCHAR(200),
    CONSTRAINT ck_folha_afast_datas CHECK (data_fim IS NULL OR data_fim >= data_inicio)
);

CREATE TABLE folha.alteracao_contratual (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id      BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    data_alteracao      DATE         NOT NULL,
    descricao           VARCHAR(200) NOT NULL,
    salario_anterior    NUMERIC(18,2),
    salario_novo        NUMERIC(18,2),
    cargo_anterior      VARCHAR(100),
    cargo_novo          VARCHAR(100)
);
