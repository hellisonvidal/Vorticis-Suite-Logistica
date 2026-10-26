-- Código de produto passa a ocupar o menor número livre (depende de fiscal.produto, migração 035).

-- Próximo código de produto da empresa (menor livre), com trava contra concorrência.
CREATE OR REPLACE FUNCTION fiscal.fn_proximo_produto(p_empresa BIGINT)
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE v INTEGER;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext('vorticis_produto'), p_empresa::int);
    SELECT s.n INTO v
      FROM generate_series(1, (SELECT COALESCE(MAX(codigo), 0) + 1 FROM fiscal.produto WHERE empresa_id = p_empresa)) s(n)
     WHERE NOT EXISTS (SELECT 1 FROM fiscal.produto p WHERE p.empresa_id = p_empresa AND p.codigo = s.n)
     ORDER BY s.n LIMIT 1;
    RETURN v;
END $$;
