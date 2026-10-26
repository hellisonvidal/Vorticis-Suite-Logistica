-- =============================================================================
-- Frota: contabilização das diárias pagas (on-line e em lote)
--   * Nova origem de lançamento FROTA.
--   * frota.configuracao: conta de despesa de diárias e conta de crédito (contas a pagar ou caixa).
--   * frota.diaria.lancamento_id: lançamento contábil gerado pelo pagamento da diária.
-- =============================================================================
ALTER TYPE contabil.origem_lancamento ADD VALUE IF NOT EXISTS 'FROTA';

ALTER TABLE frota.configuracao
    ADD COLUMN conta_diaria_despesa VARCHAR(30),
    ADD COLUMN conta_diaria_credito VARCHAR(30);

ALTER TABLE frota.diaria
    ADD COLUMN lancamento_id BIGINT REFERENCES contabil.lancamento(id) ON DELETE SET NULL;
