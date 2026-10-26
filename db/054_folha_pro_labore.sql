-- =============================================================================
-- 054 — Folha: pró-labore dos sócios
--   * Configuração por sócio (core.socio): retira ou não pró-labore, valor mensal, vigência, dependentes para IRRF
--     e INSS já descontado em outras fontes (limita ao teto).
--   * Cálculo por competência: INSS do contribuinte individual (11%, limitado ao teto), IRRF e CPP patronal (20%,
--     zero no Simples Nacional fora do anexo IV). A competência fechada bloqueia inclusões, alterações e exclusões.
--   * Alíquotas em core.parametro_legal (FOLHA_PRO_LABORE), nunca no código.
-- =============================================================================

CREATE TABLE folha.pro_labore (
    id                 BIGSERIAL PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id),
    socio_id           BIGINT        NOT NULL REFERENCES core.socio(id) ON DELETE CASCADE,
    retira             BOOLEAN       NOT NULL DEFAULT TRUE,
    valor              NUMERIC(15,2) NOT NULL DEFAULT 0,
    inicio             DATE          NOT NULL,
    fim                DATE,
    dependentes_irrf   SMALLINT      NOT NULL DEFAULT 0,
    inss_outras_fontes NUMERIC(15,2) NOT NULL DEFAULT 0,
    observacao         VARCHAR(500),
    CONSTRAINT uq_pro_labore_socio UNIQUE (empresa_id, socio_id),
    CONSTRAINT ck_pro_labore_valor CHECK (valor >= 0 AND inss_outras_fontes >= 0 AND dependentes_irrf >= 0),
    CONSTRAINT ck_pro_labore_retira CHECK (NOT retira OR valor > 0),
    CONSTRAINT ck_pro_labore_fim CHECK (fim IS NULL OR fim >= inicio)
);

CREATE TABLE folha.pro_labore_calculo (
    id             BIGSERIAL PRIMARY KEY,
    empresa_id     BIGINT        NOT NULL REFERENCES core.empresa(id),
    socio_id       BIGINT        NOT NULL REFERENCES core.socio(id) ON DELETE CASCADE,
    competencia    DATE          NOT NULL,
    bruto          NUMERIC(15,2) NOT NULL,
    inss           NUMERIC(15,2) NOT NULL,
    irrf           NUMERIC(15,2) NOT NULL,
    liquido        NUMERIC(15,2) NOT NULL,
    inss_patronal  NUMERIC(15,2) NOT NULL,
    memoria        TEXT          NOT NULL,
    calculado_em   TIMESTAMPTZ   NOT NULL DEFAULT now(),
    calculado_por  BIGINT        REFERENCES core.usuario(id),
    CONSTRAINT uq_pro_labore_calculo UNIQUE (empresa_id, socio_id, competencia),
    CONSTRAINT ck_pro_labore_calc_dia1 CHECK (competencia = date_trunc('month', competencia)::date)
);
CREATE INDEX ix_pro_labore_calculo_comp ON folha.pro_labore_calculo (empresa_id, competencia);

-- Competência fechada: o banco também recusa mexer no cálculo do pró-labore.
CREATE FUNCTION folha.fn_pro_labore_competencia_aberta() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_empresa BIGINT := COALESCE(NEW.empresa_id, OLD.empresa_id);
    v_comp    DATE   := COALESCE(NEW.competencia, OLD.competencia);
BEGIN
    IF EXISTS (SELECT 1 FROM folha.competencia c
                WHERE c.empresa_id = v_empresa AND c.competencia = v_comp AND c.status = 'FECHADA') THEN
        RAISE EXCEPTION 'A competência % da folha está fechada. Reabra-a para alterar o pró-labore.', to_char(v_comp, 'MM/YYYY')
            USING ERRCODE = 'P0001';
    END IF;
    RETURN COALESCE(NEW, OLD);
END $$;

CREATE TRIGGER trg_pro_labore_competencia BEFORE INSERT OR UPDATE OR DELETE ON folha.pro_labore_calculo
    FOR EACH ROW EXECUTE FUNCTION folha.fn_pro_labore_competencia_aberta();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.pro_labore FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.pro_labore_calculo FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.parametro_legal (chave, vigencia_inicio, vigencia_fim, valor, fundamento_legal) VALUES
('FOLHA_PRO_LABORE', '1999-11-29', NULL, '{"aliquotaInss":11.0,"aliquotaPatronal":20.0}',
 'Lei nº 8.212/1991, art. 21, § 2º (11% do contribuinte individual) e art. 22, III (20% patronal sobre a remuneração do contribuinte individual)');
