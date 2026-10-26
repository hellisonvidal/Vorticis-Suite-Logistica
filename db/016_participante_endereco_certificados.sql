-- =============================================================================
-- Perseus Contábil — Migração 016: endereço de clientes/fornecedores, contador que assina
--   a ECD/ECF e repositório de certificados digitais
-- =============================================================================

-- Clientes e fornecedores com endereço completo (como no cadastro de empresas)
ALTER TABLE core.participante
    ADD COLUMN cep          CHAR(8),
    ADD COLUMN logradouro   VARCHAR(150),
    ADD COLUMN numero       VARCHAR(20),
    ADD COLUMN complemento  VARCHAR(100),
    ADD COLUMN bairro       VARCHAR(100),
    ADD CONSTRAINT ck_participante_cep CHECK (cep IS NULL OR cep ~ '^[0-9]{8}$');

-- Contador que assina a ECD e a ECF (J930 / termo de abertura e encerramento)
ALTER TABLE core.contador
    ADD COLUMN assina_ecd_ecf BOOLEAN NOT NULL DEFAULT FALSE;

-- Repositório de certificados digitais para comunicação com órgãos públicos (Receita, SEFAZ, Junta...).
-- A1: o arquivo .pfx fica guardado como foi importado (protegido pela senha dele, que não é gravada).
-- A3: token/cartão; só a identificação (o certificado fica no dispositivo).
CREATE TABLE core.certificado_digital (
    id            BIGSERIAL PRIMARY KEY,
    tipo          CHAR(2)      NOT NULL CHECK (tipo IN ('A1', 'A3')),
    descricao     VARCHAR(150) NOT NULL,
    titular       VARCHAR(250) NOT NULL,
    documento     VARCHAR(14),                    -- CNPJ ou CPF do titular, quando presente no certificado
    emissor       VARCHAR(250) NOT NULL,
    valido_de     TIMESTAMPTZ  NOT NULL,
    valido_ate    TIMESTAMPTZ  NOT NULL,
    impressao     CHAR(40)     NOT NULL UNIQUE,   -- thumbprint SHA-1
    arquivo       BYTEA,                          -- PFX (somente A1)
    empresa_id    BIGINT       REFERENCES core.empresa(id) ON DELETE SET NULL,
    criado_em     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_certificado_arquivo CHECK ((tipo = 'A1') = (arquivo IS NOT NULL))
);

-- A limpeza do banco também remove os certificados.
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
    DELETE FROM core.certificado_digital;
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
