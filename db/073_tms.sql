-- =============================================================================
-- 073 — TMS (gestão do transporte) do Vorticis Suite Logística, sobre o cadastro do Frota.
--   * tms.tabela_frete: frete-peso (R$/kg com mínimo e cubagem), ad valorem, GRIS, pedágio por fração de 100 kg,
--     taxas de coleta e entrega, alíquota de ICMS informada pelo usuário e prazo de entrega; por rota ou por UF de origem/destino.
--   * tms.cotacao: cotação de frete para o cliente (aberta, aprovada, recusada, convertida em CT-e).
--   * tms.cte: conhecimento de transporte (digitado, emitido, em trânsito, entregue, cancelado), ligado à coleta, à viagem,
--     ao veículo e ao motorista do Frota; a chave de acesso é informada ao emitir (transmissão à SEFAZ fica para depois).
--   * tms.manifesto + tms.manifesto_cte: manifesto de carga (MDF-e) que reúne os CT-e de um veículo (aberto, encerrado, cancelado).
--   * tms.ocorrencia: rastreamento da entrega (saída, chegada, saiu para entrega, entregue, tentativa, avaria, devolução).
--   * tms.fatura: fatura de frete que reúne CT-e do mesmo tomador e gera a conta a receber (fiscal.duplicata).
--   * Função FROTA_TMS nos perfis que já tinham FROTA_OPERACAO e nos administradores.
-- =============================================================================
CREATE SCHEMA IF NOT EXISTS tms;

CREATE TABLE tms.tabela_frete (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL CHECK (codigo > 0),
    descricao          VARCHAR(100)  NOT NULL,
    rota_id            BIGINT        REFERENCES frota.rota(id) ON DELETE SET NULL,
    uf_origem          CHAR(2),
    uf_destino         CHAR(2),
    valor_kg           NUMERIC(12,4) NOT NULL DEFAULT 0 CHECK (valor_kg >= 0),
    frete_minimo       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (frete_minimo >= 0),
    fator_cubagem      NUMERIC(8,2)  NOT NULL DEFAULT 300 CHECK (fator_cubagem >= 0),
    ad_valorem_pct     NUMERIC(7,4)  NOT NULL DEFAULT 0 CHECK (ad_valorem_pct BETWEEN 0 AND 100),
    gris_pct           NUMERIC(7,4)  NOT NULL DEFAULT 0 CHECK (gris_pct BETWEEN 0 AND 100),
    pedagio_fracao     NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (pedagio_fracao >= 0),
    taxa_coleta        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (taxa_coleta >= 0),
    taxa_entrega       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (taxa_entrega >= 0),
    outras_taxas       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (outras_taxas >= 0),
    aliquota_icms      NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (aliquota_icms BETWEEN 0 AND 99),
    prazo_dias         INTEGER       NOT NULL DEFAULT 0 CHECK (prazo_dias >= 0),
    ativa              BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_tabela_frete_codigo UNIQUE (empresa_id, codigo)
);

CREATE TABLE tms.cotacao (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL,
    data               DATE          NOT NULL,
    validade           DATE          NOT NULL,
    cliente_id         BIGINT        NOT NULL REFERENCES core.participante(id),
    tabela_id          BIGINT        NOT NULL REFERENCES tms.tabela_frete(id),
    origem             VARCHAR(100)  NOT NULL,
    destino            VARCHAR(100)  NOT NULL,
    peso_kg            NUMERIC(12,3) NOT NULL DEFAULT 0 CHECK (peso_kg >= 0),
    volume_m3          NUMERIC(10,3) NOT NULL DEFAULT 0 CHECK (volume_m3 >= 0),
    valor_mercadoria   NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_mercadoria >= 0),
    coleta             BOOLEAN       NOT NULL DEFAULT FALSE,
    entrega            BOOLEAN       NOT NULL DEFAULT TRUE,
    valor_total        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_total >= 0),
    situacao           VARCHAR(10)   NOT NULL DEFAULT 'ABERTA',
    observacao         VARCHAR(500),
    CONSTRAINT uq_cotacao_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_cotacao_situacao CHECK (situacao IN ('ABERTA', 'APROVADA', 'RECUSADA', 'CONVERTIDA')),
    CONSTRAINT ck_cotacao_validade CHECK (validade >= data)
);

CREATE TABLE tms.fatura (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL,
    emissao            DATE          NOT NULL,
    vencimento         DATE          NOT NULL,
    cliente_id         BIGINT        NOT NULL REFERENCES core.participante(id),
    valor_total        NUMERIC(15,2) NOT NULL CHECK (valor_total > 0),
    duplicata_id       BIGINT        REFERENCES fiscal.duplicata(id) ON DELETE SET NULL,
    situacao           VARCHAR(9)    NOT NULL DEFAULT 'ABERTA',
    observacao         VARCHAR(500),
    CONSTRAINT uq_fatura_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_fatura_situacao CHECK (situacao IN ('ABERTA', 'CANCELADA')),
    CONSTRAINT ck_fatura_vencimento CHECK (vencimento >= emissao)
);

CREATE TABLE tms.cte (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    serie              SMALLINT      NOT NULL DEFAULT 1 CHECK (serie BETWEEN 0 AND 999),
    numero             INTEGER       NOT NULL CHECK (numero > 0),
    emissao            DATE          NOT NULL,
    cfop               CHAR(4),
    remetente_id       BIGINT        NOT NULL REFERENCES core.participante(id),
    destinatario_id    BIGINT        NOT NULL REFERENCES core.participante(id),
    tomador_id         BIGINT        NOT NULL REFERENCES core.participante(id),
    origem_uf          CHAR(2)       NOT NULL,
    origem_ibge        CHAR(7)       NOT NULL CHECK (origem_ibge ~ '^[0-9]{7}$'),
    origem_municipio   VARCHAR(100)  NOT NULL,
    destino_uf         CHAR(2)       NOT NULL,
    destino_ibge       CHAR(7)       NOT NULL CHECK (destino_ibge ~ '^[0-9]{7}$'),
    destino_municipio  VARCHAR(100)  NOT NULL,
    tabela_id          BIGINT        REFERENCES tms.tabela_frete(id) ON DELETE SET NULL,
    cotacao_id         BIGINT        REFERENCES tms.cotacao(id) ON DELETE SET NULL,
    coleta_id          BIGINT        REFERENCES frota.coleta(id) ON DELETE SET NULL,
    viagem_id          BIGINT        REFERENCES frota.viagem(id) ON DELETE SET NULL,
    veiculo_id         BIGINT        REFERENCES frota.veiculo(id),
    motorista_id       BIGINT        REFERENCES frota.motorista(id),
    produto            VARCHAR(100),
    volumes            INTEGER       NOT NULL DEFAULT 0 CHECK (volumes >= 0),
    peso_kg            NUMERIC(12,3) NOT NULL DEFAULT 0 CHECK (peso_kg >= 0),
    volume_m3          NUMERIC(10,3) NOT NULL DEFAULT 0 CHECK (volume_m3 >= 0),
    valor_mercadoria   NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_mercadoria >= 0),
    chaves_nfe         VARCHAR(2000),
    frete_peso         NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (frete_peso >= 0),
    ad_valorem         NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (ad_valorem >= 0),
    gris               NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (gris >= 0),
    pedagio            NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (pedagio >= 0),
    taxas              NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (taxas >= 0),
    aliquota_icms      NUMERIC(5,2)  NOT NULL DEFAULT 0 CHECK (aliquota_icms BETWEEN 0 AND 99),
    icms               NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (icms >= 0),
    valor_total        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_total >= 0),
    previsao_entrega   DATE,
    chave              CHAR(44),
    protocolo          VARCHAR(20),
    situacao           VARCHAR(11)   NOT NULL DEFAULT 'DIGITADO',
    entregue_em        TIMESTAMP,
    recebedor          VARCHAR(100),
    fatura_id          BIGINT        REFERENCES tms.fatura(id) ON DELETE SET NULL,
    observacao         VARCHAR(500),
    CONSTRAINT uq_cte_numero UNIQUE (empresa_id, serie, numero),
    CONSTRAINT ck_cte_situacao CHECK (situacao IN ('DIGITADO', 'EMITIDO', 'EM_TRANSITO', 'ENTREGUE', 'CANCELADO')),
    CONSTRAINT ck_cte_chave CHECK (chave IS NULL OR chave ~ '^[0-9]{44}$'),
    CONSTRAINT ck_cte_emitido_tem_chave CHECK (situacao IN ('DIGITADO', 'CANCELADO') OR chave IS NOT NULL),
    CONSTRAINT ck_cte_entregue CHECK (situacao <> 'ENTREGUE' OR entregue_em IS NOT NULL)
);
CREATE UNIQUE INDEX uq_cte_chave ON tms.cte (chave) WHERE chave IS NOT NULL;
CREATE INDEX ix_cte_emissao ON tms.cte (empresa_id, emissao);
CREATE INDEX ix_cte_fatura ON tms.cte (fatura_id);

CREATE TABLE tms.manifesto (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    serie              SMALLINT      NOT NULL DEFAULT 1 CHECK (serie BETWEEN 0 AND 999),
    numero             INTEGER       NOT NULL CHECK (numero > 0),
    emissao            DATE          NOT NULL,
    uf_inicio          CHAR(2)       NOT NULL,
    uf_fim             CHAR(2)       NOT NULL,
    ufs_percurso       VARCHAR(80),
    veiculo_id         BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    motorista_id       BIGINT        NOT NULL REFERENCES frota.motorista(id),
    viagem_id          BIGINT        REFERENCES frota.viagem(id) ON DELETE SET NULL,
    chave              CHAR(44),
    situacao           VARCHAR(9)    NOT NULL DEFAULT 'ABERTO',
    encerrado_em       DATE,
    observacao         VARCHAR(500),
    CONSTRAINT uq_manifesto_numero UNIQUE (empresa_id, serie, numero),
    CONSTRAINT ck_manifesto_situacao CHECK (situacao IN ('ABERTO', 'ENCERRADO', 'CANCELADO')),
    CONSTRAINT ck_manifesto_chave CHECK (chave IS NULL OR chave ~ '^[0-9]{44}$'),
    CONSTRAINT ck_manifesto_encerrado CHECK (situacao <> 'ENCERRADO' OR encerrado_em IS NOT NULL)
);

CREATE TABLE tms.manifesto_cte (
    manifesto_id       BIGINT        NOT NULL REFERENCES tms.manifesto(id) ON DELETE CASCADE,
    cte_id             BIGINT        NOT NULL REFERENCES tms.cte(id),
    PRIMARY KEY (manifesto_id, cte_id)
);
CREATE INDEX ix_manifesto_cte_cte ON tms.manifesto_cte (cte_id);

CREATE TABLE tms.ocorrencia (
    id                 BIGSERIAL     PRIMARY KEY,
    cte_id             BIGINT        NOT NULL REFERENCES tms.cte(id) ON DELETE CASCADE,
    data_hora          TIMESTAMP     NOT NULL,
    tipo               VARCHAR(17)   NOT NULL,
    descricao          VARCHAR(300),
    recebedor          VARCHAR(100),
    registrado_por     BIGINT        REFERENCES core.usuario(id) ON DELETE SET NULL,
    CONSTRAINT ck_ocorrencia_tipo CHECK (tipo IN ('SAIDA', 'CHEGADA', 'SAIU_PARA_ENTREGA', 'ENTREGUE', 'TENTATIVA', 'AVARIA', 'DEVOLUCAO'))
);
CREATE INDEX ix_ocorrencia_cte ON tms.ocorrencia (cte_id, data_hora);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON tms.cte FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON tms.fatura FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON tms.manifesto FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT DISTINCT pp.perfil_id, 'FROTA_TMS' FROM core.perfil_permissao pp WHERE pp.permissao = 'FROTA_OPERACAO'
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'FROTA_TMS' FROM core.perfil p WHERE p.administrador
ON CONFLICT DO NOTHING;
