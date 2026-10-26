-- =============================================================================
-- Vorticis — Migração 051: orçamento anual (Relatórios › Gerenciais › Orçado x Realizado)
--   * contabil.orcamento: valor orçado por empresa, ano, mês, conta de resultado e centro de custo (opcional).
--     O valor é na natureza da conta (receita credora e despesa devedora positivas).
--   * Permissão ORCAMENTO_MANTER: Administrador e Contador. A leitura segue as permissões de relatório.
-- =============================================================================

CREATE TABLE contabil.orcamento (
    id              BIGSERIAL PRIMARY KEY,
    empresa_id      BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    ano             SMALLINT      NOT NULL CHECK (ano BETWEEN 2000 AND 2100),
    mes             SMALLINT      NOT NULL CHECK (mes BETWEEN 1 AND 12),
    conta_id        BIGINT        NOT NULL REFERENCES contabil.plano_contas(id) ON DELETE CASCADE,
    centro_custo_id BIGINT        REFERENCES contabil.centro_custo(id) ON DELETE CASCADE,
    valor           NUMERIC(16,2) NOT NULL
);
CREATE UNIQUE INDEX uq_orcamento ON contabil.orcamento (empresa_id, ano, mes, conta_id, COALESCE(centro_custo_id, 0));
CREATE INDEX ix_orcamento_empresa_ano ON contabil.orcamento (empresa_id, ano);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON contabil.orcamento FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, 'ORCAMENTO_MANTER'
  FROM core.perfil p
 WHERE p.padrao AND (p.administrador OR p.nome = 'Contador')
ON CONFLICT DO NOTHING;
