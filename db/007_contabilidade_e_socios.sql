-- =============================================================================
-- Perseus Contábil — Migração 007: contabilidade responsável e quadro societário
--   * Contadores e escritórios contábeis (cadastros globais, um escritório atende
--     várias empresas) e o vínculo de cada um com a empresa, com vigência e
--     responsável técnico — dados dos signatários da ECD/ECF (J930/0930) e dos
--     termos do Livro Diário
--   * Sócios da empresa (QSA), com representante legal
-- =============================================================================

CREATE TABLE core.escritorio_contabil (
    id            BIGSERIAL PRIMARY KEY,
    cnpj          CHAR(14)     NOT NULL UNIQUE,
    razao_social  VARCHAR(150) NOT NULL,
    crc           VARCHAR(20),                 -- registro da organização contábil no CRC
    uf_crc        CHAR(2),
    email         VARCHAR(150),
    telefone      VARCHAR(30),
    ativo         BOOLEAN      NOT NULL DEFAULT TRUE,
    criado_em     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_escritorio_cnpj CHECK (cnpj ~ '^[0-9A-Z]{12}[0-9]{2}$')
);

CREATE TABLE core.contador (
    id            BIGSERIAL PRIMARY KEY,
    nome          VARCHAR(150) NOT NULL,
    cpf           CHAR(11)     NOT NULL UNIQUE,
    crc           VARCHAR(20)  NOT NULL,       -- número de registro no CRC (ex.: 1SP123456/O-7)
    uf_crc        CHAR(2)      NOT NULL,
    email         VARCHAR(150),
    telefone      VARCHAR(30),
    escritorio_id BIGINT       REFERENCES core.escritorio_contabil(id),
    ativo         BOOLEAN      NOT NULL DEFAULT TRUE,
    criado_em     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_contador_cpf CHECK (cpf ~ '^[0-9]{11}$')
);

-- Quem cuida da contabilidade da empresa, e desde quando. Um vínculo é de um contador OU de um escritório.
CREATE TABLE core.empresa_contabilidade (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT  NOT NULL REFERENCES core.empresa(id),
    contador_id         BIGINT  REFERENCES core.contador(id),
    escritorio_id       BIGINT  REFERENCES core.escritorio_contabil(id),
    inicio              DATE    NOT NULL,
    fim                 DATE,
    responsavel_tecnico BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT ck_um_ou_outro CHECK ((contador_id IS NULL) <> (escritorio_id IS NULL)),
    CONSTRAINT ck_vigencia CHECK (fim IS NULL OR fim >= inicio),
    CONSTRAINT ck_responsavel_contador CHECK (NOT responsavel_tecnico OR contador_id IS NOT NULL)
);
-- Um só responsável técnico vigente por empresa; o mesmo contador/escritório não fica vinculado duas vezes ao mesmo tempo.
CREATE UNIQUE INDEX uq_responsavel_vigente ON core.empresa_contabilidade (empresa_id) WHERE responsavel_tecnico AND fim IS NULL;
CREATE UNIQUE INDEX uq_contador_vigente ON core.empresa_contabilidade (empresa_id, contador_id) WHERE contador_id IS NOT NULL AND fim IS NULL;
CREATE UNIQUE INDEX uq_escritorio_vigente ON core.empresa_contabilidade (empresa_id, escritorio_id) WHERE escritorio_id IS NOT NULL AND fim IS NULL;

CREATE TABLE core.socio (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    nome                VARCHAR(150) NOT NULL,
    tipo_pessoa         CHAR(1)      NOT NULL DEFAULT 'F',    -- F física, J jurídica, E estrangeiro
    cpf_cnpj            VARCHAR(14),
    documento_mascarado VARCHAR(20),                          -- como divulgado na base pública (ex.: ***123456**)
    qualificacao        VARCHAR(100) NOT NULL,                -- ex.: Sócio-Administrador
    qualificacao_codigo SMALLINT,                             -- código da qualificação no QSA da RFB, quando importado
    participacao        NUMERIC(7,4),                         -- % do capital
    data_entrada        DATE,
    data_saida          DATE,
    representante_legal BOOLEAN      NOT NULL DEFAULT FALSE,
    email               VARCHAR(150),
    telefone            VARCHAR(30),
    origem              VARCHAR(40)  NOT NULL DEFAULT 'MANUAL',
    CONSTRAINT ck_socio_tipo CHECK (tipo_pessoa IN ('F', 'J', 'E')),
    CONSTRAINT ck_socio_participacao CHECK (participacao IS NULL OR participacao BETWEEN 0 AND 100),
    CONSTRAINT ck_socio_saida CHECK (data_saida IS NULL OR data_entrada IS NULL OR data_saida >= data_entrada)
);
CREATE INDEX ix_socio_empresa ON core.socio (empresa_id);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.escritorio_contabil FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.contador FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.empresa_contabilidade FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON core.socio FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT id, 'EMPRESA_RESPONSAVEIS_MANTER' FROM core.perfil WHERE padrao AND nome = 'Contador'
ON CONFLICT DO NOTHING;
