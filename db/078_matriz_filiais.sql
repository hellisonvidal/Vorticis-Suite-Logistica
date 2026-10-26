-- =============================================================================
-- 078 — Matriz e filiais: a filial é uma empresa com escrituração própria, ligada à matriz (mesma raiz do CNPJ, os 8 primeiros caracteres).
--   Os relatórios contábeis podem ser tirados de cada empresa (individual) ou do grupo (agrupado: somado ou lado a lado).
--   Regras: a empresa não é filial de si mesma, a matriz não é filial de outra e a filial não tem filiais.
-- =============================================================================
ALTER TABLE core.empresa ADD COLUMN matriz_id BIGINT REFERENCES core.empresa(id) ON DELETE SET NULL,
                         ADD CONSTRAINT ck_empresa_matriz_propria CHECK (matriz_id IS NULL OR matriz_id <> id);
CREATE INDEX ix_empresa_matriz ON core.empresa (matriz_id);

CREATE OR REPLACE FUNCTION core.fn_valida_matriz() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_matriz core.empresa%ROWTYPE;
BEGIN
    IF NEW.matriz_id IS NULL THEN RETURN NEW; END IF;
    SELECT * INTO v_matriz FROM core.empresa WHERE id = NEW.matriz_id;
    IF v_matriz.matriz_id IS NOT NULL THEN
        RAISE EXCEPTION 'A empresa escolhida como matriz já é filial de outra.';
    END IF;
    IF EXISTS (SELECT 1 FROM core.empresa WHERE matriz_id = NEW.id) THEN
        RAISE EXCEPTION 'Esta empresa tem filiais: ela não pode ser filial de outra.';
    END IF;
    IF left(v_matriz.cnpj, 8) <> left(NEW.cnpj, 8) THEN
        RAISE EXCEPTION 'A filial precisa ter a mesma raiz de CNPJ (8 primeiros dígitos) da matriz.';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_valida_matriz BEFORE INSERT OR UPDATE OF matriz_id, cnpj ON core.empresa
    FOR EACH ROW EXECUTE FUNCTION core.fn_valida_matriz();
