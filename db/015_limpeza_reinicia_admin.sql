-- =============================================================================
-- Perseus Contábil — Migração 015: a limpeza do banco recomeça do zero
--   Além das empresas e cadastros, a limpeza apaga usuários e perfis personalizados e
--   recria o administrador padrão (login ADMIN; o hash da senha vem do aplicativo).
--   O ADMIN deve trocar a senha no primeiro acesso.
-- =============================================================================

DROP FUNCTION IF EXISTS core.fn_limpar_banco();

CREATE OR REPLACE FUNCTION core.fn_limpar_banco(p_hash_admin TEXT)
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE
    v_empresa BIGINT;
    v_qtd     INTEGER := 0;
BEGIN
    FOR v_empresa IN SELECT id FROM core.empresa ORDER BY id LOOP
        PERFORM core.fn_excluir_empresa(v_empresa);
        v_qtd := v_qtd + 1;
    END LOOP;
    DELETE FROM core.contador;
    DELETE FROM core.escritorio_contabil;
    DELETE FROM contabil.plano_referencial;
    DELETE FROM contabil.plano_referencial_importacao;
    DELETE FROM contabil.layout_importacao;

    -- Usuários: todos saem; fica só o administrador padrão. Ele é criado antes (com o login liberado)
    -- para que a regra "sempre um administrador ativo" continue valendo durante a exclusão dos demais.
    DELETE FROM core.registro_acesso;
    DELETE FROM core.usuario_empresa;
    UPDATE core.usuario SET login = '#excluir' || id;
    INSERT INTO core.usuario (login, nome, senha_hash, administrador, perfil_id, deve_trocar_senha)
    SELECT 'ADMIN', 'Administrador', p_hash_admin, TRUE, (SELECT id FROM core.perfil WHERE administrador), TRUE;
    DELETE FROM core.usuario WHERE login LIKE '#excluir%';
    DELETE FROM core.perfil WHERE NOT padrao;
    DELETE FROM core.configuracao_sistema WHERE chave = 'senha_administrador';   -- volta à senha padrão
    INSERT INTO core.configuracao_sistema (chave, valor) VALUES ('perfis_configurados', 'S')
    ON CONFLICT (chave) DO UPDATE SET valor = 'S', alterado_em = now(), alterado_por = NULL;
    RETURN v_qtd;
END $$;
