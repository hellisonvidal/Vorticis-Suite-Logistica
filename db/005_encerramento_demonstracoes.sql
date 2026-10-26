-- =============================================================================
-- Perseus Contábil — Migração 005
--   Novas funções no catálogo de permissões: encerramento do exercício e
--   demonstrações (BP/DRE). Liberadas nos perfis padrão:
--     Contador   -> EXERCICIO_ENCERRAR, RELATORIO_DEMONSTRACOES
--     Assistente -> RELATORIO_DEMONSTRACOES
--     Consulta   -> RELATORIO_DEMONSTRACOES
--   Perfis criados pelo administrador não são alterados.
-- =============================================================================

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('Contador', 'EXERCICIO_ENCERRAR'), ('Contador', 'RELATORIO_DEMONSTRACOES'),
               ('Assistente', 'RELATORIO_DEMONSTRACOES'), ('Consulta', 'RELATORIO_DEMONSTRACOES')) AS x(nome, permissao)
    ON x.nome = p.nome
 WHERE p.padrao
ON CONFLICT DO NOTHING;

-- Lançamentos de encerramento só por meio do serviço de encerramento (origem ENCERRAMENTO, ou o estorno dele).
CREATE INDEX ix_lcto_origem ON contabil.lancamento (empresa_id, exercicio_id, origem) WHERE status = 'EFETIVADO';
