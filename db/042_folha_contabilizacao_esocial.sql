-- =============================================================================
-- Vorticis Folha de Pagamento — Migração 042: provisões, contabilização e eSocial
-- =============================================================================

-- Provisão mensal de férias e 13º salário por funcionário, com encargos.
CREATE TABLE folha.provisao (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    funcionario_id      BIGINT       NOT NULL REFERENCES folha.funcionario(id),
    competencia         DATE         NOT NULL,
    centro_custo_id     BIGINT       REFERENCES contabil.centro_custo(id) ON DELETE SET NULL,
    base                NUMERIC(18,2) NOT NULL,        -- remuneração-base (salário + adicionais + média de variáveis)
    ferias              NUMERIC(18,2) NOT NULL,        -- 1/12 da base + 1/3 constitucional
    decimo              NUMERIC(18,2) NOT NULL,        -- 1/12 da base
    enc_ferias_inss     NUMERIC(18,2) NOT NULL DEFAULT 0,   -- INSS patronal + RAT + terceiros sobre a provisão de férias
    enc_ferias_fgts     NUMERIC(18,2) NOT NULL DEFAULT 0,
    enc_decimo_inss     NUMERIC(18,2) NOT NULL DEFAULT 0,
    enc_decimo_fgts     NUMERIC(18,2) NOT NULL DEFAULT 0,
    calculado_em        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT uq_folha_provisao UNIQUE (empresa_id, funcionario_id, competencia)
);

-- Regras de contabilização: chave = código da rubrica ou código especial (ENC_*, PROV_*). As contas são códigos do plano.
CREATE TABLE folha.regra_contabil (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    chave               VARCHAR(30)  NOT NULL,
    descricao           VARCHAR(100) NOT NULL,
    conta_debito        VARCHAR(40)  NOT NULL,
    conta_credito       VARCHAR(40)  NOT NULL,
    usa_centro_custo    BOOLEAN      NOT NULL DEFAULT FALSE,   -- abre a partida por centro de custo do funcionário
    lado_centro_custo   CHAR(1)      NOT NULL DEFAULT 'D',     -- D = partida de débito, C = partida de crédito (o lado da despesa)
    CONSTRAINT uq_folha_regra UNIQUE (empresa_id, chave),
    CONSTRAINT ck_folha_regra_lado CHECK (lado_centro_custo IN ('D', 'C'))
);

-- Contabilizações geradas (um lançamento por competência e tipo); desfazer estorna o lançamento.
CREATE TABLE folha.contabilizacao (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    competencia         DATE         NOT NULL,
    tipo                VARCHAR(10)  NOT NULL,           -- FOLHA | PROVISAO
    lancamento_id       BIGINT       NOT NULL,           -- contabil.lancamento (vínculo lógico)
    numero_lancamento   BIGINT,
    valor_total         NUMERIC(18,2) NOT NULL,
    criada_em           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    criada_por          BIGINT       REFERENCES core.usuario(id),
    desfeita_em         TIMESTAMPTZ,
    desfeita_por        BIGINT       REFERENCES core.usuario(id),
    estorno_id          BIGINT,
    CONSTRAINT ck_folha_contab_tipo CHECK (tipo IN ('FOLHA', 'PROVISAO'))
);
CREATE UNIQUE INDEX uq_folha_contab_ativa ON folha.contabilizacao (empresa_id, competencia, tipo) WHERE desfeita_em IS NULL;
CREATE TRIGGER trg_auditoria_contab AFTER INSERT OR UPDATE OR DELETE ON folha.contabilizacao
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

-- Eventos do eSocial gerados (XML sem assinatura e sem transmissão nesta etapa).
CREATE TABLE folha.esocial_evento (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    tipo                VARCHAR(8)   NOT NULL,           -- S-1000, S-2200...
    competencia         DATE,                            -- período de apuração (eventos periódicos)
    referencia          VARCHAR(60)  NOT NULL DEFAULT '', -- chave do objeto (matrícula, código da rubrica...)
    id_evento           CHAR(36)     NOT NULL,           -- atributo Id do evento (ID + tpInsc + nrInsc + data/hora + sequencial)
    xml                 TEXT         NOT NULL,
    status              VARCHAR(10)  NOT NULL DEFAULT 'GERADO',   -- GERADO | EXPORTADO
    gerado_em           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    gerado_por          BIGINT       REFERENCES core.usuario(id),
    exportado_em        TIMESTAMPTZ,
    CONSTRAINT uq_folha_esocial_id UNIQUE (empresa_id, id_evento),
    CONSTRAINT ck_folha_esocial_status CHECK (status IN ('GERADO', 'EXPORTADO'))
);
CREATE INDEX ix_folha_esocial_comp ON folha.esocial_evento (empresa_id, competencia, tipo);
