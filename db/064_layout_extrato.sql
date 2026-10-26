-- =============================================================================
-- Vorticis — Migração 064: layouts personalizados de extrato bancário
--   * contabil.layout_importacao passa a guardar também os layouts de extrato em PDF, TXT ou CSV
--     (Integrações › Importar › Extratos bancários): coluna tipo PLANILHA | EXTRATO. O nome é único
--     dentro de cada tipo. A limpeza do banco já apaga a tabela inteira.
-- =============================================================================

ALTER TABLE contabil.layout_importacao
    ADD COLUMN tipo VARCHAR(10) NOT NULL DEFAULT 'PLANILHA' CHECK (tipo IN ('PLANILHA', 'EXTRATO'));

DROP INDEX contabil.uq_layout_importacao_nome;
CREATE UNIQUE INDEX uq_layout_importacao_nome ON contabil.layout_importacao (tipo, lower(nome));
