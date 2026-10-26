-- =============================================================================
-- Vorticis Contábil — Migração 060: parâmetro "Contabilidade gerencial"
--   * core.empresa.usa_gerencial: ligado, o menu Relatórios › Gerenciais (centro de custo, orçamento,
--     orçado x realizado) aparece no Contábil; desligado, fica escondido.
--   * Quem já usava centros de custo continua vendo os relatórios gerenciais.
-- =============================================================================

ALTER TABLE core.empresa
    ADD COLUMN usa_gerencial BOOLEAN NOT NULL DEFAULT FALSE;

UPDATE core.empresa SET usa_gerencial = TRUE WHERE usa_centro_custo;
