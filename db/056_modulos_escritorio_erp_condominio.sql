-- =============================================================================
-- 056 — Novos módulos da suíte: Escritório, ERP e Condomínio
--   * Indicadores de módulo na empresa (as existentes ficam fora dos módulos novos até serem marcadas).
--   * Funções (permissões) dos módulos: Administrador e Contador recebem todas; Assistente as operacionais;
--     Consulta os relatórios.
-- =============================================================================
ALTER TABLE core.empresa
    ADD COLUMN modulo_escritorio BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN modulo_erp        BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN modulo_condominio BOOLEAN NOT NULL DEFAULT FALSE,
    DROP CONSTRAINT ck_empresa_algum_modulo,
    ADD CONSTRAINT ck_empresa_algum_modulo
        CHECK (modulo_contabil OR modulo_fiscal OR modulo_folha OR modulo_escritorio OR modulo_erp OR modulo_condominio);

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('ESCRITORIO_TAREFAS'), ('ESCRITORIO_HONORARIOS'), ('ESCRITORIO_RELATORIOS'),
               ('ERP_VENDAS'), ('ERP_COMPRAS'), ('ERP_ESTOQUE'), ('ERP_TRANSPORTE'), ('ERP_RELATORIOS'),
               ('CONDOMINIO_CADASTROS'), ('CONDOMINIO_FINANCEIRO'), ('CONDOMINIO_RELATORIOS')) AS x(permissao) ON TRUE
 WHERE p.administrador OR (p.padrao AND p.nome = 'Contador')
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('Assistente', 'ESCRITORIO_TAREFAS'), ('Assistente', 'ERP_VENDAS'), ('Assistente', 'ERP_COMPRAS'), ('Assistente', 'ERP_ESTOQUE'),
               ('Assistente', 'ERP_TRANSPORTE'), ('Assistente', 'CONDOMINIO_CADASTROS'), ('Assistente', 'CONDOMINIO_FINANCEIRO'),
               ('Consulta', 'ESCRITORIO_RELATORIOS'), ('Consulta', 'ERP_RELATORIOS'), ('Consulta', 'CONDOMINIO_RELATORIOS')) AS x(nome, permissao)
    ON x.nome = p.nome
 WHERE p.padrao
ON CONFLICT DO NOTHING;
