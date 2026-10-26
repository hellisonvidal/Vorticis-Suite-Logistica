-- =============================================================================
-- 074 — Perfil padrão "Manutenção": oficina e almoxarifado da frota.
--   Planos, peças e serviços, ordens de serviço (sem autorizar), consulta dos cadastros e relatórios da frota,
--   notas fiscais de entrada das peças e serviços (importar XML e digitar) e estoque.
-- =============================================================================
INSERT INTO core.perfil (nome, descricao, padrao)
VALUES ('Manutenção', 'Manutenção da frota: ordens de serviço, peças, notas de entrada das oficinas e estoque; não autoriza serviços', TRUE)
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('FROTA_MANUTENCAO'), ('FROTA_RELATORIOS'), ('FISCAL_DOCUMENTOS_IMPORTAR'), ('FISCAL_DOCUMENTOS_CONSULTAR'), ('ERP_ESTOQUE')) AS x(permissao) ON TRUE
 WHERE p.nome = 'Manutenção' AND p.padrao
ON CONFLICT DO NOTHING;
