-- =============================================================================
-- Vorticis — Migração 043
--   Folha › campos exigidos pelo leiaute S-1.3 do eSocial que ainda não existiam no cadastro do funcionário:
--   tipo de logradouro (Tabela 20), CNPJ do sindicato da categoria (cnpjSindCategProf, obrigatório no S-2200/S-2206),
--   tipo e descrição da jornada contratual (tpJornada / dscJorn).
-- =============================================================================
ALTER TABLE folha.funcionario
    ADD COLUMN tipo_logradouro   VARCHAR(4),                        -- eSocial Tabela 20 (R, AV, TV...)
    ADD COLUMN cnpj_sindicato    CHAR(14),                          -- sindicato representativo da categoria
    ADD COLUMN tipo_jornada      SMALLINT     NOT NULL DEFAULT 9,   -- tpJornada: 2 12x36, 3, 4, 5, 6, 7, 9 demais
    ADD COLUMN descricao_jornada VARCHAR(300),                      -- dscJorn (exigida para tpJornada 9; sugerida a partir das horas semanais)
    ADD CONSTRAINT ck_folha_func_cnpj_sind CHECK (cnpj_sindicato IS NULL OR cnpj_sindicato ~ '^[0-9]{14}$'),
    ADD CONSTRAINT ck_folha_func_tp_jornada CHECK (tipo_jornada IN (2, 3, 4, 5, 6, 7, 9));
