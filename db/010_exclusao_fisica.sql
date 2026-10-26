-- =============================================================================
-- Perseus Contábil — Migração 010: exclusão física de cadastros
--   Excluir uma empresa ou um usuário remove os registros do banco, para que o
--   mesmo CNPJ ou login possa ser cadastrado de novo sem conflito.
--   * Empresa: core.fn_excluir_empresa apaga, numa transação, tudo o que pertence
--     à empresa (lançamentos, plano de contas, períodos, sócios, acessos...).
--     As regras de imutabilidade dos lançamentos efetivados continuam valendo em
--     qualquer outra operação; só são liberadas para a empresa em exclusão.
--   * Usuário: referências históricas (quem lançou, fechou período, importou,
--     registro de acesso) passam a ficar vazias; a trilha de auditoria guarda o login.
-- =============================================================================

-- Empresa sendo excluída nesta transação (definida só por core.fn_excluir_empresa).
CREATE OR REPLACE FUNCTION core.excluindo_empresa(p_empresa BIGINT)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    SELECT COALESCE(current_setting('perseus.excluindo_empresa', true), '') = p_empresa::text
$$;

CREATE OR REPLACE FUNCTION contabil.fn_valida_lancamento()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_ex      contabil.exercicio%ROWTYPE;
    v_periodo contabil.status_periodo;
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'RASCUNHO' AND NOT core.excluindo_empresa(OLD.empresa_id) THEN
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

CREATE OR REPLACE FUNCTION contabil.fn_bloqueia_partida_efetivada()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status  contabil.status_lancamento;
    v_empresa BIGINT;
BEGIN
    SELECT status, empresa_id INTO v_status, v_empresa FROM contabil.lancamento
     WHERE id = CASE WHEN TG_OP = 'INSERT' THEN NEW.lancamento_id ELSE OLD.lancamento_id END;
    IF v_status <> 'RASCUNHO' AND NOT (TG_OP = 'DELETE' AND core.excluindo_empresa(v_empresa)) THEN
        RAISE EXCEPTION 'Lançamento já efetivado: corrija por estorno, não por alteração';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END $$;

-- Exclui a empresa e tudo o que pertence a ela. Retorna o número de lançamentos apagados.
CREATE OR REPLACE FUNCTION core.fn_excluir_empresa(p_empresa BIGINT)
RETURNS INTEGER LANGUAGE plpgsql AS $$
DECLARE
    v_lancamentos INTEGER;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM core.empresa WHERE id = p_empresa) THEN
        RAISE EXCEPTION 'Empresa % não encontrada', p_empresa;
    END IF;
    PERFORM set_config('perseus.excluindo_empresa', p_empresa::text, true);

    SELECT count(*) INTO v_lancamentos FROM contabil.lancamento WHERE empresa_id = p_empresa;
    DELETE FROM contabil.mapeamento_referencial WHERE empresa_id = p_empresa;
    DELETE FROM contabil.partida WHERE lancamento_id IN (SELECT id FROM contabil.lancamento WHERE empresa_id = p_empresa);
    DELETE FROM contabil.lancamento WHERE empresa_id = p_empresa;
    DELETE FROM contabil.saldo_mensal WHERE empresa_id = p_empresa;
    DELETE FROM contabil.historico_padrao WHERE empresa_id = p_empresa;
    DELETE FROM contabil.plano_contas WHERE empresa_id = p_empresa;
    DELETE FROM contabil.centro_custo WHERE empresa_id = p_empresa;
    DELETE FROM contabil.periodo WHERE exercicio_id IN (SELECT id FROM contabil.exercicio WHERE empresa_id = p_empresa);
    DELETE FROM contabil.exercicio WHERE empresa_id = p_empresa;
    DELETE FROM core.participante WHERE empresa_id = p_empresa;
    DELETE FROM core.socio WHERE empresa_id = p_empresa;
    DELETE FROM core.empresa_contabilidade WHERE empresa_id = p_empresa;
    DELETE FROM core.usuario_empresa WHERE empresa_id = p_empresa;
    DELETE FROM core.empresa_regime WHERE empresa_id = p_empresa;
    DELETE FROM core.estabelecimento WHERE empresa_id = p_empresa;
    DELETE FROM core.empresa WHERE id = p_empresa;

    PERFORM set_config('perseus.excluindo_empresa', '', true);
    RETURN v_lancamentos;
END $$;

-- Usuário: referências históricas ficam vazias quando ele é excluído.
ALTER TABLE contabil.periodo DROP CONSTRAINT periodo_fechado_por_fkey,
    ADD CONSTRAINT periodo_fechado_por_fkey FOREIGN KEY (fechado_por) REFERENCES core.usuario (id) ON DELETE SET NULL;
ALTER TABLE contabil.lancamento DROP CONSTRAINT lancamento_criado_por_fkey,
    ADD CONSTRAINT lancamento_criado_por_fkey FOREIGN KEY (criado_por) REFERENCES core.usuario (id) ON DELETE SET NULL;
ALTER TABLE core.registro_acesso DROP CONSTRAINT registro_acesso_usuario_id_fkey,
    ADD CONSTRAINT registro_acesso_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES core.usuario (id) ON DELETE SET NULL;
ALTER TABLE contabil.plano_referencial_importacao DROP CONSTRAINT plano_referencial_importacao_importado_por_fkey,
    ADD CONSTRAINT plano_referencial_importacao_importado_por_fkey FOREIGN KEY (importado_por) REFERENCES core.usuario (id) ON DELETE SET NULL;
ALTER TABLE core.configuracao_sistema DROP CONSTRAINT configuracao_sistema_alterado_por_fkey,
    ADD CONSTRAINT configuracao_sistema_alterado_por_fkey FOREIGN KEY (alterado_por) REFERENCES core.usuario (id) ON DELETE SET NULL;
ALTER TABLE core.usuario_empresa DROP CONSTRAINT usuario_empresa_usuario_id_fkey,
    ADD CONSTRAINT usuario_empresa_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES core.usuario (id) ON DELETE CASCADE;
