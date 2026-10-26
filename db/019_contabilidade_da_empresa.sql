-- =============================================================================
-- Vorticis Contábil — Migração 019: configurações contábeis da empresa e grupo dos participantes
--   * core.empresa guarda o modelo de plano de contas escolhido, a máscara dos códigos, o início da
--     escrituração, o uso de centro de custo, o porte (ECD 0000.IND_GRANDE_PORTE) e as contas
--     padrão do encerramento (apuração do resultado, lucros e prejuízos acumulados), por código.
--     A forma de apuração do IRPJ e o regime de caixa continuam em core.empresa_regime.
--   * contabil.plano_contas.grupo_participantes_id: conta que exige participante indica o grupo
--     (conta sintética) em que as contas dos participantes (clientes/fornecedores) são alocadas.
-- =============================================================================

ALTER TABLE core.empresa
    ADD COLUMN plano_modelo        VARCHAR(60),          -- id do modelo de plano aplicado (NULL = plano próprio/importado)
    ADD COLUMN mascara_plano       VARCHAR(30),          -- ex.: 9.9.99.999
    ADD COLUMN inicio_escrituracao DATE,                 -- data de início da escrituração contábil na empresa
    ADD COLUMN usa_centro_custo    BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN grande_porte        BOOLEAN NOT NULL DEFAULT FALSE,   -- ECD 0000.IND_GRANDE_PORTE (Lei 11.638/07, art. 3º)
    ADD COLUMN conta_apuracao      VARCHAR(30),          -- código da conta de apuração do resultado
    ADD COLUMN conta_lucros        VARCHAR(30),          -- código de lucros (ou superávit) acumulados
    ADD COLUMN conta_prejuizos     VARCHAR(30);          -- código de prejuízos (ou déficit) acumulados

-- A empresa que já tinha o plano modelo original fica registrada com ele.
UPDATE core.empresa e
   SET plano_modelo = 'basico'
 WHERE EXISTS (SELECT 1 FROM contabil.plano_contas c WHERE c.empresa_id = e.id AND c.codigo = '5.1.01.001');

-- Quem já usava centro de custo continua usando.
UPDATE core.empresa e
   SET usa_centro_custo = TRUE
 WHERE EXISTS (SELECT 1 FROM contabil.centro_custo cc WHERE cc.empresa_id = e.id)
    OR EXISTS (SELECT 1 FROM contabil.plano_contas c WHERE c.empresa_id = e.id AND c.exige_centro_custo);

ALTER TABLE contabil.plano_contas
    ADD COLUMN grupo_participantes_id BIGINT REFERENCES contabil.plano_contas(id) ON DELETE SET NULL;

-- Contas que já exigiam participante: o grupo passa a ser a conta superior (sintética).
UPDATE contabil.plano_contas
   SET grupo_participantes_id = conta_pai_id
 WHERE exige_participante AND conta_pai_id IS NOT NULL;

-- O grupo é uma conta sintética da mesma empresa.
CREATE OR REPLACE FUNCTION contabil.fn_valida_grupo_participantes()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.grupo_participantes_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM contabil.plano_contas g
         WHERE g.id = NEW.grupo_participantes_id AND g.empresa_id = NEW.empresa_id AND g.tipo = 'S') THEN
        RAISE EXCEPTION 'O grupo dos participantes da conta % deve ser uma conta sintética da mesma empresa', NEW.codigo;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_grupo_participantes BEFORE INSERT OR UPDATE OF grupo_participantes_id ON contabil.plano_contas
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_valida_grupo_participantes();
