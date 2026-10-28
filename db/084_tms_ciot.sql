-- =============================================================================
-- Vorticis Suite Logística — CIOT (Código Identificador da Operação de Transporte), módulo Transporte › Operação
--   * Operação de transporte com TAC (transportador autônomo, CPF) ou ETC equiparada (CNPJ), obrigatória quando a transportadora
--     subcontrata (Res. ANTT 5.862/2019): contratado, RNTRC, veículo, trecho, carga, valores, vale-pedágio, adiantamento e o CIOT
--     gerado pela IPEF (Repom, Pamcard, e-Frete...); CT-e ligados à operação.
--   * Retenções do TAC (INSS, SEST/SENAT e IRRF) calculadas pelos parâmetros legais TAC_RETENCOES, FOLHA_INSS_EMPREGADO (teto) e
--     FOLHA_IRRF_MENSAL (tabela do IR), acumulando os fretes do mesmo contratado no mês.
-- =============================================================================

CREATE TABLE tms.ciot (
    id                 BIGSERIAL     PRIMARY KEY,
    empresa_id         BIGINT        NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    numero             INTEGER       NOT NULL CHECK (numero > 0),
    ciot               VARCHAR(12)   CHECK (ciot IS NULL OR ciot ~ '^[0-9]{12}$'),
    codigo_verificador VARCHAR(4),
    ipef               VARCHAR(60),
    tipo               VARCHAR(12)   NOT NULL DEFAULT 'PADRAO',
    contratado_id      BIGINT        NOT NULL REFERENCES core.participante(id),
    rntrc              VARCHAR(9)    NOT NULL CHECK (rntrc ~ '^[0-9]{8,9}$'),
    placa              VARCHAR(7)    NOT NULL,
    motorista_nome     VARCHAR(100),
    motorista_cpf      VARCHAR(11),
    viagem_id          BIGINT        REFERENCES frota.viagem(id) ON DELETE SET NULL,
    origem_uf          CHAR(2)       NOT NULL,
    origem_ibge        CHAR(7)       NOT NULL CHECK (origem_ibge ~ '^[0-9]{7}$'),
    origem_municipio   VARCHAR(100)  NOT NULL,
    destino_uf         CHAR(2)       NOT NULL,
    destino_ibge       CHAR(7)       NOT NULL CHECK (destino_ibge ~ '^[0-9]{7}$'),
    destino_municipio  VARCHAR(100)  NOT NULL,
    inicio             DATE          NOT NULL,
    fim                DATE          NOT NULL,
    produto            VARCHAR(100),
    ncm                VARCHAR(8),
    peso_kg            NUMERIC(12,3) NOT NULL DEFAULT 0 CHECK (peso_kg >= 0),
    valor_frete        NUMERIC(15,2) NOT NULL CHECK (valor_frete > 0),
    vale_pedagio       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (vale_pedagio >= 0),
    adiantamento       NUMERIC(15,2) NOT NULL DEFAULT 0 CHECK (adiantamento >= 0),
    dependentes        SMALLINT      NOT NULL DEFAULT 0 CHECK (dependentes BETWEEN 0 AND 20),
    inss               NUMERIC(15,2) NOT NULL DEFAULT 0,
    sest_senat         NUMERIC(15,2) NOT NULL DEFAULT 0,
    irrf               NUMERIC(15,2) NOT NULL DEFAULT 0,
    situacao           VARCHAR(10)   NOT NULL DEFAULT 'DIGITADO',
    observacao         VARCHAR(300),
    CONSTRAINT uq_ciot_numero UNIQUE (empresa_id, numero),
    CONSTRAINT ck_ciot_tipo CHECK (tipo IN ('PADRAO', 'TAC_AGREGADO')),
    CONSTRAINT ck_ciot_situacao CHECK (situacao IN ('DIGITADO', 'DECLARADO', 'ENCERRADO', 'CANCELADO')),
    CONSTRAINT ck_ciot_declarado CHECK (situacao IN ('DIGITADO', 'CANCELADO') OR ciot IS NOT NULL),
    CONSTRAINT ck_ciot_periodo CHECK (fim >= inicio)
);
CREATE UNIQUE INDEX uq_ciot_codigo ON tms.ciot (empresa_id, ciot) WHERE ciot IS NOT NULL;

CREATE TABLE tms.ciot_cte (
    ciot_id   BIGINT NOT NULL REFERENCES tms.ciot(id) ON DELETE CASCADE,
    cte_id    BIGINT NOT NULL REFERENCES tms.cte(id) ON DELETE CASCADE,
    PRIMARY KEY (ciot_id, cte_id)
);

INSERT INTO core.parametro_legal (chave, vigencia_inicio, valor, fundamento_legal) VALUES
    ('TAC_RETENCOES', DATE '2016-01-01',
     '{"baseInss": 20, "aliquotaInss": 11, "sestSenat": 2.5, "baseIrrf": 10}',
     'Decreto 3.048/1999 art. 201 §4º e IN RFB 2.110/2022 (base de 20% do frete; INSS 11% até o teto); Lei 8.706/1993 (SEST 1,5% + SENAT 1,0%); RIR/2018 art. 39 (IR sobre 10% do frete de carga)')
ON CONFLICT (chave, vigencia_inicio) DO NOTHING;
