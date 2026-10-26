-- =============================================================================
-- Vorticis Fiscal — Migração 035: cadastro de produtos, vínculo com o código do emitente e configuração fiscal
--   * fiscal.produto: cadastro de produtos/mercadorias da empresa (código sequencial por empresa, NCM, CEST, GTIN, origem,
--     tipo do item do SPED, CFOP/CST e alíquotas padrão de entrada e saída).
--   * fiscal.produto_vinculo: (emitente + código do produto no emitente) -> produto, usado pela importação de XML
--     para vincular ou cadastrar automaticamente o produto.
--   * fiscal.configuracao: opções do Fiscal por empresa ("Usar centros de custo no fiscal").
--   A exclusão da empresa leva as três tabelas por ON DELETE CASCADE.
-- =============================================================================

CREATE TABLE fiscal.configuracao (
    empresa_id         BIGINT      PRIMARY KEY REFERENCES core.empresa (id) ON DELETE CASCADE,
    usa_centro_custo   BOOLEAN     NOT NULL DEFAULT FALSE,   -- só vale quando core.empresa.usa_centro_custo também está ligado
    atualizado_em      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE fiscal.produto (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL,                -- sequencial por empresa
    descricao          VARCHAR(200)  NOT NULL,
    unidade            VARCHAR(10)   NOT NULL DEFAULT 'UN',
    ncm                VARCHAR(8),
    cest               VARCHAR(7),
    gtin               VARCHAR(14),                           -- código de barras (EAN/GTIN)
    origem             CHAR(1)       NOT NULL DEFAULT '0' CHECK (origem BETWEEN '0' AND '8'),   -- origem da mercadoria (orig do ICMS)
    tipo_item          CHAR(2)       NOT NULL DEFAULT '00' CHECK (tipo_item IN ('00','01','02','03','04','05','06','07','08','09','10','99')),   -- SPED 0200.TIPO_ITEM
    cfop_entrada       VARCHAR(4),
    cfop_saida         VARCHAR(4),
    cst_icms_entrada   VARCHAR(3),
    cst_icms_saida     VARCHAR(3),
    aliq_icms          NUMERIC(9,4)  NOT NULL DEFAULT 0,
    aliq_ipi           NUMERIC(9,4)  NOT NULL DEFAULT 0,
    aliq_pis           NUMERIC(9,4)  NOT NULL DEFAULT 0,
    aliq_cofins        NUMERIC(9,4)  NOT NULL DEFAULT 0,
    ativo              BOOLEAN       NOT NULL DEFAULT TRUE,
    criado_em          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_produto_codigo UNIQUE (empresa_id, codigo)
);
CREATE UNIQUE INDEX uq_produto_gtin ON fiscal.produto (empresa_id, gtin) WHERE gtin IS NOT NULL;
CREATE INDEX ix_produto_descricao ON fiscal.produto (empresa_id, lower(descricao));

CREATE TABLE fiscal.produto_vinculo (
    id                    BIGSERIAL    PRIMARY KEY,
    empresa_id            BIGINT       NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    produto_id            BIGINT       NOT NULL REFERENCES fiscal.produto (id) ON DELETE CASCADE,
    emitente_documento    VARCHAR(14)  NOT NULL DEFAULT '',   -- CNPJ/CPF do emitente da nota
    cod_produto_emitente  VARCHAR(60)  NOT NULL,              -- cProd do XML
    CONSTRAINT uq_produto_vinculo UNIQUE (empresa_id, emitente_documento, cod_produto_emitente)
);
CREATE INDEX ix_produto_vinculo_produto ON fiscal.produto_vinculo (produto_id);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON fiscal.produto
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
