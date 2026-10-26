-- =============================================================================
-- Vorticis Fiscal — Migração 047: SPED Fiscal (EFD ICMS/IPI) e EFD-Contribuições
--   * Nova função no catálogo de permissões: FISCAL_SPED_GERAR (gerar, assinar e consultar o histórico dos
--     arquivos da EFD ICMS/IPI e da EFD-Contribuições), liberada nos perfis padrão Contador e Administrador.
--   * fiscal.efd_gerada: histórico dos arquivos gerados (tipo, período, perfil, finalidade, hash SHA-1 do arquivo
--     sem assinaturas, linhas e assinaturas). A exclusão da empresa remove o histórico (ON DELETE CASCADE).
-- =============================================================================

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'FISCAL_SPED_GERAR'
  FROM core.perfil p
 WHERE p.administrador OR (p.padrao AND p.nome = 'Contador')
ON CONFLICT DO NOTHING;

CREATE TABLE fiscal.efd_gerada (
    id             BIGSERIAL    PRIMARY KEY,
    empresa_id     BIGINT       NOT NULL REFERENCES core.empresa (id) ON DELETE CASCADE,
    tipo           VARCHAR(14)  NOT NULL CHECK (tipo IN ('ICMS_IPI', 'CONTRIBUICOES')),
    dt_ini         DATE         NOT NULL,
    dt_fin         DATE         NOT NULL,
    perfil         CHAR(1),                               -- EFD ICMS/IPI: A, B ou C (nulo na EFD-Contribuições)
    finalidade     CHAR(1)      NOT NULL DEFAULT '0',     -- 0 = original, 1 = retificadora/substituto
    hash_sha1      CHAR(40)     NOT NULL CHECK (hash_sha1 ~ '^[0-9A-F]{40}$'),   -- SHA-1 (hexadecimal maiúsculo) do arquivo sem assinaturas
    linhas         INTEGER      NOT NULL,                 -- 9999.QTD_LIN
    assinaturas    SMALLINT     NOT NULL DEFAULT 0,
    gerado_em      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    gerado_por     BIGINT       REFERENCES core.usuario (id) ON DELETE SET NULL,
    CONSTRAINT ck_efd_periodo CHECK (dt_fin >= dt_ini)
);
CREATE INDEX ix_efd_gerada_empresa ON fiscal.efd_gerada (empresa_id, tipo, dt_ini);
