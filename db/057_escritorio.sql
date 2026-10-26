-- =============================================================================
-- 057 — Módulo Escritório: gestão do escritório contábil sobre os cadastros comuns (empresas, sócios, contadores,
-- clientes e fornecedores ficam em core.* e valem em todos os módulos).
--   * Tarefas e prazos por empresa, com responsável, prioridade e recorrência (mensal/anual).
--   * Honorários: contrato por empresa e cobranças mensais (uma por competência), com recebimento.
--   * Protocolo de documentos entregues ao cliente ou recebidos dele.
-- =============================================================================
CREATE SCHEMA escritorio;

CREATE TABLE escritorio.tarefa (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id),
    titulo          VARCHAR(150) NOT NULL,
    descricao       TEXT,
    vencimento      DATE         NOT NULL,
    responsavel_id  BIGINT       REFERENCES core.usuario(id),
    prioridade      SMALLINT     NOT NULL DEFAULT 2,             -- 1 alta, 2 normal, 3 baixa
    situacao        VARCHAR(10)  NOT NULL DEFAULT 'PENDENTE',
    recorrencia     VARCHAR(7)   NOT NULL DEFAULT 'NENHUMA',
    concluida_em    TIMESTAMPTZ,
    concluida_por   BIGINT       REFERENCES core.usuario(id),
    criada_em       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_tarefa_situacao CHECK (situacao IN ('PENDENTE', 'ANDAMENTO', 'CONCLUIDA', 'CANCELADA')),
    CONSTRAINT ck_tarefa_recorrencia CHECK (recorrencia IN ('NENHUMA', 'MENSAL', 'ANUAL')),
    CONSTRAINT ck_tarefa_prioridade CHECK (prioridade BETWEEN 1 AND 3)
);
CREATE INDEX ix_tarefa_vencimento ON escritorio.tarefa (vencimento) WHERE situacao IN ('PENDENTE', 'ANDAMENTO');
CREATE INDEX ix_tarefa_empresa ON escritorio.tarefa (empresa_id);

CREATE TABLE escritorio.honorario_contrato (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL UNIQUE REFERENCES core.empresa(id),
    valor           NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    dia_vencimento  SMALLINT      NOT NULL CHECK (dia_vencimento BETWEEN 1 AND 31),
    inicio          DATE          NOT NULL,
    fim             DATE,
    indice_reajuste VARCHAR(10)   NOT NULL DEFAULT 'NENHUM',
    observacao      VARCHAR(500),
    CONSTRAINT ck_honorario_indice CHECK (indice_reajuste IN ('NENHUM', 'IPCA', 'IGPM', 'INPC', 'SALARIO')),
    CONSTRAINT ck_honorario_fim CHECK (fim IS NULL OR fim >= inicio)
);

CREATE TABLE escritorio.honorario_cobranca (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    competencia     DATE          NOT NULL,
    valor           NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    vencimento      DATE          NOT NULL,
    situacao        VARCHAR(9)    NOT NULL DEFAULT 'ABERTA',
    recebido_em     DATE,
    valor_recebido  NUMERIC(15,2),
    CONSTRAINT uq_honorario_cobranca UNIQUE (empresa_id, competencia),
    CONSTRAINT ck_honorario_cob_dia1 CHECK (competencia = date_trunc('month', competencia)::date),
    CONSTRAINT ck_honorario_cob_situacao CHECK (situacao IN ('ABERTA', 'RECEBIDA', 'CANCELADA')),
    CONSTRAINT ck_honorario_cob_recebida CHECK ((situacao = 'RECEBIDA') = (recebido_em IS NOT NULL AND valor_recebido IS NOT NULL))
);

CREATE TABLE escritorio.protocolo (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id),
    numero          INTEGER      NOT NULL,
    data            DATE         NOT NULL,
    tipo            VARCHAR(11)  NOT NULL,
    documento       VARCHAR(150) NOT NULL,
    descricao       VARCHAR(500),
    pessoa          VARCHAR(150) NOT NULL,                       -- quem recebeu (entrega) ou entregou (recebimento)
    usuario_id      BIGINT       REFERENCES core.usuario(id),
    criado_em       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT uq_protocolo_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_protocolo_tipo CHECK (tipo IN ('ENTREGA', 'RECEBIMENTO'))
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON escritorio.tarefa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON escritorio.honorario_contrato FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON escritorio.honorario_cobranca FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON escritorio.protocolo FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
