-- =============================================================================
-- 059 — Módulo Condomínio: a empresa ativa é o condomínio (CNPJ do condomínio).
--   * Unidades com fração ideal; proprietário e inquilino são participantes (core.participante, cadastro comum).
--   * Taxas (ordinária, fundo de reserva, extra) rateadas pela fração ideal, em partes iguais ou com valor fixo por unidade.
--   * Cobranças mensais por unidade (com a composição), recebimento com multa e juros de atraso.
--   * Despesas do condomínio, áreas comuns com reservas, ocorrências e a configuração de vencimento e encargos.
--   * Limite legal da multa (Código Civil, art. 1.336, § 1º) em core.parametro_legal.
-- =============================================================================
CREATE SCHEMA condominio;

CREATE TABLE condominio.configuracao (
    empresa_id       BIGINT       PRIMARY KEY REFERENCES core.empresa(id),
    dia_vencimento   SMALLINT     NOT NULL DEFAULT 10 CHECK (dia_vencimento BETWEEN 1 AND 28),
    multa_percentual NUMERIC(5,2) NOT NULL DEFAULT 2 CHECK (multa_percentual >= 0),
    juros_mensal     NUMERIC(5,2) NOT NULL DEFAULT 1 CHECK (juros_mensal >= 0)
);

CREATE TABLE condominio.unidade (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    bloco           VARCHAR(20)   NOT NULL DEFAULT '',
    numero          VARCHAR(20)   NOT NULL,
    tipo            VARCHAR(11)   NOT NULL DEFAULT 'APARTAMENTO',
    fracao_ideal    NUMERIC(12,8) NOT NULL CHECK (fracao_ideal > 0),
    area_m2         NUMERIC(10,2),
    proprietario_id BIGINT        REFERENCES core.participante(id),
    inquilino_id    BIGINT        REFERENCES core.participante(id),
    cobrar_inquilino BOOLEAN      NOT NULL DEFAULT FALSE,
    ativa           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_unidade UNIQUE (empresa_id, bloco, numero),
    CONSTRAINT ck_unidade_tipo CHECK (tipo IN ('APARTAMENTO', 'CASA', 'SALA', 'LOJA', 'VAGA', 'OUTRO'))
);

CREATE TABLE condominio.taxa (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    descricao       VARCHAR(100)  NOT NULL,
    rateio          VARCHAR(6)    NOT NULL,                          -- FRACAO | IGUAL | FIXO
    valor           NUMERIC(15,2) NOT NULL CHECK (valor > 0),        -- total a ratear (FRACAO/IGUAL) ou valor por unidade (FIXO)
    inicio          DATE          NOT NULL,
    fim             DATE,
    ativa           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_taxa_rateio CHECK (rateio IN ('FRACAO', 'IGUAL', 'FIXO')),
    CONSTRAINT ck_taxa_fim CHECK (fim IS NULL OR fim >= inicio)
);

CREATE TABLE condominio.cobranca (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    unidade_id      BIGINT        NOT NULL REFERENCES condominio.unidade(id),
    competencia     DATE          NOT NULL,
    vencimento      DATE          NOT NULL,
    valor           NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    situacao        VARCHAR(9)    NOT NULL DEFAULT 'ABERTA',
    pago_em         DATE,
    multa           NUMERIC(15,2) NOT NULL DEFAULT 0,
    juros           NUMERIC(15,2) NOT NULL DEFAULT 0,
    desconto        NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_pago      NUMERIC(15,2),
    CONSTRAINT uq_cobranca UNIQUE (unidade_id, competencia),
    CONSTRAINT ck_cobranca_dia1 CHECK (competencia = date_trunc('month', competencia)::date),
    CONSTRAINT ck_cobranca_situacao CHECK (situacao IN ('ABERTA', 'PAGA', 'CANCELADA')),
    CONSTRAINT ck_cobranca_paga CHECK ((situacao = 'PAGA') = (pago_em IS NOT NULL AND valor_pago IS NOT NULL))
);
CREATE INDEX ix_cobranca_comp ON condominio.cobranca (empresa_id, competencia);

CREATE TABLE condominio.cobranca_item (
    id              BIGSERIAL PRIMARY KEY,
    cobranca_id     BIGINT        NOT NULL REFERENCES condominio.cobranca(id) ON DELETE CASCADE,
    taxa_id         BIGINT        REFERENCES condominio.taxa(id) ON DELETE SET NULL,
    descricao       VARCHAR(100)  NOT NULL,
    valor           NUMERIC(15,2) NOT NULL
);

CREATE TABLE condominio.despesa (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    data            DATE          NOT NULL,
    vencimento      DATE          NOT NULL,
    fornecedor_id   BIGINT        REFERENCES core.participante(id),
    categoria       VARCHAR(30)   NOT NULL,
    descricao       VARCHAR(200)  NOT NULL,
    valor           NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    pago_em         DATE,
    CONSTRAINT ck_despesa_categoria CHECK (categoria IN ('PESSOAL', 'AGUA', 'ENERGIA', 'GAS', 'MANUTENCAO', 'LIMPEZA', 'SEGURANCA',
                                                         'ADMINISTRACAO', 'SEGURO', 'TRIBUTOS', 'FUNDO_RESERVA', 'OUTRAS'))
);

CREATE TABLE condominio.area_comum (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    nome            VARCHAR(80)   NOT NULL,
    capacidade      INTEGER       NOT NULL DEFAULT 0 CHECK (capacidade >= 0),
    taxa_reserva    NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (taxa_reserva >= 0),
    ativa           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_area_comum UNIQUE (empresa_id, nome)
);

CREATE TABLE condominio.reserva (
    id              BIGSERIAL PRIMARY KEY,
    area_id         BIGINT        NOT NULL REFERENCES condominio.area_comum(id) ON DELETE CASCADE,
    unidade_id      BIGINT        NOT NULL REFERENCES condominio.unidade(id),
    data            DATE          NOT NULL,
    situacao        VARCHAR(9)    NOT NULL DEFAULT 'RESERVADA',
    observacao      VARCHAR(300),
    CONSTRAINT ck_reserva_situacao CHECK (situacao IN ('RESERVADA', 'CANCELADA'))
);
-- uma reserva ativa por área e dia
CREATE UNIQUE INDEX uq_reserva_area_dia ON condominio.reserva (area_id, data) WHERE situacao = 'RESERVADA';

CREATE TABLE condominio.ocorrencia (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id),
    unidade_id      BIGINT        REFERENCES condominio.unidade(id),
    data            DATE          NOT NULL,
    tipo            VARCHAR(11)   NOT NULL,
    titulo          VARCHAR(150)  NOT NULL,
    descricao       TEXT,
    situacao        VARCHAR(9)    NOT NULL DEFAULT 'ABERTA',
    resolvida_em    DATE,
    CONSTRAINT ck_ocorrencia_tipo CHECK (tipo IN ('RECLAMACAO', 'MANUTENCAO', 'ADVERTENCIA', 'MULTA', 'AVISO', 'ASSEMBLEIA')),
    CONSTRAINT ck_ocorrencia_situacao CHECK (situacao IN ('ABERTA', 'RESOLVIDA'))
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON condominio.unidade FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON condominio.taxa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON condominio.cobranca FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON condominio.despesa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.parametro_legal (chave, vigencia_inicio, vigencia_fim, valor, fundamento_legal) VALUES
('CONDOMINIO_ENCARGOS', '2003-01-11', NULL, '{"multaMaxima":2.0,"jurosMensalPadrao":1.0}',
 'Código Civil (Lei nº 10.406/2002), art. 1.336, § 1º: multa de até 2% e juros convencionados ou, não sendo previstos, de 1% ao mês');
