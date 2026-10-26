-- =============================================================================
-- 076 — Pedágio: layouts dos arquivos das operadoras (Sem Parar, ConectCar, Veloe...) e as passagens importadas.
--   * frota.pedagio_layout: a operadora (fornecedor da nota) e os nomes das colunas da planilha/CSV (data, hora, placa, praça, valor)
--     e o CFOP de entrada usado na nota fiscal gerada.
--   * frota.pedagio: passagens por veículo; a mesma passagem não entra duas vezes; documento_id = nota fiscal de entrada gerada.
-- =============================================================================
CREATE TABLE frota.pedagio_layout (
    id              BIGSERIAL     PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    nome            VARCHAR(60)   NOT NULL,
    operadora_id    BIGINT        NOT NULL REFERENCES core.participante(id),
    coluna_data     VARCHAR(80)   NOT NULL,
    coluna_hora     VARCHAR(80),
    coluna_placa    VARCHAR(80)   NOT NULL,
    coluna_praca    VARCHAR(80),
    coluna_valor    VARCHAR(80)   NOT NULL,
    cfop            CHAR(4)       NOT NULL DEFAULT '1949' CHECK (cfop ~ '^[123][0-9]{3}$'),
    ativo           BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_pedagio_layout_nome UNIQUE (empresa_id, nome)
);

CREATE TABLE frota.pedagio (
    id              BIGSERIAL     PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    layout_id       BIGINT        REFERENCES frota.pedagio_layout(id) ON DELETE SET NULL,
    data_hora       TIMESTAMP     NOT NULL,
    placa           VARCHAR(8)    NOT NULL,
    veiculo_id      BIGINT        REFERENCES frota.veiculo(id) ON DELETE SET NULL,
    praca           VARCHAR(150)  NOT NULL DEFAULT '',
    valor           NUMERIC(15,2) NOT NULL CHECK (valor > 0),
    documento_id    BIGINT        REFERENCES fiscal.documento(id) ON DELETE SET NULL,
    arquivo         VARCHAR(200),
    importado_em    TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_pedagio_passagem UNIQUE (empresa_id, placa, data_hora, praca, valor)
);
CREATE INDEX ix_pedagio_data ON frota.pedagio (empresa_id, data_hora);
CREATE INDEX ix_pedagio_documento ON frota.pedagio (documento_id);
