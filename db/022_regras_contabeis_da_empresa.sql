-- =============================================================================
-- Vorticis Contábil — Migração 022: regras de lançamento da empresa (aba Contabilidade)
--   * exige_documento: todo lançamento manual/importado precisa informar o nº do documento/arquivo de suporte
--     em cada partida (ECD I250.NUM_ARQ). Lançamentos de saldo inicial, encerramento e estorno ficam isentos.
--   * data_bloqueio: lançamentos com data até essa data (inclusive) são recusados — escrituração já
--     entregue/conferida. Ficam isentos os saldos iniciais e o lançamento de encerramento do exercício.
--   Os dois são conferidos em ValidadorLancamento (único caminho de gravação de lançamentos).
-- =============================================================================

ALTER TABLE core.empresa
    ADD COLUMN exige_documento BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN data_bloqueio   DATE;
