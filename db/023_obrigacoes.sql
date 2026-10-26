-- =============================================================================
-- Vorticis — Migração 023: calendário de obrigações
--   * core.obrigacao_personalizada: obrigações da empresa que não estão no catálogo federal embutido
--     (ISS, ICMS, taxas municipais/estaduais, contratos...), com a mesma regra de vencimento do catálogo.
--   * core.obrigacao_status: baixa (cumprimento) por empresa/obrigação/competência. O código é o do catálogo
--     federal (ex.: DAS) ou "P<id>" para personalizadas; competência "AAAA-MM" (mensal/trimestral) ou "AAAA" (anual).
--   * Permissão OBRIGACOES_MANTER: Administrador, Contador e Assistente. A leitura é de todo perfil com acesso à empresa.
-- =============================================================================

CREATE TABLE core.obrigacao_personalizada (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    nome            VARCHAR(120) NOT NULL,
    orgao           VARCHAR(80)  NOT NULL DEFAULT '',
    esfera          VARCHAR(10)  NOT NULL DEFAULT 'MUNICIPAL',
    periodicidade   VARCHAR(10)  NOT NULL DEFAULT 'MENSAL',
    regra           VARCHAR(20)  NOT NULL DEFAULT 'DIA_FIXO',
    dia             INT          NOT NULL DEFAULT 10,
    meses_apos      INT          NOT NULL DEFAULT 1,
    mes_vencimento  INT          NOT NULL DEFAULT 1,
    anos_apos       INT          NOT NULL DEFAULT 0,
    ajuste          VARCHAR(10)  NOT NULL DEFAULT 'POSTERGA',
    observacao      VARCHAR(500),
    ativa           BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_obrig_esfera CHECK (esfera IN ('FEDERAL', 'ESTADUAL', 'MUNICIPAL', 'OUTRA')),
    CONSTRAINT ck_obrig_period CHECK (periodicidade IN ('MENSAL', 'TRIMESTRAL', 'ANUAL')),
    CONSTRAINT ck_obrig_regra  CHECK (regra IN ('DIA_FIXO', 'DIA_UTIL', 'ULTIMO_DIA_UTIL')),
    CONSTRAINT ck_obrig_ajuste CHECK (ajuste IN ('ANTECIPA', 'POSTERGA', 'NENHUM')),
    CONSTRAINT ck_obrig_dia    CHECK (dia BETWEEN 0 AND 31 AND meses_apos BETWEEN 0 AND 24 AND mes_vencimento BETWEEN 1 AND 12 AND anos_apos BETWEEN 0 AND 3)
);
CREATE INDEX ix_obrig_pers_empresa ON core.obrigacao_personalizada (empresa_id);

CREATE TABLE core.obrigacao_status (
    empresa_id     BIGINT      NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    obrigacao      VARCHAR(30) NOT NULL,
    competencia    VARCHAR(7)  NOT NULL,
    cumprida_em    DATE        NOT NULL,
    observacao     VARCHAR(500),
    usuario_id     BIGINT      REFERENCES core.usuario(id),
    atualizado_em  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (empresa_id, obrigacao, competencia)
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.obrigacao_personalizada FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'OBRIGACOES_MANTER'
  FROM core.perfil p
 WHERE p.padrao AND (p.administrador OR p.nome IN ('Contador', 'Assistente'))
ON CONFLICT DO NOTHING;
