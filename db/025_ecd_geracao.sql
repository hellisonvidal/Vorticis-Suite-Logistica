-- =============================================================================
-- Vorticis Contábil — Migração 025: geração do arquivo da ECD (SPED Contábil)
--   * Nova função no catálogo de permissões: SPED_ECD_GERAR (gerar e assinar o arquivo da ECD),
--     liberada nos perfis padrão Contador e Administrador.
--   * contabil.ecd_gerada: histórico dos arquivos gerados (número de ordem do livro, hash SHA-1 do
--     arquivo sem assinaturas, quantidade de linhas e de assinaturas). Serve para sugerir o próximo
--     número de ordem (I030/J900.NUM_ORD) e para conferir o hash informado em uma ECD substituta.
--     A exclusão da empresa remove o histórico junto (ON DELETE CASCADE).
-- =============================================================================

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'SPED_ECD_GERAR'
  FROM core.perfil p
 WHERE p.padrao AND (p.nome = 'Contador' OR p.administrador)
ON CONFLICT DO NOTHING;

CREATE TABLE contabil.ecd_gerada (
    id             BIGSERIAL PRIMARY KEY,
    empresa_id     BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    ano            SMALLINT     NOT NULL,
    dt_ini         DATE         NOT NULL,
    dt_fin         DATE         NOT NULL,
    num_ordem      INTEGER      NOT NULL,                 -- ECD I030/J900.NUM_ORD (número do livro)
    nat_livro      VARCHAR(80)  NOT NULL,                 -- ECD I030.NAT_LIVR
    hash_sha1      CHAR(40)     NOT NULL,                 -- SHA-1 (hexadecimal maiúsculo) do arquivo sem assinaturas
    linhas         INTEGER      NOT NULL,                 -- ECD 9999.QTD_LIN
    assinaturas    SMALLINT     NOT NULL DEFAULT 0,
    gerado_em      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    gerado_por     BIGINT       REFERENCES core.usuario(id) ON DELETE SET NULL,
    CONSTRAINT ck_ecd_periodo CHECK (dt_fin >= dt_ini),
    CONSTRAINT ck_ecd_num_ordem CHECK (num_ordem > 0),
    CONSTRAINT ck_ecd_hash CHECK (hash_sha1 ~ '^[0-9A-F]{40}$')
);
CREATE INDEX ix_ecd_gerada_empresa ON contabil.ecd_gerada (empresa_id, ano);
