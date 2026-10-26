-- =============================================================================
-- Vorticis Fiscal — Migração 030: documentos fiscais eletrônicos (NF-e/NFC-e e NFS-e nacional)
--   * Esquema `fiscal` (faixa 030-039 reservada ao módulo Fiscal).
--   * fiscal.documento / fiscal.documento_item: XML importado, deduplicado por chave de acesso
--     por empresa, com entrada/saída identificada pelo CNPJ da empresa, participante vinculado
--     (core.participante) e tributos (ICMS, ICMS-ST, IPI, PIS, COFINS, ISS e retenções).
--   * Novas funções (permissões) do módulo Fiscal nos perfis padrão.
--   A exclusão da empresa (core.fn_excluir_empresa) leva os documentos junto: as chaves para
--   core.empresa são ON DELETE CASCADE e as de lançamento/participante, ON DELETE SET NULL.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS fiscal;

CREATE TABLE fiscal.documento (
    id                  BIGSERIAL     PRIMARY KEY,
    empresa_id          BIGINT        NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    modelo              VARCHAR(5)    NOT NULL CHECK (modelo IN ('55', '65', 'NFSE')),
    chave_acesso        VARCHAR(60)   NOT NULL,                 -- NF-e/NFC-e: 44 dígitos; NFS-e nacional: 50 dígitos
    tipo_operacao       CHAR(1)       NOT NULL CHECK (tipo_operacao IN ('E', 'S')),
    numero              VARCHAR(20)   NOT NULL,
    serie               VARCHAR(10)   NOT NULL DEFAULT '',
    data_emissao        DATE          NOT NULL,
    data_entrada_saida  DATE          NOT NULL,                 -- competência fiscal do livro (entrada/saída; padrão = emissão)
    natureza_operacao   VARCHAR(200),
    situacao            VARCHAR(12)   NOT NULL DEFAULT 'AUTORIZADA' CHECK (situacao IN ('AUTORIZADA', 'CANCELADA', 'DENEGADA')),
    emitente_documento  VARCHAR(14),
    emitente_nome       VARCHAR(150),
    emitente_uf         CHAR(2),
    destinatario_documento VARCHAR(14),
    destinatario_nome   VARCHAR(150),
    destinatario_uf     CHAR(2),
    participante_id     BIGINT        REFERENCES core.participante (id) ON DELETE SET NULL,   -- a outra parte: cliente (saída) ou fornecedor (entrada)
    valor_produtos      NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_desconto      NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_frete         NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_seguro        NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_outras        NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_total         NUMERIC(15,2) NOT NULL DEFAULT 0,       -- vNF (NFS-e: valor do serviço)
    vl_bc_icms          NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_icms             NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_bc_icms_st       NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_icms_st          NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_ipi              NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_pis              NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_cofins           NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_iss              NUMERIC(15,2) NOT NULL DEFAULT 0,
    iss_retido          BOOLEAN       NOT NULL DEFAULT FALSE,
    -- Retenções informadas no documento (retTrib da NF-e; tribFed/tribMun da NFS-e)
    vl_ret_pis          NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_ret_cofins       NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_ret_csll         NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_irrf             NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_ret_prev         NUMERIC(15,2) NOT NULL DEFAULT 0,       -- INSS retido
    xml_original        BYTEA,                                  -- XML original compactado (gzip)
    sha256              CHAR(64),
    lancamento_id       BIGINT        REFERENCES contabil.lancamento (id) ON DELETE SET NULL,   -- contabilização (ver 031)
    contabilizado_em    TIMESTAMPTZ,
    contabilizado_por   BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    importado_em        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    importado_por       BIGINT        REFERENCES core.usuario (id) ON DELETE SET NULL,
    CONSTRAINT uq_documento_chave UNIQUE (empresa_id, chave_acesso)
);
CREATE INDEX ix_documento_periodo ON fiscal.documento (empresa_id, tipo_operacao, data_entrada_saida);
CREATE INDEX ix_documento_participante ON fiscal.documento (participante_id);
CREATE INDEX ix_documento_lancamento ON fiscal.documento (lancamento_id);

CREATE TABLE fiscal.documento_item (
    id                  BIGSERIAL     PRIMARY KEY,
    documento_id        BIGINT        NOT NULL REFERENCES fiscal.documento (id) ON DELETE CASCADE,
    n_item              INTEGER       NOT NULL,
    cod_produto         VARCHAR(60),
    descricao           VARCHAR(500),
    ncm                 VARCHAR(8),
    cest                VARCHAR(7),
    cfop                VARCHAR(10)   NOT NULL,                 -- NFS-e não tem CFOP: usa o código convencional 'NFSE'
    unidade             VARCHAR(10),
    quantidade          NUMERIC(18,4) NOT NULL DEFAULT 0,
    valor_unitario      NUMERIC(24,10) NOT NULL DEFAULT 0,
    valor_produto       NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_desconto      NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_frete         NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_seguro        NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_outras        NUMERIC(15,2) NOT NULL DEFAULT 0,
    valor_contabil      NUMERIC(15,2) NOT NULL DEFAULT 0,       -- produto - desconto + frete + seguro + outras + IPI + ICMS-ST
    cst_icms            VARCHAR(4),                             -- CST (regime normal) ou CSOSN (Simples Nacional)
    origem_mercadoria   CHAR(1),
    vl_bc_icms          NUMERIC(15,2) NOT NULL DEFAULT 0,
    aliq_icms           NUMERIC(9,4)  NOT NULL DEFAULT 0,
    vl_icms             NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_bc_icms_st       NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_icms_st          NUMERIC(15,2) NOT NULL DEFAULT 0,
    cst_ipi             VARCHAR(2),
    vl_bc_ipi           NUMERIC(15,2) NOT NULL DEFAULT 0,
    aliq_ipi            NUMERIC(9,4)  NOT NULL DEFAULT 0,
    vl_ipi              NUMERIC(15,2) NOT NULL DEFAULT 0,
    cst_pis             VARCHAR(2),
    vl_bc_pis           NUMERIC(15,2) NOT NULL DEFAULT 0,
    aliq_pis            NUMERIC(9,4)  NOT NULL DEFAULT 0,
    vl_pis              NUMERIC(15,2) NOT NULL DEFAULT 0,
    cst_cofins          VARCHAR(2),
    vl_bc_cofins        NUMERIC(15,2) NOT NULL DEFAULT 0,
    aliq_cofins         NUMERIC(9,4)  NOT NULL DEFAULT 0,
    vl_cofins           NUMERIC(15,2) NOT NULL DEFAULT 0,
    vl_bc_iss           NUMERIC(15,2) NOT NULL DEFAULT 0,
    aliq_iss            NUMERIC(9,4)  NOT NULL DEFAULT 0,
    vl_iss              NUMERIC(15,2) NOT NULL DEFAULT 0,
    CONSTRAINT uq_documento_item UNIQUE (documento_id, n_item)
);
CREATE INDEX ix_documento_item_cfop ON fiscal.documento_item (cfop);

-- Cancelamento importado antes da nota: aplicado quando o documento for importado.
CREATE TABLE fiscal.cancelamento_pendente (
    empresa_id     BIGINT       NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    chave_acesso   VARCHAR(60)  NOT NULL,
    registrado_em  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    PRIMARY KEY (empresa_id, chave_acesso)
);

-- ---------------------------------------------------------------------------
-- Funções (permissões) do módulo Fiscal. Códigos em Vorticis.Domain.Seguranca.Permissoes.Catalogo.
--   Administrador e Contador: todas · Assistente: importa, consulta e contabiliza · Consulta: consulta e relatórios
-- ---------------------------------------------------------------------------
INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES
        ('FISCAL_DOCUMENTOS_IMPORTAR'), ('FISCAL_DOCUMENTOS_CONSULTAR'), ('FISCAL_CONTABILIZAR'), ('FISCAL_APURAR'),
        ('FISCAL_LALUR_MANTER'), ('FISCAL_RETENCOES_MANTER'), ('FISCAL_RELATORIOS')
       ) AS x(permissao) ON TRUE
 WHERE p.administrador OR p.nome = 'Contador'
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES
        ('Assistente', 'FISCAL_DOCUMENTOS_IMPORTAR'), ('Assistente', 'FISCAL_DOCUMENTOS_CONSULTAR'),
        ('Assistente', 'FISCAL_CONTABILIZAR'), ('Assistente', 'FISCAL_RELATORIOS'),
        ('Consulta', 'FISCAL_DOCUMENTOS_CONSULTAR'), ('Consulta', 'FISCAL_RELATORIOS')
       ) AS x(nome, permissao) ON x.nome = p.nome
 WHERE p.padrao
ON CONFLICT DO NOTHING;
