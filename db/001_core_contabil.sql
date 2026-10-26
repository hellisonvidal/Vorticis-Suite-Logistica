-- =============================================================================
-- Perseus — Sistema de Escrituração Fiscal Contábil
-- Migração 001: núcleo cadastral + módulo contábil
-- Banco alvo: PostgreSQL 16+ (instalação local no Windows)
--
-- Convenções:
--   * Valores monetários: NUMERIC(18,2). Nunca float/double/real.
--   * Quantidades/alíquotas: NUMERIC(18,6) / NUMERIC(9,4).
--   * Toda tabela com dados de negócio carrega empresa_id (multiempresa).
--   * Nada contábil efetivado é apagado ou alterado: corrige-se por ESTORNO.
--   * Códigos de layout (ECD/ECF/eSocial) ficam em colunas com o mesmo nome
--     usado no leiaute oficial, para facilitar a geração dos arquivos SPED.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS core;
CREATE SCHEMA IF NOT EXISTS contabil;

-- -----------------------------------------------------------------------------
-- Tipos
-- -----------------------------------------------------------------------------
CREATE TYPE core.regime_tributario AS ENUM (
    'MEI', 'SIMPLES_NACIONAL', 'LUCRO_PRESUMIDO', 'LUCRO_REAL',
    'LUCRO_ARBITRADO', 'IMUNE_ISENTA'
);

CREATE TYPE contabil.indicador_dc     AS ENUM ('D', 'C');
CREATE TYPE contabil.tipo_conta       AS ENUM ('S', 'A');           -- Sintética / Analítica (ECD I050.IND_CTA)
CREATE TYPE contabil.status_periodo   AS ENUM ('ABERTO', 'FECHADO');
CREATE TYPE contabil.status_lancamento AS ENUM ('RASCUNHO', 'EFETIVADO', 'ESTORNADO');
-- ECD I200.IND_LCTO: N = normal, E = encerramento de resultado, X = extemporâneo
CREATE TYPE contabil.ind_lcto         AS ENUM ('N', 'E', 'X');
CREATE TYPE contabil.origem_lancamento AS ENUM (
    'MANUAL', 'NFE', 'NFSE', 'CTE', 'EXTRATO', 'PLANILHA', 'FOLHA',
    'ENCERRAMENTO', 'ESTORNO', 'SALDO_INICIAL', 'DEPRECIACAO', 'APURACAO_TRIBUTO'
);

-- -----------------------------------------------------------------------------
-- Núcleo: empresas, estabelecimentos, regime, usuários, auditoria, parâmetros
-- -----------------------------------------------------------------------------
CREATE TABLE core.empresa (
    id                  BIGSERIAL PRIMARY KEY,
    cnpj                CHAR(14)     NOT NULL UNIQUE,          -- raiz + filial 0001 da matriz
    razao_social        VARCHAR(150) NOT NULL,
    nome_fantasia       VARCHAR(150),
    natureza_juridica   CHAR(4)      NOT NULL,                 -- tabela CONCLA (ex.: 2062 = LTDA)
    cnae_principal      CHAR(7)      NOT NULL,
    uf                  CHAR(2)      NOT NULL,
    cod_municipio_ibge  CHAR(7)      NOT NULL,
    inscricao_estadual  VARCHAR(20),
    inscricao_municipal VARCHAR(20),
    nire                VARCHAR(11),                           -- ECD 0000.NIRE (Junta Comercial)
    data_inicio_ativ    DATE         NOT NULL,
    ativa               BOOLEAN      NOT NULL DEFAULT TRUE,
    criado_em           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT ck_empresa_cnpj CHECK (cnpj ~ '^[0-9]{14}$')
);

CREATE TABLE core.estabelecimento (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    cnpj                CHAR(14)     NOT NULL UNIQUE,
    matriz              BOOLEAN      NOT NULL DEFAULT FALSE,
    uf                  CHAR(2)      NOT NULL,
    cod_municipio_ibge  CHAR(7)      NOT NULL,
    inscricao_estadual  VARCHAR(20),
    inscricao_municipal VARCHAR(20)
);

-- O regime muda por ano-calendário: guardar histórico, nunca sobrescrever.
CREATE TABLE core.empresa_regime (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    regime              core.regime_tributario NOT NULL,
    vigencia_inicio     DATE         NOT NULL,
    vigencia_fim        DATE,
    apuracao_irpj       VARCHAR(20),                           -- TRIMESTRAL | ANUAL_ESTIMATIVA (Lucro Real)
    regime_caixa        BOOLEAN      NOT NULL DEFAULT FALSE,   -- Presumido pode optar por caixa (IN RFB 1.700/2017)
    CONSTRAINT ck_regime_vig CHECK (vigencia_fim IS NULL OR vigencia_fim >= vigencia_inicio),
    CONSTRAINT uq_regime_inicio UNIQUE (empresa_id, vigencia_inicio)
);

CREATE TABLE core.usuario (
    id                  BIGSERIAL PRIMARY KEY,
    login               VARCHAR(60)  NOT NULL UNIQUE,
    nome                VARCHAR(150) NOT NULL,
    cpf                 CHAR(11),
    crc                 VARCHAR(20),                           -- contador responsável (ECD J930)
    senha_hash          TEXT         NOT NULL,                 -- Argon2id
    ativo               BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE core.usuario_empresa (
    usuario_id          BIGINT       NOT NULL REFERENCES core.usuario(id),
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    perfil              VARCHAR(30)  NOT NULL,                 -- ADMIN | CONTADOR | ASSISTENTE | CONSULTA
    PRIMARY KEY (usuario_id, empresa_id)
);

-- Terceiros (clientes, fornecedores, bancos, sócios, empregados) — ECD 0150, NF-e, folha.
CREATE TABLE core.participante (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    codigo              VARCHAR(60)  NOT NULL,                 -- ECD 0150.COD_PART
    nome                VARCHAR(150) NOT NULL,
    cpf_cnpj            VARCHAR(14),
    cod_pais            CHAR(5)      NOT NULL DEFAULT '01058', -- tabela BACEN
    uf                  CHAR(2),
    cod_municipio_ibge  CHAR(7),
    CONSTRAINT uq_participante UNIQUE (empresa_id, codigo)
);

-- Alíquotas, tetos, faixas (INSS, IRRF, salário mínimo, presunções, anexos do Simples,
-- alíquotas CBS/IBS de transição...). Sempre versionados por vigência — nunca hardcode.
CREATE TABLE core.parametro_legal (
    id                  BIGSERIAL PRIMARY KEY,
    chave               VARCHAR(80)  NOT NULL,                 -- ex.: 'INSS_EMPREGADO_FAIXAS'
    vigencia_inicio     DATE         NOT NULL,
    vigencia_fim        DATE,
    valor               JSONB        NOT NULL,
    fundamento_legal    VARCHAR(250) NOT NULL,                 -- ex.: 'Portaria Interministerial MPS/MF nº ...'
    CONSTRAINT uq_parametro UNIQUE (chave, vigencia_inicio)
);

-- Trilha de auditoria encadeada por hash (detecção de adulteração direta no banco).
CREATE TABLE core.auditoria (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT,
    usuario_login       VARCHAR(60)  NOT NULL DEFAULT current_user,
    ocorrido_em         TIMESTAMPTZ  NOT NULL DEFAULT clock_timestamp(),
    tabela              VARCHAR(80)  NOT NULL,
    registro_id         BIGINT,
    operacao            VARCHAR(10)  NOT NULL,
    dados_antes         JSONB,
    dados_depois        JSONB,
    hash_anterior       CHAR(64),
    hash_registro       CHAR(64)     NOT NULL
);

-- -----------------------------------------------------------------------------
-- Estrutura contábil: exercício, períodos, plano referencial, plano de contas
-- -----------------------------------------------------------------------------
CREATE TABLE contabil.exercicio (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    ano                 SMALLINT     NOT NULL,
    data_inicio         DATE         NOT NULL,                 -- pode não ser 01/01 (início de atividade, cisão...)
    data_fim            DATE         NOT NULL,
    encerrado           BOOLEAN      NOT NULL DEFAULT FALSE,
    CONSTRAINT uq_exercicio UNIQUE (empresa_id, ano),
    CONSTRAINT ck_exercicio CHECK (data_fim >= data_inicio)
);

CREATE TABLE contabil.periodo (
    id                  BIGSERIAL PRIMARY KEY,
    exercicio_id        BIGINT       NOT NULL REFERENCES contabil.exercicio(id),
    competencia         DATE         NOT NULL,                 -- 1º dia do mês
    status              contabil.status_periodo NOT NULL DEFAULT 'ABERTO',
    fechado_em          TIMESTAMPTZ,
    fechado_por         BIGINT       REFERENCES core.usuario(id),
    CONSTRAINT uq_periodo UNIQUE (exercicio_id, competencia),
    CONSTRAINT ck_competencia CHECK (EXTRACT(DAY FROM competencia) = 1)
);

-- Plano de contas referencial publicado pela RFB (tabelas dinâmicas do PVA ECD/ECF).
-- Importado dos arquivos oficiais, por ano e por tipo de plano (COD_PLAN_REF / tabela ECF).
CREATE TABLE contabil.plano_referencial (
    id                  BIGSERIAL PRIMARY KEY,
    cod_plan_ref        VARCHAR(4)   NOT NULL,                 -- ECD 0000.COD_PLAN_REF / tabela ECF (L100A, L300A, P100, P150...)
    ano_vigencia        SMALLINT     NOT NULL,
    codigo              VARCHAR(30)  NOT NULL,                 -- ex.: 1.01.01.01.01
    descricao           VARCHAR(400) NOT NULL,
    tipo                contabil.tipo_conta NOT NULL,
    nivel               SMALLINT     NOT NULL,
    codigo_pai          VARCHAR(30),
    cod_nat             CHAR(2)      NOT NULL,
    CONSTRAINT uq_plano_ref UNIQUE (cod_plan_ref, ano_vigencia, codigo)
);

CREATE TABLE contabil.plano_contas (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    codigo              VARCHAR(30)  NOT NULL,                 -- máscara configurável, ex.: 1.1.01.001
    codigo_reduzido     INTEGER,                               -- atalho de digitação
    descricao           VARCHAR(200) NOT NULL,
    tipo                contabil.tipo_conta NOT NULL,          -- só 'A' recebe lançamento
    nivel               SMALLINT     NOT NULL,
    conta_pai_id        BIGINT       REFERENCES contabil.plano_contas(id),
    -- ECD I050.COD_NAT: 01 Ativo | 02 Passivo | 03 PL | 04 Resultado | 05 Compensação | 09 Outras
    cod_nat             CHAR(2)      NOT NULL,
    natureza_saldo      contabil.indicador_dc NOT NULL,        -- devedora / credora (alerta de saldo invertido)
    exige_centro_custo  BOOLEAN      NOT NULL DEFAULT FALSE,
    exige_participante  BOOLEAN      NOT NULL DEFAULT FALSE,
    data_inclusao       DATE         NOT NULL,                 -- ECD I050.DT_ALT
    ativa               BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_conta UNIQUE (empresa_id, codigo),
    CONSTRAINT ck_cod_nat CHECK (cod_nat IN ('01','02','03','04','05','09')),
    CONSTRAINT ck_sintetica_raiz CHECK (nivel > 1 OR tipo = 'S'), -- ECD: nível 1 sempre sintético
    CONSTRAINT ck_pai CHECK ((nivel = 1) = (conta_pai_id IS NULL))
);
CREATE INDEX ix_conta_pai ON contabil.plano_contas(conta_pai_id);

CREATE TABLE contabil.centro_custo (                           -- ECD I100
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    codigo              VARCHAR(30)  NOT NULL,
    descricao           VARCHAR(150) NOT NULL,
    centro_pai_id       BIGINT       REFERENCES contabil.centro_custo(id),
    data_inclusao       DATE         NOT NULL,
    ativo               BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_ccusto UNIQUE (empresa_id, codigo)
);

-- De/para conta societária -> conta referencial (ECD I051). Uma conta analítica
-- mapeia para exatamente uma referencial por ano/plano (opcionalmente por centro de custo).
CREATE TABLE contabil.mapeamento_referencial (
    id                  BIGSERIAL PRIMARY KEY,
    conta_id            BIGINT       NOT NULL REFERENCES contabil.plano_contas(id),
    centro_custo_id     BIGINT       REFERENCES contabil.centro_custo(id),
    plano_referencial_id BIGINT      NOT NULL REFERENCES contabil.plano_referencial(id),
    ano_vigencia        SMALLINT     NOT NULL
);
CREATE UNIQUE INDEX uq_mapeamento
    ON contabil.mapeamento_referencial (conta_id, COALESCE(centro_custo_id, 0), ano_vigencia);

CREATE TABLE contabil.historico_padrao (                       -- ECD I075
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    codigo              VARCHAR(20)  NOT NULL,
    descricao           VARCHAR(250) NOT NULL,                 -- aceita placeholders: {documento}, {participante}
    CONSTRAINT uq_hist UNIQUE (empresa_id, codigo)
);

-- -----------------------------------------------------------------------------
-- Lançamentos (cabeçalho = ECD I200) e partidas (linhas = ECD I250)
-- -----------------------------------------------------------------------------
CREATE TABLE contabil.lancamento (
    id                  BIGSERIAL PRIMARY KEY,
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    exercicio_id        BIGINT       NOT NULL REFERENCES contabil.exercicio(id),
    numero              BIGINT,                                -- ECD I200.NUM_LCTO — atribuído na efetivação, sem lacunas
    data_lancamento     DATE         NOT NULL,                 -- ECD I200.DT_LCTO (competência)
    ind_lcto            contabil.ind_lcto NOT NULL DEFAULT 'N',
    data_lcto_ext       DATE,                                  -- ECD I200.DT_LCTO_EXT (extemporâneo)
    origem              contabil.origem_lancamento NOT NULL,
    documento_origem_id BIGINT,                                -- FK lógica p/ fiscal.documento / banco.transacao
    status              contabil.status_lancamento NOT NULL DEFAULT 'RASCUNHO',
    estorno_de_id       BIGINT       REFERENCES contabil.lancamento(id),
    criado_por          BIGINT       REFERENCES core.usuario(id),
    criado_em           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    efetivado_em        TIMESTAMPTZ,
    CONSTRAINT uq_lcto_numero UNIQUE (empresa_id, exercicio_id, numero),
    CONSTRAINT ck_ext CHECK ((ind_lcto = 'X') = (data_lcto_ext IS NOT NULL)),
    CONSTRAINT ck_numero_efetivado CHECK (status = 'RASCUNHO' OR numero IS NOT NULL)
);
CREATE INDEX ix_lcto_data ON contabil.lancamento(empresa_id, data_lancamento);

CREATE TABLE contabil.partida (
    id                  BIGSERIAL PRIMARY KEY,
    lancamento_id       BIGINT       NOT NULL REFERENCES contabil.lancamento(id),
    sequencia           SMALLINT     NOT NULL,
    conta_id            BIGINT       NOT NULL REFERENCES contabil.plano_contas(id),
    centro_custo_id     BIGINT       REFERENCES contabil.centro_custo(id),
    participante_id     BIGINT       REFERENCES core.participante(id),
    ind_dc              contabil.indicador_dc NOT NULL,
    valor               NUMERIC(18,2) NOT NULL,
    historico_padrao_id BIGINT       REFERENCES contabil.historico_padrao(id),
    historico           VARCHAR(500) NOT NULL,                 -- ECD I250.HIST (obrigatório)
    num_arquivo         VARCHAR(100),                          -- ECD I250.NUM_ARQ (nº doc/arquivo de suporte)
    CONSTRAINT ck_valor_positivo CHECK (valor > 0),            -- sinal vem de ind_dc, nunca do valor
    CONSTRAINT uq_partida_seq UNIQUE (lancamento_id, sequencia)
);
CREATE INDEX ix_partida_conta ON contabil.partida(conta_id);

-- Saldos mensais por conta/centro de custo: base para Razão, Balancete, ECD I155 e relatórios.
-- Mantido pela camada de aplicação na efetivação/estorno (ou recalculável a qualquer momento).
CREATE TABLE contabil.saldo_mensal (
    empresa_id          BIGINT       NOT NULL REFERENCES core.empresa(id),
    conta_id            BIGINT       NOT NULL REFERENCES contabil.plano_contas(id),
    centro_custo_id     BIGINT       NOT NULL DEFAULT 0,       -- 0 = sem centro de custo
    competencia         DATE         NOT NULL,
    total_debito        NUMERIC(18,2) NOT NULL DEFAULT 0,
    total_credito       NUMERIC(18,2) NOT NULL DEFAULT 0,
    PRIMARY KEY (empresa_id, conta_id, centro_custo_id, competencia)
);

-- =============================================================================
-- REGRAS DE INTEGRIDADE NO BANCO (última linha de defesa; a aplicação valida antes)
-- =============================================================================

-- R1. Partidas dobradas: em todo lançamento EFETIVADO, Σ Débitos = Σ Créditos,
--     com ao menos uma partida de cada lado. Constraint trigger DEFERRABLE:
--     o teste roda no COMMIT, permitindo inserir cabeçalho e linhas em qualquer ordem.
CREATE OR REPLACE FUNCTION contabil.fn_valida_partidas_dobradas()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_lanc_id  BIGINT;
    v_status   contabil.status_lancamento;
    v_deb      NUMERIC(18,2);
    v_cred     NUMERIC(18,2);
    v_qtd_d    INT;
    v_qtd_c    INT;
BEGIN
    IF TG_TABLE_NAME = 'lancamento' THEN
        v_lanc_id := NEW.id;
    ELSIF TG_OP = 'DELETE' THEN
        v_lanc_id := OLD.lancamento_id;
    ELSE
        v_lanc_id := NEW.lancamento_id;
    END IF;

    SELECT status INTO v_status FROM contabil.lancamento WHERE id = v_lanc_id;
    IF v_status IS NULL OR v_status = 'RASCUNHO' THEN
        RETURN NULL;                                           -- rascunho pode estar desbalanceado
    END IF;

    SELECT COALESCE(SUM(valor) FILTER (WHERE ind_dc = 'D'), 0),
           COALESCE(SUM(valor) FILTER (WHERE ind_dc = 'C'), 0),
           COUNT(*) FILTER (WHERE ind_dc = 'D'),
           COUNT(*) FILTER (WHERE ind_dc = 'C')
      INTO v_deb, v_cred, v_qtd_d, v_qtd_c
      FROM contabil.partida
     WHERE lancamento_id = v_lanc_id;

    IF v_qtd_d = 0 OR v_qtd_c = 0 THEN
        RAISE EXCEPTION 'Lançamento %: exige ao menos uma partida a débito e uma a crédito', v_lanc_id
              USING ERRCODE = 'check_violation';
    END IF;

    IF v_deb <> v_cred THEN
        RAISE EXCEPTION 'Lançamento %: débitos (%) <> créditos (%), diferença %',
              v_lanc_id, v_deb, v_cred, v_deb - v_cred
              USING ERRCODE = 'check_violation';
    END IF;

    RETURN NULL;
END $$;

CREATE CONSTRAINT TRIGGER trg_partidas_dobradas_partida
    AFTER INSERT OR UPDATE OR DELETE ON contabil.partida
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_valida_partidas_dobradas();

CREATE CONSTRAINT TRIGGER trg_partidas_dobradas_lancamento
    AFTER INSERT OR UPDATE OF status ON contabil.lancamento
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_valida_partidas_dobradas();

-- R2. Partida só em conta ANALÍTICA, ativa, da mesma empresa do lançamento,
--     existente na data do lançamento; centro de custo/participante quando exigidos.
CREATE OR REPLACE FUNCTION contabil.fn_valida_partida()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_lanc  contabil.lancamento%ROWTYPE;
    v_conta contabil.plano_contas%ROWTYPE;
BEGIN
    SELECT * INTO v_lanc  FROM contabil.lancamento  WHERE id = NEW.lancamento_id;
    SELECT * INTO v_conta FROM contabil.plano_contas WHERE id = NEW.conta_id;

    IF v_conta.empresa_id <> v_lanc.empresa_id THEN
        RAISE EXCEPTION 'Conta % não pertence à empresa do lançamento', v_conta.codigo;
    END IF;
    IF v_conta.tipo <> 'A' THEN
        RAISE EXCEPTION 'Conta % é sintética e não aceita lançamentos', v_conta.codigo;
    END IF;
    IF NOT v_conta.ativa THEN
        RAISE EXCEPTION 'Conta % está inativa', v_conta.codigo;
    END IF;
    IF v_conta.data_inclusao > v_lanc.data_lancamento THEN
        RAISE EXCEPTION 'Conta % incluída em % — posterior à data do lançamento %',
              v_conta.codigo, v_conta.data_inclusao, v_lanc.data_lancamento;
    END IF;
    IF v_conta.exige_centro_custo AND NEW.centro_custo_id IS NULL THEN
        RAISE EXCEPTION 'Conta % exige centro de custo', v_conta.codigo;
    END IF;
    IF v_conta.exige_participante AND NEW.participante_id IS NULL THEN
        RAISE EXCEPTION 'Conta % exige participante', v_conta.codigo;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_valida_partida
    BEFORE INSERT OR UPDATE ON contabil.partida
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_valida_partida();

-- R3. Imutabilidade: partidas de lançamento efetivado/estornado não mudam nem somem.
CREATE OR REPLACE FUNCTION contabil.fn_bloqueia_partida_efetivada()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status contabil.status_lancamento;
BEGIN
    SELECT status INTO v_status FROM contabil.lancamento
     WHERE id = CASE WHEN TG_OP = 'INSERT' THEN NEW.lancamento_id ELSE OLD.lancamento_id END;
    IF v_status <> 'RASCUNHO' THEN
        RAISE EXCEPTION 'Lançamento já efetivado: corrija por estorno, não por alteração';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END $$;

CREATE TRIGGER trg_bloqueia_partida_efetivada
    BEFORE INSERT OR UPDATE OR DELETE ON contabil.partida
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_bloqueia_partida_efetivada();

-- R4. Transições de status do lançamento e período aberto.
--     RASCUNHO -> EFETIVADO (período ABERTO) | EFETIVADO -> ESTORNADO. Nada mais.
CREATE OR REPLACE FUNCTION contabil.fn_valida_lancamento()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_ex      contabil.exercicio%ROWTYPE;
    v_periodo contabil.status_periodo;
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'RASCUNHO' THEN
            RAISE EXCEPTION 'Lançamento % efetivado não pode ser excluído', OLD.numero;
        END IF;
        RETURN OLD;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.status <> 'RASCUNHO' THEN
        IF NOT (OLD.status = 'EFETIVADO' AND NEW.status = 'ESTORNADO'
                AND (to_jsonb(NEW) - 'status') = (to_jsonb(OLD) - 'status')) THEN
            RAISE EXCEPTION 'Lançamento % efetivado é imutável (só é permitido marcar como ESTORNADO)', OLD.numero;
        END IF;
        RETURN NEW;
    END IF;

    SELECT * INTO v_ex FROM contabil.exercicio WHERE id = NEW.exercicio_id;
    IF v_ex.empresa_id <> NEW.empresa_id
       OR NEW.data_lancamento NOT BETWEEN v_ex.data_inicio AND v_ex.data_fim THEN
        RAISE EXCEPTION 'Data % fora do exercício %', NEW.data_lancamento, v_ex.ano;
    END IF;

    IF NEW.status = 'EFETIVADO' THEN
        SELECT p.status INTO v_periodo
          FROM contabil.periodo p
         WHERE p.exercicio_id = NEW.exercicio_id
           AND p.competencia = date_trunc('month', NEW.data_lancamento)::date;
        IF v_periodo IS DISTINCT FROM 'ABERTO' THEN
            RAISE EXCEPTION 'Período de % está fechado ou não existe', to_char(NEW.data_lancamento, 'MM/YYYY');
        END IF;
        IF NEW.ind_lcto = 'E' AND NEW.data_lancamento <> v_ex.data_fim THEN
            RAISE EXCEPTION 'Lançamento de encerramento deve ter a data final do exercício';
        END IF;
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_valida_lancamento
    BEFORE INSERT OR UPDATE OR DELETE ON contabil.lancamento
    FOR EACH ROW EXECUTE FUNCTION contabil.fn_valida_lancamento();

-- -----------------------------------------------------------------------------
-- Visões de apoio
-- -----------------------------------------------------------------------------
-- Livro Razão (por conta, ordenado) — apenas lançamentos que contam na escrituração.
CREATE VIEW contabil.vw_razao AS
SELECT l.empresa_id, p.conta_id, l.data_lancamento, l.numero, p.sequencia,
       p.ind_dc, p.valor, p.historico, p.centro_custo_id, l.id AS lancamento_id
  FROM contabil.partida p
  JOIN contabil.lancamento l ON l.id = p.lancamento_id
 WHERE l.status IN ('EFETIVADO', 'ESTORNADO');                 -- o estornado E o estorno ficam no livro

-- Balancete por competência (movimento analítico; totalização sintética na aplicação).
CREATE VIEW contabil.vw_balancete_mensal AS
SELECT l.empresa_id, p.conta_id,
       date_trunc('month', l.data_lancamento)::date AS competencia,
       SUM(p.valor) FILTER (WHERE p.ind_dc = 'D') AS total_debito,
       SUM(p.valor) FILTER (WHERE p.ind_dc = 'C') AS total_credito
  FROM contabil.partida p
  JOIN contabil.lancamento l ON l.id = p.lancamento_id
 WHERE l.status IN ('EFETIVADO', 'ESTORNADO')
 GROUP BY 1, 2, 3;
