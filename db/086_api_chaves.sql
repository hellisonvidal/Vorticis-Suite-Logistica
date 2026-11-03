-- Integração por API (Configurações › Integração por API): chaves de acesso de sistemas externos.
-- A chave é mostrada uma vez ao ser gerada; aqui fica só o hash SHA-256 e o começo dela (para o usuário reconhecer).
-- Cada chave age em nome de um usuário e de uma empresa (as permissões do perfil do usuário valem para a API).
CREATE TABLE core.api_chave (
    id           BIGSERIAL    PRIMARY KEY,
    empresa_id   BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    usuario_id   BIGINT       NOT NULL REFERENCES core.usuario(id),
    nome         VARCHAR(80)  NOT NULL CHECK (length(trim(nome)) > 0),
    prefixo      VARCHAR(12)  NOT NULL,
    hash         CHAR(64)     NOT NULL UNIQUE,
    escrita      BOOLEAN      NOT NULL DEFAULT FALSE,
    criada_em    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    revogada_em  TIMESTAMPTZ,
    ultimo_uso   TIMESTAMPTZ
);
CREATE INDEX ix_api_chave_empresa ON core.api_chave (empresa_id);
