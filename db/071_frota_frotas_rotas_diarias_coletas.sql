-- =============================================================================
-- 071 — Frota: frotas, rotas, diárias e ordens de coleta.
--   * frota.frota (própria/agregada/terceiros) e vínculo do veículo e do centro de custo (contabil.centro_custo.frota_id) com a frota.
--   * frota.rota + paradas (cidades por UF/IBGE), com distância automática (Haversine x fator rodoviário) ou manual.
--   * frota.diaria: diárias de motoristas/ajudantes com adiantamento e acerto; valores por tipo em frota.diaria_valor.
--   * frota.coleta: ordens de coleta.
--   * Parâmetros: fator rodoviário e velocidade média (rotas).
-- =============================================================================
CREATE TABLE frota.frota (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL CHECK (codigo > 0),
    descricao          VARCHAR(100)  NOT NULL,
    tipo               VARCHAR(8)    NOT NULL DEFAULT 'PROPRIA',
    responsavel        VARCHAR(100),
    centro_custo_id    BIGINT        REFERENCES contabil.centro_custo(id) ON DELETE SET NULL,
    ativa              BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_frota_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_frota_tipo CHECK (tipo IN ('PROPRIA', 'AGREGADA', 'TERCEIROS'))
);

ALTER TABLE frota.veiculo ADD COLUMN frota_id BIGINT REFERENCES frota.frota(id) ON DELETE SET NULL;
ALTER TABLE contabil.centro_custo ADD COLUMN frota_id BIGINT REFERENCES frota.frota(id) ON DELETE SET NULL;

ALTER TABLE frota.configuracao
    ADD COLUMN fator_rodoviario NUMERIC(4,2) NOT NULL DEFAULT 1.25 CHECK (fator_rodoviario BETWEEN 1 AND 3),
    ADD COLUMN velocidade_media NUMERIC(5,1) NOT NULL DEFAULT 60 CHECK (velocidade_media BETWEEN 10 AND 130);

CREATE TABLE frota.rota (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL CHECK (codigo > 0),
    descricao          VARCHAR(100)  NOT NULL,
    distancia_km       NUMERIC(10,1) NOT NULL DEFAULT 0 CHECK (distancia_km >= 0),
    distancia_origem   VARCHAR(10)   NOT NULL DEFAULT 'ESTIMADA',
    tempo_horas        NUMERIC(7,2)  NOT NULL DEFAULT 0 CHECK (tempo_horas >= 0),
    pedagios           NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (pedagios >= 0),
    ativa              BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_rota_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_rota_dist_origem CHECK (distancia_origem IN ('ESTIMADA', 'RODOVIARIA', 'MANUAL'))
);

CREATE TABLE frota.rota_parada (
    rota_id            BIGINT        NOT NULL REFERENCES frota.rota(id) ON DELETE CASCADE,
    ordem              SMALLINT      NOT NULL CHECK (ordem >= 0),
    uf                 CHAR(2)       NOT NULL,
    municipio_ibge     CHAR(7)       NOT NULL CHECK (municipio_ibge ~ '^[0-9]{7}$'),
    municipio          VARCHAR(100)  NOT NULL,
    PRIMARY KEY (rota_id, ordem)
);

CREATE TABLE frota.diaria_valor (
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    tipo               VARCHAR(12)   NOT NULL,
    valor              NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor >= 0),
    PRIMARY KEY (empresa_id, tipo),
    CONSTRAINT ck_diaria_valor_tipo CHECK (tipo IN ('PERNOITE', 'ALIMENTACAO', 'REFEICAO', 'HOSPEDAGEM', 'OUTRA'))
);

CREATE TABLE frota.diaria (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL,
    data               DATE          NOT NULL,
    motorista_id       BIGINT        NOT NULL REFERENCES frota.motorista(id),
    funcao             VARCHAR(9)    NOT NULL DEFAULT 'MOTORISTA',
    veiculo_id         BIGINT        REFERENCES frota.veiculo(id),
    frota_id           BIGINT        REFERENCES frota.frota(id) ON DELETE SET NULL,
    viagem_id          BIGINT        REFERENCES frota.viagem(id) ON DELETE SET NULL,
    rota_id            BIGINT        REFERENCES frota.rota(id) ON DELETE SET NULL,
    tipo               VARCHAR(12)   NOT NULL,
    quantidade         NUMERIC(6,2)  NOT NULL CHECK (quantidade > 0),
    valor_unitario     NUMERIC(15,2) NOT NULL CHECK (valor_unitario >= 0),
    adiantamento       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (adiantamento >= 0),
    valor_gasto        NUMERIC(15,2) CHECK (valor_gasto IS NULL OR valor_gasto >= 0),
    situacao           VARCHAR(8)    NOT NULL DEFAULT 'LANCADA',
    centro_custo_id    BIGINT        REFERENCES contabil.centro_custo(id) ON DELETE SET NULL,
    duplicata_id       BIGINT        REFERENCES fiscal.duplicata(id) ON DELETE SET NULL,
    observacao         VARCHAR(300),
    CONSTRAINT uq_diaria_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_diaria_situacao CHECK (situacao IN ('LANCADA', 'PAGA', 'ACERTADA')),
    CONSTRAINT ck_diaria_funcao CHECK (funcao IN ('MOTORISTA', 'AJUDANTE')),
    CONSTRAINT ck_diaria_tipo CHECK (tipo IN ('PERNOITE', 'ALIMENTACAO', 'REFEICAO', 'HOSPEDAGEM', 'OUTRA'))
);
CREATE INDEX ix_diaria_data ON frota.diaria (empresa_id, data);

CREATE TABLE frota.coleta (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL,
    agendada_em        TIMESTAMP     NOT NULL,
    remetente_id       BIGINT        REFERENCES core.participante(id) ON DELETE SET NULL,
    destinatario_id    BIGINT        REFERENCES core.participante(id) ON DELETE SET NULL,
    endereco_coleta    VARCHAR(250)  NOT NULL,
    mercadoria         VARCHAR(150),
    volumes            INTEGER       NOT NULL DEFAULT 0 CHECK (volumes >= 0),
    peso_kg            NUMERIC(12,3) NOT NULL DEFAULT 0 CHECK (peso_kg >= 0),
    valor_mercadoria   NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_mercadoria >= 0),
    veiculo_id         BIGINT        REFERENCES frota.veiculo(id),
    motorista_id       BIGINT        REFERENCES frota.motorista(id),
    rota_id            BIGINT        REFERENCES frota.rota(id) ON DELETE SET NULL,
    situacao           VARCHAR(10)   NOT NULL DEFAULT 'AGENDADA',
    observacao         VARCHAR(500),
    CONSTRAINT uq_coleta_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_coleta_situacao CHECK (situacao IN ('AGENDADA', 'EM_COLETA', 'COLETADA', 'CANCELADA'))
);
CREATE INDEX ix_coleta_data ON frota.coleta (empresa_id, agendada_em);
