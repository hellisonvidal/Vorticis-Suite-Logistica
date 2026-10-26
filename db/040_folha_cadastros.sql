-- =============================================================================
-- Vorticis Folha de Pagamento — Migração 040: cadastros e tabelas legais
--   * Esquema folha: departamentos, cargos, funcionários, dependentes, rubricas e parâmetros da empresa.
--   * Tabelas legais por vigência em core.parametro_legal (INSS, IRRF, salário-família, salário mínimo, FGTS).
--   * Permissões novas da folha (grupo "Folha de pagamento") nos perfis padrão.
--   Faixa reservada da Folha: 040 a 049. Dados das empresas saem junto com a empresa (ON DELETE CASCADE).
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS folha;

CREATE TABLE folha.departamento (
    id          BIGSERIAL PRIMARY KEY,
    empresa_id  BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo      VARCHAR(20)  NOT NULL,                       -- eSocial S-1020 codLotacao
    nome        VARCHAR(100) NOT NULL,
    ativo       BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_folha_departamento UNIQUE (empresa_id, codigo)
);

CREATE TABLE folha.cargo (
    id          BIGSERIAL PRIMARY KEY,
    empresa_id  BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo      VARCHAR(20)  NOT NULL,
    nome        VARCHAR(100) NOT NULL,
    cbo         CHAR(6),                                     -- Classificação Brasileira de Ocupações
    ativo       BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_folha_cargo UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_folha_cargo_cbo CHECK (cbo IS NULL OR cbo ~ '^[0-9]{6}$')
);

CREATE TABLE folha.funcionario (
    id                      BIGSERIAL PRIMARY KEY,
    empresa_id              BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    matricula               VARCHAR(30)  NOT NULL,
    nome                    VARCHAR(150) NOT NULL,
    cpf                     CHAR(11)     NOT NULL,
    pis_nis                 CHAR(11),
    data_nascimento         DATE         NOT NULL,
    sexo                    CHAR(1)      NOT NULL,           -- M | F
    raca_cor                SMALLINT     NOT NULL DEFAULT 6, -- eSocial Tabela 02 (6 = não informado)
    estado_civil            SMALLINT     NOT NULL DEFAULT 1, -- 1 solteiro, 2 casado, 3 divorciado, 4 separado, 5 viúvo
    grau_instrucao          CHAR(2)      NOT NULL DEFAULT '09',
    email                   VARCHAR(150),
    telefone                VARCHAR(20),
    cep                     CHAR(8),
    logradouro              VARCHAR(100),
    numero                  VARCHAR(15),
    complemento             VARCHAR(60),
    bairro                  VARCHAR(60),
    cod_municipio_ibge      CHAR(7),
    uf                      CHAR(2),
    -- vínculo
    data_admissao           DATE         NOT NULL,
    categoria_esocial       CHAR(3)      NOT NULL DEFAULT '101',   -- 101 empregado, 103 aprendiz, 901 estagiário...
    tipo_contrato           VARCHAR(15)  NOT NULL DEFAULT 'INDETERMINADO',
    data_termino_contrato   DATE,
    cargo_id                BIGINT       REFERENCES folha.cargo(id),
    departamento_id         BIGINT       REFERENCES folha.departamento(id),
    cbo                     CHAR(6),
    tipo_salario            VARCHAR(8)   NOT NULL DEFAULT 'MENSAL',   -- MENSAL | HORISTA (salário = valor da hora)
    salario                 NUMERIC(18,2) NOT NULL,
    jornada_semanal_horas   NUMERIC(5,2) NOT NULL DEFAULT 44,
    jornada_mensal_horas    NUMERIC(6,2) NOT NULL DEFAULT 220,
    centro_custo_id         BIGINT       REFERENCES contabil.centro_custo(id) ON DELETE SET NULL,
    insalubridade_percentual NUMERIC(4,0) NOT NULL DEFAULT 0,        -- 0, 10, 20 ou 40 (% do salário mínimo)
    periculosidade          BOOLEAN      NOT NULL DEFAULT FALSE,     -- 30% do salário base
    -- dados bancários
    banco                   CHAR(3),
    agencia                 VARCHAR(10),
    conta                   VARCHAR(20),
    tipo_conta              VARCHAR(10),
    chave_pix               VARCHAR(100),
    -- situação
    situacao                VARCHAR(10)  NOT NULL DEFAULT 'ATIVO',    -- ATIVO | AFASTADO | DESLIGADO
    data_desligamento       DATE,
    motivo_desligamento     VARCHAR(20),
    criado_em               TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT uq_folha_func_matricula UNIQUE (empresa_id, matricula),
    CONSTRAINT uq_folha_func_cpf UNIQUE (empresa_id, cpf),
    CONSTRAINT ck_folha_func_cpf CHECK (cpf ~ '^[0-9]{11}$'),
    CONSTRAINT ck_folha_func_pis CHECK (pis_nis IS NULL OR pis_nis ~ '^[0-9]{11}$'),
    CONSTRAINT ck_folha_func_sexo CHECK (sexo IN ('M', 'F')),
    CONSTRAINT ck_folha_func_salario CHECK (salario > 0),
    CONSTRAINT ck_folha_func_tipo_salario CHECK (tipo_salario IN ('MENSAL', 'HORISTA')),
    CONSTRAINT ck_folha_func_contrato CHECK (tipo_contrato IN ('INDETERMINADO', 'DETERMINADO', 'EXPERIENCIA', 'APRENDIZ', 'ESTAGIO')),
    CONSTRAINT ck_folha_func_situacao CHECK (situacao IN ('ATIVO', 'AFASTADO', 'DESLIGADO')),
    CONSTRAINT ck_folha_func_insal CHECK (insalubridade_percentual IN (0, 10, 20, 40)),
    CONSTRAINT ck_folha_func_deslig CHECK (data_desligamento IS NULL OR data_desligamento >= data_admissao)
);
CREATE INDEX ix_folha_func_situacao ON folha.funcionario (empresa_id, situacao);

CREATE TABLE folha.dependente (
    id                  BIGSERIAL PRIMARY KEY,
    funcionario_id      BIGINT       NOT NULL REFERENCES folha.funcionario(id) ON DELETE CASCADE,
    nome                VARCHAR(150) NOT NULL,
    cpf                 CHAR(11),
    data_nascimento     DATE         NOT NULL,
    tipo_dependente     CHAR(2)      NOT NULL DEFAULT '03',   -- eSocial Tabela 07 (03 = filho/enteado)
    incapaz             BOOLEAN      NOT NULL DEFAULT FALSE,
    dep_irrf            BOOLEAN      NOT NULL DEFAULT TRUE,   -- deduz do IRRF
    dep_salario_familia BOOLEAN      NOT NULL DEFAULT FALSE,  -- gera salário-família (menor de 14 anos ou inválido)
    CONSTRAINT ck_folha_dep_cpf CHECK (cpf IS NULL OR cpf ~ '^[0-9]{11}$')
);

-- Rubricas (eventos da folha). Códigos de incidência = tabelas 21 (CP), 22 (IRRF) e 23 (FGTS) do eSocial.
CREATE TABLE folha.rubrica (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id) ON DELETE CASCADE,
    codigo              VARCHAR(10)  NOT NULL,
    descricao           VARCHAR(100) NOT NULL,
    tipo                CHAR(1)      NOT NULL,                -- P provento | D desconto | I informativa
    natureza_esocial    CHAR(4)      NOT NULL,                -- Tabela 03
    inc_inss            CHAR(2)      NOT NULL DEFAULT '00',   -- 00 não incide | 11 base mensal | 12 base 13º | 21 desconto do segurado
    inc_irrf            CHAR(2)      NOT NULL DEFAULT '00',   -- 00 não incide | 01 rendimento mensal | 09 13º | 11 dedução previdência | 31 dedução pensão
    inc_fgts            CHAR(2)      NOT NULL DEFAULT '00',   -- 00 não incide | 11 base mensal | 12 base 13º
    variavel            BOOLEAN      NOT NULL DEFAULT FALSE,  -- entra nas médias de férias, 13º e rescisão
    sistema             BOOLEAN      NOT NULL DEFAULT FALSE,  -- rubrica padrão usada pelo cálculo (não excluível)
    ativa               BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_folha_rubrica UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_folha_rubrica_tipo CHECK (tipo IN ('P', 'D', 'I'))
);

-- Parâmetros de encargos da empresa (RAT, FAP, terceiros, anexo do Simples Nacional).
CREATE TABLE folha.parametro_empresa (
    empresa_id          BIGINT PRIMARY KEY REFERENCES core.empresa(id) ON DELETE CASCADE,
    rat_aliquota        NUMERIC(5,2) NOT NULL DEFAULT 2.00,   -- grau de risco 1, 2 ou 3 (%)
    fap                 NUMERIC(5,4) NOT NULL DEFAULT 1.0000, -- 0,5000 a 2,0000
    terceiros_aliquota  NUMERIC(5,2) NOT NULL DEFAULT 5.80,   -- FPAS 515 (comércio/serviços) = 5,8%
    fpas                CHAR(3)      NOT NULL DEFAULT '515',
    cod_terceiros       CHAR(4)      NOT NULL DEFAULT '0079',
    simples_anexo       VARCHAR(3),                           -- I, II, III, IV ou V (só Simples Nacional)
    class_trib          CHAR(2)      NOT NULL DEFAULT '99',   -- eSocial Tabela 08
    responsavel_nome    VARCHAR(100),                         -- S-1299 ideRespInf
    responsavel_cpf     CHAR(11),
    responsavel_email   VARCHAR(100),
    responsavel_telefone VARCHAR(15),
    CONSTRAINT ck_folha_param_anexo CHECK (simples_anexo IS NULL OR simples_anexo IN ('I', 'II', 'III', 'IV', 'V'))
);

CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.funcionario FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.rubrica FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();
CREATE TRIGGER trg_auditoria AFTER INSERT OR UPDATE OR DELETE ON folha.parametro_empresa FOR EACH ROW EXECUTE FUNCTION core.fn_auditoria();

-- -----------------------------------------------------------------------------
-- Tabelas legais por vigência (core.parametro_legal): nada de valor legal no código.
-- -----------------------------------------------------------------------------
-- INSS do empregado: alíquotas progressivas por faixa (Portaria Interministerial MPS/MF nº 13/2026; 2025: Portaria nº 6/2025).
INSERT INTO core.parametro_legal (chave, vigencia_inicio, vigencia_fim, valor, fundamento_legal) VALUES
('FOLHA_INSS_EMPREGADO', '2025-01-01', '2025-12-31',
 '{"faixas":[{"ate":1518.00,"aliquota":7.5},{"ate":2793.88,"aliquota":9},{"ate":4190.83,"aliquota":12},{"ate":8157.41,"aliquota":14}]}',
 'Portaria Interministerial MPS/MF nº 6/2025 (INSS progressivo, teto R$ 8.157,41)'),
('FOLHA_INSS_EMPREGADO', '2026-01-01', NULL,
 '{"faixas":[{"ate":1621.00,"aliquota":7.5},{"ate":2902.84,"aliquota":9},{"ate":4354.27,"aliquota":12},{"ate":8475.55,"aliquota":14}]}',
 'Portaria Interministerial MPS/MF nº 13/2026 (INSS progressivo, teto R$ 8.475,55)')
ON CONFLICT (chave, vigencia_inicio) DO NOTHING;

-- IRRF mensal: tabela progressiva, dedução por dependente, desconto simplificado e redução da Lei 15.270/2025.
INSERT INTO core.parametro_legal (chave, vigencia_inicio, vigencia_fim, valor, fundamento_legal) VALUES
('FOLHA_IRRF_MENSAL', '2025-05-01', '2025-12-31',
 '{"faixas":[{"ate":2428.80,"aliquota":0,"deduzir":0},{"ate":2826.65,"aliquota":7.5,"deduzir":182.16},{"ate":3751.05,"aliquota":15,"deduzir":394.16},{"ate":4664.68,"aliquota":22.5,"deduzir":675.49},{"ate":null,"aliquota":27.5,"deduzir":908.73}],"deducaoDependente":189.59,"descontoSimplificado":607.20,"reducao":null}',
 'MP 1.294/2025 (Lei 15.191/2025) e Lei 14.848/2024: tabela progressiva mensal e desconto simplificado de R$ 607,20'),
('FOLHA_IRRF_MENSAL', '2026-01-01', NULL,
 '{"faixas":[{"ate":2428.80,"aliquota":0,"deduzir":0},{"ate":2826.65,"aliquota":7.5,"deduzir":182.16},{"ate":3751.05,"aliquota":15,"deduzir":394.16},{"ate":4664.68,"aliquota":22.5,"deduzir":675.49},{"ate":null,"aliquota":27.5,"deduzir":908.73}],"deducaoDependente":189.59,"descontoSimplificado":607.20,"reducao":{"limiteIsencao":5000.00,"reducaoMaxima":312.89,"limiteSuperior":7350.00,"constante":978.62,"coeficiente":0.133145}}',
 'Lei 15.270/2025 (redução do IRRF: até R$ 5.000 isento; R$ 978,62 - 0,133145 x rendimento até R$ 7.350) e tabela mensal da Lei 15.191/2025')
ON CONFLICT (chave, vigencia_inicio) DO NOTHING;

INSERT INTO core.parametro_legal (chave, vigencia_inicio, vigencia_fim, valor, fundamento_legal) VALUES
('FOLHA_SALARIO_FAMILIA', '2025-01-01', '2025-12-31', '{"limiteRemuneracao":1906.04,"cota":65.00}',
 'Portaria Interministerial MPS/MF nº 6/2025 (salário-família)'),
('FOLHA_SALARIO_FAMILIA', '2026-01-01', NULL, '{"limiteRemuneracao":1980.38,"cota":67.54}',
 'Portaria Interministerial MPS/MF nº 13/2026 (salário-família)'),
('FOLHA_SALARIO_MINIMO', '2025-01-01', '2025-12-31', '{"valor":1518.00}', 'Decreto nº 12.342/2024 (salário mínimo 2025)'),
('FOLHA_SALARIO_MINIMO', '2026-01-01', NULL, '{"valor":1621.00}', 'Decreto nº 12.797/2025 (salário mínimo 2026)'),
('FOLHA_FGTS', '1990-05-01', NULL, '{"aliquota":8.0,"aliquotaAprendiz":2.0,"multaSemJustaCausa":40.0,"multaAcordo":20.0}',
 'Lei 8.036/1990, arts. 15 e 18 (depósito de 8%; multa de 40%); CLT art. 484-A (acordo: 20%); Lei 10.097/2000 (aprendiz: 2%)')
ON CONFLICT (chave, vigencia_inicio) DO NOTHING;

-- -----------------------------------------------------------------------------
-- Permissões da folha (códigos de Permissoes.Catalogo, grupo "Folha de pagamento")
-- -----------------------------------------------------------------------------
INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
 CROSS JOIN (VALUES ('FOLHA_CONSULTAR'), ('FOLHA_CADASTROS_MANTER'), ('FOLHA_PARAMETROS_MANTER'), ('FOLHA_EVENTOS_LANCAR'),
                    ('FOLHA_CALCULAR'), ('FOLHA_FECHAR'), ('FOLHA_REABRIR'), ('FOLHA_CONTABILIZAR'), ('FOLHA_ESOCIAL'),
                    ('FOLHA_RELATORIOS')) AS x(permissao)
 WHERE p.padrao AND (p.administrador OR p.nome = 'Contador')
ON CONFLICT DO NOTHING;

INSERT INTO core.perfil_permissao (perfil_id, permissao)
SELECT p.id, x.permissao
  FROM core.perfil p
 CROSS JOIN (VALUES ('FOLHA_CONSULTAR'), ('FOLHA_EVENTOS_LANCAR')) AS x(permissao)
 WHERE p.padrao AND p.nome = 'Assistente'
ON CONFLICT DO NOTHING;
