-- Sequências de cadastros passam a reaproveitar o menor número livre (lacunas deixadas por exclusões).
-- Só afeta códigos exibidos ao usuário; chaves internas (id) e a numeração de lançamentos efetivados não mudam.

-- Código reduzido do plano de contas: menor livre por empresa (substitui a regra MAX+1 da migração 018).
CREATE OR REPLACE FUNCTION contabil.fn_codigo_reduzido()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.tipo = 'S' THEN
        NEW.codigo_reduzido := NULL;
    ELSIF NEW.codigo_reduzido IS NULL THEN
        PERFORM pg_advisory_xact_lock(hashtext('vorticis_reduzido'), NEW.empresa_id::int);
        SELECT s.n INTO NEW.codigo_reduzido
          FROM generate_series(1, (SELECT COALESCE(MAX(codigo_reduzido), 0) + 1 FROM contabil.plano_contas WHERE empresa_id = NEW.empresa_id)) s(n)
         WHERE NOT EXISTS (SELECT 1 FROM contabil.plano_contas p WHERE p.empresa_id = NEW.empresa_id AND p.codigo_reduzido = s.n)
         ORDER BY s.n LIMIT 1;
    END IF;
    RETURN NEW;
END $$;

-- Próximo código numérico de participante da empresa (menor livre), com trava.
CREATE OR REPLACE FUNCTION core.fn_proximo_participante(p_empresa BIGINT)
RETURNS BIGINT LANGUAGE plpgsql AS $$
DECLARE v BIGINT;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('vorticis_participante'), p_empresa::int);
    SELECT s.n INTO v
      FROM generate_series(1::bigint, (SELECT COALESCE(MAX(codigo::bigint), 0) + 1 FROM core.participante WHERE empresa_id = p_empresa AND codigo ~ '^[0-9]{1,15}$')) s(n)
     WHERE NOT EXISTS (SELECT 1 FROM core.participante p WHERE p.empresa_id = p_empresa AND p.codigo ~ '^[0-9]{1,15}$' AND p.codigo::bigint = s.n)
     ORDER BY s.n LIMIT 1;
    RETURN v;
END $$;
