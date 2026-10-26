-- 008: configuração inicial do sistema (primeiro uso).
-- Depois de criar o administrador, a definição dos perfis de acesso é obrigatória antes de usar o sistema;
-- o cadastro de empresas deixa de ser exigido no primeiro acesso.

CREATE TABLE core.configuracao_sistema (
    chave        VARCHAR(60)  PRIMARY KEY,
    valor        TEXT         NOT NULL,
    alterado_em  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    alterado_por BIGINT       REFERENCES core.usuario (id)
);

COMMENT ON TABLE core.configuracao_sistema IS
    'Parâmetros gerais do sistema. perfis_configurados = S quando o administrador concluiu a definição dos perfis de acesso.';

-- Bases já em uso (com empresas ou mais de um usuário) não passam de novo pela configuração inicial.
INSERT INTO core.configuracao_sistema (chave, valor)
SELECT 'perfis_configurados', 'S'
 WHERE EXISTS (SELECT 1 FROM core.empresa) OR (SELECT count(*) FROM core.usuario) > 1;
