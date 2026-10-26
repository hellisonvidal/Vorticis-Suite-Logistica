-- =============================================================================
-- Vorticis Contábil — Migração 026: geração do arquivo da ECF (SPED — Escrituração Contábil Fiscal)
--   * Nova função no catálogo de permissões: SPED_ECF_GERAR (gerar e assinar o arquivo da ECF),
--     liberada nos perfis padrão Contador e Administrador.
--   * contabil.ecf_gerada: histórico dos arquivos gerados (forma de tributação, hash SHA-1 do arquivo
--     sem assinaturas, linhas e assinaturas). A exclusão da empresa remove o histórico (ON DELETE CASCADE).
-- =============================================================================

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'SPED_ECF_GERAR'
  FROM core.perfil p
 WHERE p.padrao AND (p.nome = 'Contador' OR p.administrador)
ON CONFLICT DO NOTHING;

CREATE TABLE contabil.ecf_gerada (
    id             BIGSERIAL PRIMARY KEY,
    empresa_id     BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    ano            SMALLINT     NOT NULL,                 -- ano-calendário
    dt_ini         DATE         NOT NULL,
    dt_fin         DATE         NOT NULL,
    forma_trib     VARCHAR(10)  NOT NULL,                 -- REAL | PRESUMIDO
    hash_sha1      CHAR(40)     NOT NULL,                 -- SHA-1 (hexadecimal maiúsculo) do arquivo sem assinaturas
    linhas         INTEGER      NOT NULL,                 -- ECF 9999.QTD_LIN
    assinaturas    SMALLINT     NOT NULL DEFAULT 0,
    gerado_em      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    gerado_por     BIGINT       REFERENCES core.usuario(id) ON DELETE SET NULL,
    CONSTRAINT ck_ecf_periodo CHECK (dt_fin >= dt_ini),
    CONSTRAINT ck_ecf_forma CHECK (forma_trib IN ('REAL', 'PRESUMIDO')),
    CONSTRAINT ck_ecf_hash CHECK (hash_sha1 ~ '^[0-9A-F]{40}$')
);
CREATE INDEX ix_ecf_gerada_empresa ON contabil.ecf_gerada (empresa_id, ano);
