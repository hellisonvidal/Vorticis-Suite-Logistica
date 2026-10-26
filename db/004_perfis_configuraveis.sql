-- =============================================================================
-- Perseus Contábil — Migração 004: perfis de acesso configuráveis
--   O administrador cria perfis e escolhe, função a função, o que cada um libera.
--   Os perfis fixos da migração 003 (CONTADOR, ASSISTENTE, CONSULTA) viram perfis
--   padrão editáveis, e os acessos existentes são migrados.
-- =============================================================================

CREATE TABLE core.perfil (
    id          BIGSERIAL PRIMARY KEY,
    nome        VARCHAR(60)  NOT NULL,
    descricao   VARCHAR(250),
    padrao      BOOLEAN      NOT NULL DEFAULT FALSE,   -- criado pelo sistema (pode ser editado, não excluído)
    criado_em   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uq_perfil_nome ON core.perfil (lower(nome));

-- Códigos definidos em Perseus.Domain.Seguranca.Permissoes.Catalogo.
CREATE TABLE core.perfil_permissao (
    perfil_id   BIGINT      NOT NULL REFERENCES core.perfil(id) ON DELETE CASCADE,
    permissao   VARCHAR(60) NOT NULL,
    PRIMARY KEY (perfil_id, permissao)
);

INSERT INTO core.perfil (nome, descricao, padrao) VALUES
    ('Contador',   'Responsável técnico: todas as funções contábeis da empresa', TRUE),
    ('Assistente', 'Lança e estorna; não fecha nem reabre períodos', TRUE),
    ('Consulta',   'Somente relatórios', TRUE);

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES
        ('Contador', 'LANCAMENTO_INCLUIR'), ('Contador', 'LANCAMENTO_ESTORNAR'), ('Contador', 'SALDOS_INICIAIS'),
        ('Contador', 'PERIODO_FECHAR'), ('Contador', 'PERIODO_REABRIR'), ('Contador', 'PLANO_CONTAS_MANTER'),
        ('Contador', 'CENTRO_CUSTO_MANTER'), ('Contador', 'RELATORIO_BALANCETE'), ('Contador', 'RELATORIO_RAZAO'),
        ('Contador', 'RELATORIO_DIARIO'), ('Contador', 'RELATORIO_EXPORTAR'),
        ('Assistente', 'LANCAMENTO_INCLUIR'), ('Assistente', 'LANCAMENTO_ESTORNAR'), ('Assistente', 'RELATORIO_BALANCETE'),
        ('Assistente', 'RELATORIO_RAZAO'), ('Assistente', 'RELATORIO_DIARIO'), ('Assistente', 'RELATORIO_EXPORTAR'),
        ('Consulta', 'RELATORIO_BALANCETE'), ('Consulta', 'RELATORIO_RAZAO'), ('Consulta', 'RELATORIO_DIARIO')
       ) AS x(nome, permissao) ON x.nome = p.nome;

-- Acesso usuário × empresa passa a apontar para o perfil cadastrado.
ALTER TABLE core.usuario_empresa ADD COLUMN perfil_id BIGINT REFERENCES core.perfil(id);
UPDATE core.usuario_empresa ue
   SET perfil_id = p.id
  FROM core.perfil p
 WHERE upper(p.nome) = ue.perfil;
ALTER TABLE core.usuario_empresa ALTER COLUMN perfil_id SET NOT NULL;
ALTER TABLE core.usuario_empresa DROP CONSTRAINT ck_perfil;
ALTER TABLE core.usuario_empresa DROP COLUMN perfil;

-- Perfil em uso não pode ser excluído (o FK acima já impede; mensagem amigável aqui).
CREATE OR REPLACE FUNCTION core.fn_protege_perfil()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.padrao THEN
        RAISE EXCEPTION 'O perfil "%" é padrão do sistema e não pode ser excluído (pode ser editado)', OLD.nome;
    END IF;
    IF EXISTS (SELECT 1 FROM core.usuario_empresa WHERE perfil_id = OLD.id) THEN
        RAISE EXCEPTION 'O perfil "%" está atribuído a usuários; retire os acessos antes de excluí-lo', OLD.nome;
    END IF;
    RETURN OLD;
END $$;

CREATE TRIGGER trg_protege_perfil BEFORE DELETE ON core.perfil
    FOR EACH ROW EXECUTE FUNCTION core.fn_protege_perfil();

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.perfil
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR DELETE ON core.perfil_permissao
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON contabil.centro_custo
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
