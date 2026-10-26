-- =============================================================================
-- Perseus Contábil — Migração 012
--   * Código reduzido das contas analíticas: sequência numérica por empresa, usada
--     como atalho na digitação dos lançamentos (atribuída automaticamente).
--   * Clientes e fornecedores: dados cadastrais completos em core.participante
--     (ECD 0150), com conta contábil opcional.
-- =============================================================================

-- Contas analíticas existentes recebem a sequência na ordem do código.
WITH n AS (
    SELECT id, row_number() OVER (PARTITION BY empresa_id ORDER BY codigo) AS rn
      FROM contabil.plano_contas WHERE tipo = 'A'
)
UPDATE contabil.plano_contas c SET codigo_reduzido = n.rn FROM n WHERE n.id = c.id;
UPDATE contabil.plano_contas SET codigo_reduzido = NULL WHERE tipo = 'S';

CREATE UNIQUE INDEX uq_conta_reduzido ON contabil.plano_contas (empresa_id, codigo_reduzido) WHERE codigo_reduzido IS NOT NULL;
ALTER TABLE contabil.plano_contas ADD CONSTRAINT ck_reduzido_positivo CHECK (codigo_reduzido IS NULL OR codigo_reduzido > 0);

-- Analítica nova (ou que voltou a ser analítica) sem reduzido recebe o próximo número; sintética não tem reduzido.
CREATE OR REPLACE FUNCTION contabil.fn_codigo_reduzido()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.tipo = 'S' THEN
        NEW.codigo_reduzido := NULL;
    ELSIF NEW.codigo_reduzido IS NULL THEN
        PERFORM pg_advisory_xact_lock(hashtext('perseus_reduzido'), NEW.empresa_id::int);
        SELECT COALESCE(MAX(codigo_reduzido), 0) + 1 INTO NEW.codigo_reduzido
          FROM contabil.plano_contas WHERE empresa_id = NEW.empresa_id;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_codigo_reduzido BEFORE INSERT OR UPDATE OF tipo, codigo_reduzido ON contabil.plano_contas
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_codigo_reduzido();

-- Clientes e fornecedores
ALTER TABLE core.participante
    ADD COLUMN cliente             BOOLEAN      NOT NULL DEFAULT FALSE,
    ADD COLUMN fornecedor          BOOLEAN      NOT NULL DEFAULT FALSE,
    ADD COLUMN nome_fantasia       VARCHAR(150),
    ADD COLUMN inscricao_estadual  VARCHAR(20),
    ADD COLUMN municipio           VARCHAR(100),
    ADD COLUMN email               VARCHAR(150),
    ADD COLUMN telefone            VARCHAR(30),
    ADD COLUMN conta_id            BIGINT       REFERENCES contabil.plano_contas (id) ON DELETE SET NULL,
    ADD COLUMN ativo               BOOLEAN      NOT NULL DEFAULT TRUE,
    ADD COLUMN criado_em           TIMESTAMPTZ  NOT NULL DEFAULT now();
