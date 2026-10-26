-- =============================================================================
-- Perseus Contábil — Migração 017: plano de contas com participantes
--   A empresa pode optar por controlar clientes e fornecedores como participantes dentro da
--   conta contábil (ex.: 1.1.02.001 Clientes): os lançamentos nessa conta levam o participante
--   e o balancete mostra a soma na conta, com detalhamento por participante opcional.
-- =============================================================================

ALTER TABLE core.empresa
    ADD COLUMN usa_participantes BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE core.participante
    ADD COLUMN controle_conta BOOLEAN NOT NULL DEFAULT FALSE;   -- alocado como participante na conta contábil
