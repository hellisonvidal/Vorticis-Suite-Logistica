-- =============================================================================
-- Perseus — Migração 003
--   * Usuários: administrador, bloqueio por tentativas, troca obrigatória de senha
--   * Perfis por empresa e registro de acessos (login)
--   * Endereço/contato da empresa e origem dos dados (consulta ao CNPJ)
-- =============================================================================

ALTER TABLE core.usuario
    ADD COLUMN administrador     BOOLEAN     NOT NULL DEFAULT FALSE,
    ADD COLUMN deve_trocar_senha BOOLEAN     NOT NULL DEFAULT FALSE,
    ADD COLUMN tentativas_falhas SMALLINT    NOT NULL DEFAULT 0,
    ADD COLUMN bloqueado_ate     TIMESTAMPTZ,
    ADD COLUMN ultimo_acesso     TIMESTAMPTZ,
    ADD COLUMN criado_em         TIMESTAMPTZ NOT NULL DEFAULT now();

-- Login sem distinção de maiúsculas ("Maria" e "maria" são o mesmo usuário).
CREATE UNIQUE INDEX uq_usuario_login_ci ON core.usuario (lower(login));

-- Perfis por empresa. O administrador é global (core.usuario.administrador).
--   CONTADOR   — tudo na empresa, inclusive fechar/reabrir períodos
--   ASSISTENTE — lança e estorna; não fecha períodos
--   CONSULTA   — somente relatórios
ALTER TABLE core.usuario_empresa
    ADD CONSTRAINT ck_perfil CHECK (perfil IN ('CONTADOR', 'ASSISTENTE', 'CONSULTA'));

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.usuario_empresa
    FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

-- Sempre deve existir ao menos um administrador ativo (evita trancar todos fora do sistema).
CREATE OR REPLACE FUNCTION core.fn_protege_ultimo_admin()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.administrador AND OLD.ativo
       AND (TG_OP = 'DELETE' OR NOT (NEW.administrador AND NEW.ativo))
       AND NOT EXISTS (SELECT 1 FROM core.usuario u
                        WHERE u.id <> OLD.id AND u.administrador AND u.ativo) THEN
        RAISE EXCEPTION 'É preciso manter ao menos um administrador ativo';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END $$;

CREATE TRIGGER trg_protege_ultimo_admin BEFORE UPDATE OR DELETE ON core.usuario
    FOR EACH ROW EXECUTE FUNCTION core.fn_protege_ultimo_admin();

-- Tentativas de login (sucesso e falha). Não guarda senha.
CREATE TABLE core.registro_acesso (
    id          BIGSERIAL PRIMARY KEY,
    login       VARCHAR(60)  NOT NULL,
    usuario_id  BIGINT       REFERENCES core.usuario(id),
    ocorrido_em TIMESTAMPTZ  NOT NULL DEFAULT clock_timestamp(),
    sucesso     BOOLEAN      NOT NULL,
    motivo      VARCHAR(60),
    estacao     VARCHAR(100)
);
CREATE INDEX ix_registro_acesso_usuario ON core.registro_acesso (usuario_id, ocorrido_em);

-- Endereço e contato da empresa + rastreabilidade da consulta cadastral.
ALTER TABLE core.empresa
    ADD COLUMN cep                CHAR(8),
    ADD COLUMN logradouro         VARCHAR(150),
    ADD COLUMN numero             VARCHAR(20),
    ADD COLUMN complemento        VARCHAR(150),
    ADD COLUMN bairro             VARCHAR(100),
    ADD COLUMN municipio          VARCHAR(100),
    ADD COLUMN email              VARCHAR(150),
    ADD COLUMN telefone           VARCHAR(30),
    ADD COLUMN situacao_cadastral VARCHAR(30),
    ADD COLUMN fonte_dados        VARCHAR(60),    -- ex.: 'BrasilAPI (dados abertos RFB)'
    ADD COLUMN dados_consultados_em TIMESTAMPTZ;
