-- =============================================================================
-- Vorticis — Migração 067: clientes do Escritório com os dados de empresa e o vínculo com os demais módulos
--   * escritorio.pessoa ganha os campos do cadastro de empresas (fantasia, natureza jurídica, CNAE, IE, início das
--     atividades e endereço completo com o município IBGE), preenchidos pela consulta do CNPJ como no Contábil.
--   * empresa_vinculada_id: a empresa (core.empresa) criada ou atualizada a partir do cliente, com os módulos do contrato
--     (Contábil, Fiscal, Folha, Condomínio, Gerencial). Excluída a empresa, o vínculo some (SET NULL).
-- =============================================================================

ALTER TABLE escritorio.pessoa
    ADD COLUMN nome_fantasia          VARCHAR(150),
    ADD COLUMN natureza_juridica      CHAR(4)      CHECK (natureza_juridica IS NULL OR natureza_juridica ~ '^[0-9]{4}$'),
    ADD COLUMN cnae                   CHAR(7)      CHECK (cnae IS NULL OR cnae ~ '^[0-9]{7}$'),
    ADD COLUMN inscricao_estadual     VARCHAR(20),
    ADD COLUMN data_inicio_atividades DATE,
    ADD COLUMN cep                    CHAR(8)      CHECK (cep IS NULL OR cep ~ '^[0-9]{8}$'),
    ADD COLUMN logradouro             VARCHAR(150),
    ADD COLUMN numero                 VARCHAR(20),
    ADD COLUMN complemento            VARCHAR(100),
    ADD COLUMN bairro                 VARCHAR(100),
    ADD COLUMN cod_municipio_ibge     CHAR(7)      CHECK (cod_municipio_ibge IS NULL OR cod_municipio_ibge ~ '^[0-9]{7}$'),
    ADD COLUMN empresa_vinculada_id   BIGINT       REFERENCES core.empresa (id) ON DELETE SET NULL;

CREATE UNIQUE INDEX uq_pessoa_empresa_vinculada ON escritorio.pessoa (empresa_id, empresa_vinculada_id) WHERE empresa_vinculada_id IS NOT NULL;
