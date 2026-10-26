-- =============================================================================
-- Vorticis — Migração 049
--   Excluir e alterar lançamentos gravados (Contábil › Lançamentos).
--   * core.empresa.usa_estorno: a empresa corrige lançamentos por estorno (padrão, comportamento anterior)
--     ou altera/exclui o próprio lançamento (sem estorno).
--   * Os triggers de imutabilidade passam a admitir a exclusão e a alteração do lançamento quando a transação
--     declara a manutenção da empresa (vorticis.mantendo_lancamentos), definida só pelo repositório depois das
--     conferências da aplicação. Mesmo assim o banco recusa período fechado, exercício encerrado e ECD já gerada.
--   * contabil.fn_ajusta_saldo_lancamento mantém saldo_mensal; a trilha de auditoria (imutável) guarda o que foi
--     excluído/alterado (operações EXCLUI e ALTERA).
--   Número 049: a faixa do core/contábil (001–029) está esgotada e esta migração precisa rodar depois da 039, que também redefine fn_valida_lancamento.
-- =============================================================================

ALTER TABLE core.empresa ADD COLUMN usa_estorno BOOLEAN NOT NULL DEFAULT TRUE;

-- Empresa em manutenção de lançamentos nesta transação.
CREATE OR REPLACE FUNCTION core.mantendo_lancamentos(p_empresa BIGINT)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    SELECT COALESCE(current_setting('vorticis.mantendo_lancamentos', true), '') = p_empresa::text
$$;

-- Última defesa: lançamento só é excluído/alterado em período aberto, exercício não encerrado e sem ECD gerada.
CREATE OR REPLACE FUNCTION contabil.fn_confere_manutencao_lancamento(p_empresa BIGINT, p_exercicio BIGINT, p_data DATE)
RETURNS VOID LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_ex      contabil.exercicio%ROWTYPE;
    v_periodo contabil.status_periodo;
BEGIN
    SELECT * INTO v_ex FROM contabil.exercicio WHERE id = p_exercicio;
    IF v_ex.encerrado THEN
        RAISE EXCEPTION 'O exercício % está encerrado: lançamentos não podem ser excluídos nem alterados', v_ex.ano;
    END IF;
    IF EXISTS (SELECT 1 FROM contabil.ecd_gerada WHERE empresa_id = p_empresa AND ano = v_ex.ano) THEN
        RAISE EXCEPTION 'Já existe ECD gerada para %: lançamentos não podem ser excluídos nem alterados', v_ex.ano;
    END IF;
    SELECT p.status INTO v_periodo FROM contabil.periodo p
     WHERE p.exercicio_id = p_exercicio AND p.competencia = date_trunc('month', p_data)::date;
    IF v_periodo IS DISTINCT FROM 'ABERTO' THEN
        RAISE EXCEPTION 'Período de % está fechado ou não existe', to_char(p_data, 'MM/YYYY');
    END IF;
END $$;

CREATE OR REPLACE FUNCTION contabil.fn_valida_lancamento()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_ex      contabil.exercicio%ROWTYPE;
    v_periodo contabil.status_periodo;
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'RASCUNHO' AND NOT core.excluindo_empresa(OLD.empresa_id) THEN
            IF NOT core.mantendo_lancamentos(OLD.empresa_id) THEN
                RAISE EXCEPTION 'Lançamento % gravado só pode ser excluído pela função Excluir', OLD.numero;
            END IF;
            PERFORM contabil.fn_confere_manutencao_lancamento(OLD.empresa_id, OLD.exercicio_id, OLD.data_lancamento);
        END IF;
        RETURN OLD;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.status <> 'RASCUNHO' THEN
        -- Reorganização da numeração: só o número muda.
        IF core.reorganizando_lancamentos(OLD.empresa_id)
           AND (to_jsonb(NEW) - 'numero') = (to_jsonb(OLD) - 'numero') THEN
            RETURN NEW;
        END IF;
        IF core.mantendo_lancamentos(OLD.empresa_id) AND NEW.status = OLD.status AND NEW.numero IS NOT DISTINCT FROM OLD.numero
           AND NEW.exercicio_id = OLD.exercicio_id THEN
            -- Alteração sem estorno: confere o período de origem e segue para as conferências do novo conteúdo.
            PERFORM contabil.fn_confere_manutencao_lancamento(OLD.empresa_id, OLD.exercicio_id, OLD.data_lancamento);
        ELSIF NOT (OLD.status = 'EFETIVADO' AND NEW.status = 'ESTORNADO'
                   AND (to_jsonb(NEW) - 'status') = (to_jsonb(OLD) - 'status')) THEN
            RAISE EXCEPTION 'Lançamento % gravado é imutável (só é permitido marcar como estornado)', OLD.numero;
        ELSE
            RETURN NEW;
        END IF;
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

CREATE OR REPLACE FUNCTION contabil.fn_bloqueia_partida_efetivada()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status  contabil.status_lancamento;
    v_empresa BIGINT;
BEGIN
    SELECT status, empresa_id INTO v_status, v_empresa FROM contabil.lancamento
     WHERE id = CASE WHEN TG_OP = 'INSERT' THEN NEW.lancamento_id ELSE OLD.lancamento_id END;
    IF v_status <> 'RASCUNHO'
       AND NOT (TG_OP = 'DELETE' AND core.excluindo_empresa(v_empresa))
       AND NOT core.mantendo_lancamentos(v_empresa) THEN
        RAISE EXCEPTION 'Lançamento já gravado: altere ou exclua pela tela de lançamentos';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END $$;

-- Soma (p_sinal = 1) ou subtrai (p_sinal = -1) as partidas do lançamento em saldo_mensal.
CREATE OR REPLACE FUNCTION contabil.fn_ajusta_saldo_lancamento(p_lancamento BIGINT, p_sinal INT)
RETURNS VOID LANGUAGE sql AS $$
    INSERT INTO contabil.saldo_mensal AS s (empresa_id, conta_id, centro_custo_id, competencia, total_debito, total_credito)
    SELECT l.empresa_id, p.conta_id, COALESCE(p.centro_custo_id, 0), date_trunc('month', l.data_lancamento)::date,
           p_sinal * COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'D'), 0),
           p_sinal * COALESCE(SUM(p.valor) FILTER (WHERE p.ind_dc = 'C'), 0)
      FROM contabil.lancamento l JOIN contabil.partida p ON p.lancamento_id = l.id
     WHERE l.id = p_lancamento AND l.status <> 'RASCUNHO'
     GROUP BY l.empresa_id, p.conta_id, COALESCE(p.centro_custo_id, 0), date_trunc('month', l.data_lancamento)::date
    ON CONFLICT (empresa_id, conta_id, centro_custo_id, competencia) DO UPDATE
       SET total_debito  = s.total_debito  + EXCLUDED.total_debito,
           total_credito = s.total_credito + EXCLUDED.total_credito
$$;

-- Retrato do lançamento (cabeçalho e partidas) para a trilha de auditoria.
CREATE OR REPLACE FUNCTION contabil.fn_retrato_lancamento(p_lancamento BIGINT)
RETURNS JSONB LANGUAGE sql STABLE AS $$
    SELECT to_jsonb(l) || jsonb_build_object('partidas',
             (SELECT COALESCE(jsonb_agg(to_jsonb(p) - 'id' ORDER BY p.sequencia), '[]'::jsonb)
                FROM contabil.partida p WHERE p.lancamento_id = l.id))
      FROM contabil.lancamento l WHERE l.id = p_lancamento
$$;
