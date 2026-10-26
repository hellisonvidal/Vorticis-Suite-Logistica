-- =============================================================================
-- Perseus Contábil — Migração 006: plano de contas referencial da RFB
--   * Tabelas referenciais importadas pelo administrador (arquivo oficial),
--     por tipo de plano e ano, com registro da importação
--   * Exercício indica qual plano referencial a empresa usa (ECD/ECF)
--   * Mapeamento conta → referencial passa a ser auditado
-- =============================================================================

ALTER TABLE contabil.plano_referencial
    ADD COLUMN dt_ini DATE,
    ADD COLUMN dt_fim DATE;
CREATE INDEX ix_plano_ref_pai ON contabil.plano_referencial (cod_plan_ref, ano_vigencia, codigo_pai);

CREATE TABLE contabil.plano_referencial_importacao (
    id            BIGSERIAL PRIMARY KEY,
    cod_plan_ref  VARCHAR(4)  NOT NULL,
    ano_vigencia  SMALLINT    NOT NULL,
    arquivo       VARCHAR(260) NOT NULL,
    sha256        CHAR(64)    NOT NULL,
    contas        INTEGER     NOT NULL,
    importado_em  TIMESTAMPTZ NOT NULL DEFAULT now(),
    importado_por BIGINT      REFERENCES core.usuario(id)
);

-- Plano referencial usado pela empresa no exercício (tipo da tabela: 1 = PJ em geral – Lucro Real, 2 = Lucro Presumido...).
ALTER TABLE contabil.exercicio ADD COLUMN cod_plan_ref VARCHAR(4);

-- Só se vincula a conta referencial analítica; o vínculo de uma conta é do ano da tabela referencial.
CREATE OR REPLACE FUNCTION contabil.fn_valida_mapeamento()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_ref contabil.plano_referencial%ROWTYPE;
    v_conta contabil.plano_contas%ROWTYPE;
BEGIN
    SELECT * INTO v_ref FROM contabil.plano_referencial WHERE id = NEW.plano_referencial_id;
    SELECT * INTO v_conta FROM contabil.plano_contas WHERE id = NEW.conta_id;
    IF v_ref.tipo <> 'A' THEN
        RAISE EXCEPTION 'A conta referencial % é sintética; vincule a uma analítica', v_ref.codigo;
    END IF;
    IF v_conta.tipo <> 'A' THEN
        RAISE EXCEPTION 'Só contas analíticas são vinculadas ao plano referencial (conta %)', v_conta.codigo;
    END IF;
    IF v_ref.ano_vigencia <> NEW.ano_vigencia THEN
        RAISE EXCEPTION 'A conta referencial % é da tabela de %, não de %', v_ref.codigo, v_ref.ano_vigencia, NEW.ano_vigencia;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_valida_mapeamento BEFORE INSERT OR UPDATE ON contabil.mapeamento_referencial
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_valida_mapeamento();

-- empresa_id para a trilha de auditoria (fn_auditoria lê empresa_id da linha)
ALTER TABLE contabil.mapeamento_referencial ADD COLUMN empresa_id BIGINT REFERENCES core.empresa(id);
UPDATE contabil.mapeamento_referencial m SET empresa_id = c.empresa_id FROM contabil.plano_contas c WHERE c.id = m.conta_id;
ALTER TABLE contabil.mapeamento_referencial ALTER COLUMN empresa_id SET NOT NULL;

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON contabil.mapeamento_referencial
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT id, 'PLANO_REFERENCIAL_MAPEAR' FROM core.perfil WHERE padrao AND nome = 'Contador'
ON CONFLICT DO NOTHING;
