-- =============================================================================
-- Vorticis Contábil — Migração 020: plano de centros de custo hierárquico
--   Centros de custo passam a formar um plano (como o de contas): grupos sintéticos e centros analíticos,
--   código estruturado por níveis (ex.: 1, 1.01, 1.01.001) conforme a máscara definida por empresa.
--   Compatível: os centros existentes viram analíticos, com o nível dado pelo código; os que já têm
--   subcentros e nunca receberam lançamento viram sintéticos. Sem máscara, vale o cadastro livre de antes.
-- =============================================================================

ALTER TABLE core.empresa ADD COLUMN mascara_centro_custo VARCHAR(40);

ALTER TABLE contabil.centro_custo
    ADD COLUMN tipo  CHAR(1)  NOT NULL DEFAULT 'A',
    ADD COLUMN nivel SMALLINT NOT NULL DEFAULT 1;
ALTER TABLE contabil.centro_custo
    ADD CONSTRAINT ck_ccusto_tipo CHECK (tipo IN ('S', 'A')),
    ADD CONSTRAINT ck_ccusto_nivel CHECK (nivel >= 1);

UPDATE contabil.centro_custo
   SET nivel = array_length(string_to_array(codigo, '.'), 1);

UPDATE contabil.centro_custo c SET tipo = 'S'
 WHERE EXISTS (SELECT 1 FROM contabil.centro_custo f WHERE f.centro_pai_id = c.id)
   AND NOT EXISTS (SELECT 1 FROM contabil.partida p WHERE p.centro_custo_id = c.id);

CREATE INDEX ix_ccusto_pai ON contabil.centro_custo(centro_pai_id);

-- R2 (redefinida): além das regras da conta, o centro de custo da partida precisa ser analítico e da empresa.
CREATE OR REPLACE FUNCTION contabil.fn_valida_partida()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_lanc  contabil.lancamento%ROWTYPE;
    v_conta contabil.plano_contas%ROWTYPE;
    v_cc    contabil.centro_custo%ROWTYPE;
BEGIN
    SELECT * INTO v_lanc  FROM contabil.lancamento  WHERE id = NEW.lancamento_id;
    SELECT * INTO v_conta FROM contabil.plano_contas WHERE id = NEW.conta_id;

    IF v_conta.empresa_id <> v_lanc.empresa_id THEN
        RAISE EXCEPTION 'Conta % não pertence à empresa do lançamento', v_conta.codigo;
    END IF;
    IF v_conta.tipo <> 'A' THEN
        RAISE EXCEPTION 'Conta % é sintética e não aceita lançamentos', v_conta.codigo;
    END IF;
    IF NOT v_conta.ativa THEN
        RAISE EXCEPTION 'Conta % está inativa', v_conta.codigo;
    END IF;
    IF v_conta.data_inclusao > v_lanc.data_lancamento THEN
        RAISE EXCEPTION 'Conta % incluída em % — posterior à data do lançamento %',
              v_conta.codigo, v_conta.data_inclusao, v_lanc.data_lancamento;
    END IF;
    IF v_conta.exige_centro_custo AND NEW.centro_custo_id IS NULL THEN
        RAISE EXCEPTION 'Conta % exige centro de custo', v_conta.codigo;
    END IF;
    IF v_conta.exige_participante AND NEW.participante_id IS NULL THEN
        RAISE EXCEPTION 'Conta % exige participante', v_conta.codigo;
    END IF;
    IF NEW.centro_custo_id IS NOT NULL THEN
        SELECT * INTO v_cc FROM contabil.centro_custo WHERE id = NEW.centro_custo_id;
        IF v_cc.empresa_id <> v_lanc.empresa_id THEN
            RAISE EXCEPTION 'Centro de custo % não pertence à empresa do lançamento', v_cc.codigo;
        END IF;
        IF v_cc.tipo <> 'A' THEN
            RAISE EXCEPTION 'Centro de custo % é sintético e não aceita lançamentos', v_cc.codigo;
        END IF;
    END IF;
    RETURN NEW;
END $$;
