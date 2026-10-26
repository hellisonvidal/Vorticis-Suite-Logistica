-- =============================================================================
-- Vorticis Fiscal — Migração 036: digitação manual de notas fiscais e centro de custo no fiscal
--   * fiscal.documento.origem: XML (importado) ou DIGITADO (lançamento manual de entrada/saída). Nota digitada pode
--     não ter chave de acesso (chave_acesso passa a aceitar NULL; a chave continua única por empresa quando informada).
--   * Nota digitada é única por empresa, operação, modelo, série, número e participante.
--   * Centro de custo opcional no documento e nos itens (levado às partidas na contabilização).
--   * Produto e GTIN nos itens (vínculo com fiscal.produto, feito na importação e na digitação).
-- =============================================================================

ALTER TABLE fiscal.documento ALTER COLUMN chave_acesso DROP NOT NULL;

ALTER TABLE fiscal.documento
    ADD COLUMN origem          VARCHAR(10) NOT NULL DEFAULT 'XML' CHECK (origem IN ('XML', 'DIGITADO')),
    ADD COLUMN centro_custo_id BIGINT      REFERENCES contabil.centro_custo (id) ON DELETE SET NULL,
    ADD COLUMN atualizado_em   TIMESTAMPTZ,
    ADD CONSTRAINT ck_documento_chave CHECK (origem <> 'XML' OR chave_acesso IS NOT NULL);

CREATE UNIQUE INDEX uq_documento_digitado
    ON fiscal.documento (empresa_id, tipo_operacao, modelo, serie, numero, COALESCE(participante_id, 0))
 WHERE origem = 'DIGITADO';

ALTER TABLE fiscal.documento_item
    ADD COLUMN produto_id      BIGINT      REFERENCES fiscal.produto (id) ON DELETE SET NULL,
    ADD COLUMN gtin            VARCHAR(14),
    ADD COLUMN centro_custo_id BIGINT      REFERENCES contabil.centro_custo (id) ON DELETE SET NULL;
CREATE INDEX ix_documento_item_produto ON fiscal.documento_item (produto_id);
