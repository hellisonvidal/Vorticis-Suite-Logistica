-- =============================================================================
-- 046 - Fiscal: campos de escrituração editáveis das notas (inclusive as importadas de XML).
-- Os dados do XML original (valores, chave, itens) permanecem imutáveis; aqui ficam apenas
-- o CFOP de escrituração do item (ex.: CFOP de entrada 1.102 para um 5.102 do fornecedor),
-- as contas de contabilização escolhidas para a nota e as observações.
-- =============================================================================

ALTER TABLE fiscal.documento
    ADD COLUMN observacoes   TEXT,
    ADD COLUMN conta_debito  VARCHAR(30),      -- sobrepõe a conta da regra de contabilização (código da conta analítica)
    ADD COLUMN conta_credito VARCHAR(30);

ALTER TABLE fiscal.documento_item
    ADD COLUMN cfop_escrituracao VARCHAR(10);  -- nulo = vale o CFOP do documento (cfop)
