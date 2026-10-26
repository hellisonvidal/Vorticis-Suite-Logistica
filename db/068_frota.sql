-- =============================================================================
-- 068 — Módulo Frota (piloto): controle de frota para transportadoras. A empresa ativa é a transportadora.
--   * Indicador do módulo na empresa (modulo_frota) e funções FROTA_* nos perfis padrão.
--   * Veículos (inclusive reboques e semirreboques) e motoristas (CNH).
--   * Abastecimentos (litros, valor, km) para o consumo médio (km/l) e o custo de combustível.
--   * Planos de manutenção preventiva (a cada X km e/ou Y dias) e ordens de serviço (preventivas e corretivas).
--   * Viagens com frete e despesas (pedágio, diárias, outras); documentos com vencimento (licenciamento, seguro, ANTT...).
--   * Configuração: antecedência dos avisos de vencimento e tolerância do consumo.
-- =============================================================================
ALTER TABLE core.empresa
    ADD COLUMN modulo_frota BOOLEAN NOT NULL DEFAULT FALSE,
    DROP CONSTRAINT ck_empresa_algum_modulo,
    ADD CONSTRAINT ck_empresa_algum_modulo
        CHECK (modulo_contabil OR modulo_fiscal OR modulo_folha OR modulo_escritorio OR modulo_erp OR modulo_condominio OR modulo_frota);

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('FROTA_CADASTROS'), ('FROTA_OPERACAO'), ('FROTA_RELATORIOS')) AS x(permissao) ON TRUE
 WHERE p.administrador OR (p.padrao AND p.nome = 'Contador')
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
  JOIN (VALUES ('Assistente', 'FROTA_CADASTROS'), ('Assistente', 'FROTA_OPERACAO'), ('Consulta', 'FROTA_RELATORIOS')) AS x(nome, permissao)
    ON x.nome = p.nome
 WHERE p.padrao
ON CONFLICT DO NOTHING;

CREATE SCHEMA frota;

CREATE TABLE frota.configuracao (
    empresa_id         BIGINT       PRIMARY KEY REFERENCES core.empresa(id) ON DELETE CASCADE,
    dias_aviso         SMALLINT     NOT NULL DEFAULT 30 CHECK (dias_aviso BETWEEN 1 AND 365),
    tolerancia_consumo NUMERIC(5,2) NOT NULL DEFAULT 15 CHECK (tolerancia_consumo BETWEEN 0 AND 100)
);

CREATE TABLE frota.veiculo (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL CHECK (codigo > 0),
    placa              CHAR(7)       NOT NULL CHECK (placa ~ '^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$'),
    renavam            CHAR(11)      CHECK (renavam IS NULL OR renavam ~ '^[0-9]{11}$'),
    tipo               VARCHAR(14)   NOT NULL,
    marca              VARCHAR(40),
    modelo             VARCHAR(60),
    ano_fabricacao     SMALLINT      CHECK (ano_fabricacao IS NULL OR ano_fabricacao BETWEEN 1950 AND 2100),
    ano_modelo         SMALLINT      CHECK (ano_modelo IS NULL OR ano_modelo BETWEEN 1950 AND 2101),
    combustivel        VARCHAR(10)   NOT NULL DEFAULT 'DIESEL_S10',
    capacidade_kg      NUMERIC(12,2) CHECK (capacidade_kg IS NULL OR capacidade_kg >= 0),
    km_atual           NUMERIC(12,1) NOT NULL DEFAULT 0 CHECK (km_atual >= 0),
    media_esperada     NUMERIC(6,2)  CHECK (media_esperada IS NULL OR media_esperada > 0),   -- km/l
    ativo              BOOLEAN       NOT NULL DEFAULT TRUE,
    observacao         VARCHAR(500),
    CONSTRAINT uq_veiculo_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT uq_veiculo_placa UNIQUE (empresa_id, placa),
    CONSTRAINT ck_veiculo_tipo CHECK (tipo IN ('CAVALO', 'TRUCK', 'TOCO', 'CARRETA', 'BITREM', 'VAN', 'UTILITARIO', 'CARRO', 'MOTO',
                                               'REBOQUE', 'SEMIRREBOQUE', 'OUTRO')),
    CONSTRAINT ck_veiculo_combustivel CHECK (combustivel IN ('DIESEL_S10', 'DIESEL', 'GASOLINA', 'ETANOL', 'FLEX', 'GNV', 'ELETRICO', 'NENHUM'))
);

CREATE TABLE frota.motorista (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo             INTEGER       NOT NULL CHECK (codigo > 0),
    nome               VARCHAR(150)  NOT NULL,
    cpf                CHAR(11)      NOT NULL CHECK (cpf ~ '^[0-9]{11}$'),
    cnh                VARCHAR(11)   CHECK (cnh IS NULL OR cnh ~ '^[0-9]{9,11}$'),
    cnh_categoria      VARCHAR(2)    CHECK (cnh_categoria IS NULL OR cnh_categoria IN ('A', 'B', 'C', 'D', 'E', 'AB', 'AC', 'AD', 'AE')),
    cnh_validade       DATE,
    telefone           VARCHAR(20),
    ativo              BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_motorista_codigo UNIQUE (empresa_id, codigo),
    CONSTRAINT uq_motorista_cpf UNIQUE (empresa_id, cpf)
);

CREATE TABLE frota.abastecimento (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    veiculo_id         BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    motorista_id       BIGINT        REFERENCES frota.motorista(id),
    data               DATE          NOT NULL,
    km                 NUMERIC(12,1) NOT NULL CHECK (km >= 0),
    litros             NUMERIC(10,3) NOT NULL CHECK (litros > 0),
    valor_total        NUMERIC(15,2) NOT NULL CHECK (valor_total > 0),
    combustivel        VARCHAR(10)   NOT NULL,
    posto              VARCHAR(100),
    tanque_cheio       BOOLEAN       NOT NULL DEFAULT TRUE,
    observacao         VARCHAR(300),
    CONSTRAINT ck_abastecimento_combustivel CHECK (combustivel IN ('DIESEL_S10', 'DIESEL', 'GASOLINA', 'ETANOL', 'GNV', 'ARLA32'))
);
CREATE INDEX ix_abastecimento_veiculo ON frota.abastecimento (veiculo_id, data, km);

CREATE TABLE frota.plano_manutencao (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    veiculo_id         BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    descricao          VARCHAR(100)  NOT NULL,
    intervalo_km       NUMERIC(10,1) CHECK (intervalo_km IS NULL OR intervalo_km > 0),
    intervalo_dias     INTEGER       CHECK (intervalo_dias IS NULL OR intervalo_dias > 0),
    ultima_km          NUMERIC(12,1),
    ultima_data        DATE,
    ativo              BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_plano_intervalo CHECK (intervalo_km IS NOT NULL OR intervalo_dias IS NOT NULL)
);

CREATE TABLE frota.ordem_servico (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL,
    veiculo_id         BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    plano_id           BIGINT        REFERENCES frota.plano_manutencao(id) ON DELETE SET NULL,
    tipo               VARCHAR(10)   NOT NULL,
    data               DATE          NOT NULL,
    km                 NUMERIC(12,1) CHECK (km IS NULL OR km >= 0),
    descricao          VARCHAR(300)  NOT NULL,
    oficina            VARCHAR(100),
    valor_pecas        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_pecas >= 0),
    valor_mao_obra     NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_mao_obra >= 0),
    situacao           VARCHAR(9)    NOT NULL DEFAULT 'ABERTA',
    CONSTRAINT uq_ordem_servico UNIQUE (empresa_id, numero),
    CONSTRAINT ck_os_tipo CHECK (tipo IN ('PREVENTIVA', 'CORRETIVA')),
    CONSTRAINT ck_os_situacao CHECK (situacao IN ('ABERTA', 'CONCLUIDA'))
);

CREATE TABLE frota.viagem (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL,
    veiculo_id         BIGINT        NOT NULL REFERENCES frota.veiculo(id),
    motorista_id       BIGINT        REFERENCES frota.motorista(id),
    cliente            VARCHAR(150),
    origem             VARCHAR(100)  NOT NULL,
    destino            VARCHAR(100)  NOT NULL,
    saida              DATE          NOT NULL,
    chegada            DATE,
    km_saida           NUMERIC(12,1) NOT NULL CHECK (km_saida >= 0),
    km_chegada         NUMERIC(12,1),
    valor_frete        NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (valor_frete >= 0),
    pedagio            NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (pedagio >= 0),
    diarias            NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (diarias >= 0),
    outras_despesas    NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (outras_despesas >= 0),
    situacao           VARCHAR(12)   NOT NULL DEFAULT 'EM_ANDAMENTO',
    CONSTRAINT uq_viagem UNIQUE (empresa_id, numero),
    CONSTRAINT ck_viagem_situacao CHECK (situacao IN ('EM_ANDAMENTO', 'CONCLUIDA', 'CANCELADA')),
    CONSTRAINT ck_viagem_km CHECK (km_chegada IS NULL OR km_chegada >= km_saida),
    CONSTRAINT ck_viagem_datas CHECK (chegada IS NULL OR chegada >= saida),
    CONSTRAINT ck_viagem_concluida CHECK (situacao <> 'CONCLUIDA' OR (chegada IS NOT NULL AND km_chegada IS NOT NULL))
);

CREATE TABLE frota.documento (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    veiculo_id         BIGINT        REFERENCES frota.veiculo(id) ON DELETE CASCADE,
    motorista_id       BIGINT        REFERENCES frota.motorista(id) ON DELETE CASCADE,
    tipo               VARCHAR(13)   NOT NULL,
    numero             VARCHAR(40),
    vencimento         DATE          NOT NULL,
    valor              NUMERIC(15,2) CHECK (valor IS NULL OR valor >= 0),
    observacao         VARCHAR(300),
    CONSTRAINT ck_documento_tipo CHECK (tipo IN ('LICENCIAMENTO', 'IPVA', 'SEGURO', 'ANTT_RNTRC', 'TACOGRAFO', 'AET', 'EXAME_TOXICO', 'MOPP', 'OUTRO')),
    CONSTRAINT ck_documento_dono CHECK (veiculo_id IS NULL OR motorista_id IS NULL)
);
