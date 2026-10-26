-- =============================================================================
-- Perseus Contábil — Migração 009
--   * Perfil padrão "Administrador": todas as funções liberadas; quem o recebe é
--     administrador do sistema (usuários, perfis, empresas, backup, conexão).
--   * Perfil do usuário (core.usuario.perfil_id): informado no cadastro do usuário,
--     vale para todas as empresas, inclusive as cadastradas depois. O acesso por
--     empresa (core.usuario_empresa) continua permitindo exceções.
-- =============================================================================

ALTER TABLE core.perfil ADD COLUMN administrador BOOLEAN NOT NULL DEFAULT FALSE;
CREATE UNIQUE INDEX uq_perfil_administrador ON core.perfil (administrador) WHERE administrador;

INSERT INTO core.perfil (nome, descricao, padrao, administrador)
VALUES ('Administrador', 'Todas as funções do sistema, inclusive a administração (usuários, perfis, empresas, backup)', TRUE, TRUE)
ON CONFLICT ((lower(nome))) DO UPDATE SET padrao = TRUE, administrador = TRUE, descricao = EXCLUDED.descricao;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
 CROSS JOIN (VALUES
        ('LANCAMENTO_INCLUIR'), ('LANCAMENTO_ESTORNAR'), ('SALDOS_INICIAIS'), ('PERIODO_FECHAR'), ('PERIODO_REABRIR'),
        ('EXERCICIO_ENCERRAR'), ('PLANO_CONTAS_MANTER'), ('CENTRO_CUSTO_MANTER'), ('PLANO_REFERENCIAL_MAPEAR'),
        ('EMPRESA_RESPONSAVEIS_MANTER'), ('RELATORIO_BALANCETE'), ('RELATORIO_RAZAO'), ('RELATORIO_DIARIO'),
        ('RELATORIO_DEMONSTRACOES'), ('RELATORIO_EXPORTAR')
       ) AS x(permissao)
 WHERE p.administrador
ON CONFLICT DO NOTHING;

-- Perfil do usuário
ALTER TABLE core.usuario ADD COLUMN perfil_id BIGINT REFERENCES core.perfil (id);

-- Usuários existentes: administradores recebem o perfil Administrador; os demais, o perfil que já usam
-- (quando é o mesmo em todas as empresas). Os que variam por empresa ficam sem perfil até o administrador definir.
UPDATE core.usuario u SET perfil_id = (SELECT id FROM core.perfil WHERE administrador) WHERE u.administrador;
UPDATE core.usuario u
   SET perfil_id = x.perfil_id
  FROM (SELECT usuario_id, min(perfil_id) AS perfil_id
          FROM core.usuario_empresa GROUP BY usuario_id HAVING count(DISTINCT perfil_id) = 1) x
 WHERE x.usuario_id = u.id AND u.perfil_id IS NULL;

-- Empresa nova: cada usuário ativo com perfil recebe acesso com o seu perfil (o administrador pode alterar depois).
CREATE OR REPLACE FUNCTION core.fn_acesso_empresa_nova()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id)
    SELECT u.id, NEW.id, u.perfil_id FROM core.usuario u
     WHERE u.ativo AND u.perfil_id IS NOT NULL AND NOT u.administrador
    ON CONFLICT DO NOTHING;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_acesso_empresa_nova AFTER INSERT ON core.empresa
    FOR EACH ROW EXECUTE FUNCTION core.fn_acesso_empresa_nova();

-- Perfil em uso como perfil de usuário também não pode ser excluído.
CREATE OR REPLACE FUNCTION core.fn_protege_perfil()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.padrao THEN
        RAISE EXCEPTION 'O perfil "%" é padrão do sistema e não pode ser excluído (pode ser editado)', OLD.nome;
    END IF;
    IF EXISTS (SELECT 1 FROM core.usuario_empresa WHERE perfil_id = OLD.id)
       OR EXISTS (SELECT 1 FROM core.usuario WHERE perfil_id = OLD.id) THEN
        RAISE EXCEPTION 'O perfil "%" está atribuído a usuários; retire os acessos antes de excluí-lo', OLD.nome;
    END IF;
    RETURN OLD;
END $$;
