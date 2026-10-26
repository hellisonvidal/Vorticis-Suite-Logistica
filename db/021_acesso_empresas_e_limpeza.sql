-- =============================================================================
-- Vorticis — Migração 021: empresas liberadas por usuário e limpeza do banco sem usuários
--
--   * core.usuario.todas_empresas: o usuário (não administrador) acessa todas as empresas, inclusive as
--     cadastradas depois. Fica FALSE quando o administrador desmarca alguma empresa no cadastro do usuário;
--     então as empresas novas NÃO são liberadas automaticamente a ele (a liberação é feita no cadastro).
--     O acesso em si continua em core.usuario_empresa (uma linha por empresa liberada, com o perfil do usuário
--     naquela empresa); administradores veem todas as empresas.
--   * A limpeza do banco passa a devolver o banco ao estado de primeira instalação: apaga também TODOS os
--     usuários (sem recriar o ADMIN). No próximo acesso vale de novo o usuário de implantação (vorticis /
--     vorticis), que só serve para cadastrar o primeiro administrador. A função não recebe mais o hash de senha.
-- =============================================================================

ALTER TABLE core.usuario ADD COLUMN todas_empresas BOOLEAN NOT NULL DEFAULT TRUE;

-- Quem hoje não tem acesso a alguma empresa existente já é um usuário restrito.
UPDATE core.usuario u
   SET todas_empresas = FALSE
 WHERE NOT u.administrador
   AND EXISTS (SELECT 1 FROM core.empresa e
                WHERE NOT EXISTS (SELECT 1 FROM core.usuario_empresa ue
                                   WHERE ue.usuario_id = u.id AND ue.empresa_id = e.id));

-- Empresa nova: libera só para os usuários que acessam todas as empresas (o perfil é o do usuário).
CREATE OR REPLACE FUNCTION core.fn_acesso_empresa_nova()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id)
    SELECT u.id, NEW.id, u.perfil_id FROM core.usuario u
     WHERE u.ativo AND u.perfil_id IS NOT NULL AND NOT u.administrador AND u.todas_empresas
    ON CONFLICT DO NOTHING;
    RETURN NEW;
END $$;

-- Limpeza do banco: empresas, cadastros, certificados, usuários e perfis personalizados.
DROP FUNCTION IF EXISTS core.fn_limpar_banco(TEXT);

CREATE OR REPLACE FUNCTION core.fn_limpar_banco()
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE
    v_empresa BIGINT;
    v_qtd     INTEGER := 0;
BEGIN
    FOR v_empresa IN SELECT id FROM core.empresa ORDER BY id LOOP
        PERFORM core.fn_excluir_empresa(v_empresa);
        v_qtd := v_qtd + 1;
    END LOOP;
    DELETE FROM core.certificado_digital;
    DELETE FROM core.contador;
    DELETE FROM core.escritorio_contabil;
    DELETE FROM contabil.plano_referencial;
    DELETE FROM contabil.plano_referencial_importacao;
    DELETE FROM contabil.layout_importacao;

    -- Todos os usuários saem. A regra "sempre um administrador ativo" é suspensa só durante esta exclusão
    -- (na mesma transação); depois dela o banco está como numa instalação nova.
    DELETE FROM core.registro_acesso;
    DELETE FROM core.usuario_empresa;
    ALTER TABLE core.usuario DISABLE TRIGGER trg_protege_ultimo_admin;
    DELETE FROM core.usuario;
    ALTER TABLE core.usuario ENABLE TRIGGER trg_protege_ultimo_admin;

    DELETE FROM core.perfil WHERE NOT padrao;
    DELETE FROM core.configuracao_sistema WHERE chave IN ('senha_administrador', 'perfis_configurados');
    RETURN v_qtd;
END $$;
