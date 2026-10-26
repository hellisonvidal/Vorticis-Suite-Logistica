-- Testes da migração 069 (Frota › Manutenção): situações da ordem de serviço, conclusão com data, itens de peça e serviço,
-- reprovação exige motivo, nota de compra sem valor ou repetida é recusada e tudo sai junto com a empresa.

DO $$
DECLARE
    v_emp BIGINT;
    v_vei BIGINT;
    v_os  BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_frota)
    VALUES ('63810427000256', 'Transportadora Manutenção LTDA', '2062', '4930202', 'SP', '3550308', '2020-01-01', FALSE, FALSE, FALSE, TRUE) RETURNING id INTO v_emp;
    INSERT INTO frota.veiculo (empresa_id, codigo, placa, tipo) VALUES (v_emp, 1, 'MNT1A23', 'TRUCK') RETURNING id INTO v_vei;

    INSERT INTO frota.item_manutencao (empresa_id, codigo, tipo, descricao, valor_referencia) VALUES (v_emp, 1, 'PECA', 'Filtro de óleo', 85);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.item_manutencao (empresa_id, codigo, tipo, descricao) VALUES (%s, 1, ''SERVICO'', ''Repetido'')', v_emp), 'uq_item_manutencao');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.item_manutencao (empresa_id, codigo, tipo, descricao) VALUES (%s, 2, ''PNEU'', ''Tipo errado'')', v_emp), 'ck_item_manutencao_tipo');

    INSERT INTO frota.ordem_servico (empresa_id, numero, veiculo_id, tipo, data, descricao) VALUES (v_emp, 1, v_vei, 'CORRETIVA', '2026-04-01', 'Freios') RETURNING id INTO v_os;
    INSERT INTO frota.ordem_item (ordem_id, tipo, descricao, quantidade, valor_unitario) VALUES (v_os, 'PECA', 'Lona de freio', 4, 120);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.ordem_item (ordem_id, tipo, descricao, quantidade, valor_unitario) VALUES (%s, ''SERVICO'', ''Sem quantidade'', 0, 10)', v_os), 'ordem_item_quantidade_check');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.ordem_servico SET situacao = ''EXECUCAO'' WHERE id = %s', v_os), 'ck_os_situacao');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.ordem_servico SET situacao = ''CONCLUIDA'' WHERE id = %s', v_os), 'ck_os_conclusao');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE frota.ordem_servico SET previsao = ''2026-03-01'' WHERE id = %s', v_os), 'ck_os_datas');

    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.autorizacao (ordem_id, decisao, valor) VALUES (%s, ''REPROVADA'', 480)', v_os), 'ck_autorizacao_motivo');
    INSERT INTO frota.autorizacao (ordem_id, decisao, valor) VALUES (v_os, 'APROVADA', 480);
    UPDATE frota.ordem_servico SET situacao = 'CONCLUIDA', conclusao = '2026-04-03' WHERE id = v_os;

    INSERT INTO frota.nota_compra (empresa_id, ordem_id, fornecedor, cnpj, numero, emissao, valor_pecas) VALUES (v_emp, v_os, 'Auto Peças Sul', '11222333000181', '123', '2026-04-02', 480);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.nota_compra (empresa_id, fornecedor, cnpj, numero, emissao, valor_pecas) VALUES (%s, ''Outro nome'', ''11222333000181'', ''123'', ''2026-04-02'', 10)', v_emp), 'uq_nota_compra');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.nota_compra (empresa_id, fornecedor, numero, emissao) VALUES (%s, ''Oficina'', ''9'', ''2026-04-02'')', v_emp), 'ck_nota_compra_valor');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO frota.nota_compra (empresa_id, fornecedor, numero, emissao, valor_servicos, chave) VALUES (%s, ''Oficina'', ''10'', ''2026-04-02'', 50, ''123'')', v_emp), 'nota_compra_chave_check');

    -- excluir a ordem mantém a nota (sem vínculo); excluir a empresa leva tudo
    DELETE FROM frota.ordem_servico WHERE id = v_os;
    IF (SELECT ordem_id FROM frota.nota_compra WHERE empresa_id = v_emp) IS NOT NULL THEN RAISE EXCEPTION 'nota continuou ligada à ordem excluída'; END IF;
    PERFORM core.fn_excluir_empresa(v_emp);
    IF EXISTS (SELECT 1 FROM frota.nota_compra WHERE empresa_id = v_emp) OR EXISTS (SELECT 1 FROM frota.item_manutencao WHERE empresa_id = v_emp)
    THEN RAISE EXCEPTION 'manutenção ficou após excluir a empresa'; END IF;
    RAISE NOTICE 'OK: Frota › Manutenção (itens, situações e conclusão da ordem, autorização, notas de compra e exclusão da empresa)';
END $$;

\echo 'TODOS OS TESTES PASSARAM'
