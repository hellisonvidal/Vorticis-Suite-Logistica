-- Testes das migrações 056 a 059 (Escritório, ERP e Condomínio). Executar depois das migrações, em banco descartável:
--   cat db/0*.sql db/tests/*.sql | psql -d vorticis_sql -v ON_ERROR_STOP=1 -q
-- Cobre: indicadores de módulo, pedido concluído imutável, sinal dos movimentos de estoque, reserva única por área e dia,
-- cobrança única por unidade e competência, cadastro próprio do escritório e honorário único por cliente e competência.

DO $$
DECLARE
    v_emp  BIGINT;
    v_cli  BIGINT;
    v_prod BIGINT;
    v_ped  BIGINT;
    v_uni  BIGINT;
    v_area BIGINT;
    v_pes  BIGINT;
BEGIN
    INSERT INTO core.empresa (cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ,
                              modulo_contabil, modulo_fiscal, modulo_folha, modulo_erp)
    VALUES ('27865757000102', 'Empresa ERP LTDA', '2062', '4751201', 'SP', '3550308', '2020-01-01', FALSE, FALSE, FALSE, TRUE) RETURNING id INTO v_emp;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE core.empresa SET modulo_erp = FALSE WHERE id = %s', v_emp), 'ck_empresa_algum_modulo');
    RAISE NOTICE 'OK: empresa só no ERP';

    INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj, cliente) VALUES (v_emp, '1', 'Cliente', '11222333000181', TRUE) RETURNING id INTO v_cli;
    INSERT INTO fiscal.produto (empresa_id, codigo, descricao, unidade) VALUES (v_emp, 1, 'Produto', 'UN') RETURNING id INTO v_prod;
    INSERT INTO erp.pedido (empresa_id, tipo, numero, data, participante_id) VALUES (v_emp, 'VENDA', 1, '2026-03-10', v_cli) RETURNING id INTO v_ped;
    INSERT INTO erp.pedido_item (pedido_id, produto_id, quantidade, valor_unitario) VALUES (v_ped, v_prod, 2, 10);
    UPDATE erp.pedido SET situacao = 'CONCLUIDO', concluido_em = '2026-03-11' WHERE id = v_ped;
    PERFORM pg_temp.espera_erro_dup(format('UPDATE erp.pedido SET observacao = ''x'' WHERE id = %s', v_ped), 'não pode ser alterado');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO erp.pedido_item (pedido_id, produto_id, quantidade, valor_unitario) VALUES (%s, %s, 1, 1)', v_ped, v_prod), 'itens não podem mudar');
    PERFORM pg_temp.espera_erro_dup(format('DELETE FROM erp.pedido WHERE id = %s', v_ped), 'não pode ser alterado');
    RAISE NOTICE 'OK: pedido concluído é imutável';

    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO erp.estoque_movimento (empresa_id, produto_id, data, tipo, quantidade) VALUES (%s, %s, ''2026-03-10'', ''SAIDA'', 5)', v_emp, v_prod), 'ck_estoque_sinal');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO erp.veiculo (empresa_id, placa, uf) VALUES (%s, ''abc1234'', ''SP'')', v_emp), 'ck_veiculo_placa');
    RAISE NOTICE 'OK: estoque e veículo validados';

    INSERT INTO condominio.unidade (empresa_id, bloco, numero, fracao_ideal) VALUES (v_emp, 'A', '101', 0.5) RETURNING id INTO v_uni;
    INSERT INTO condominio.area_comum (empresa_id, nome) VALUES (v_emp, 'Salão de festas') RETURNING id INTO v_area;
    INSERT INTO condominio.reserva (area_id, unidade_id, data) VALUES (v_area, v_uni, '2026-04-04');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO condominio.reserva (area_id, unidade_id, data) VALUES (%s, %s, ''2026-04-04'')', v_area, v_uni), 'uq_reserva_area_dia');
    INSERT INTO condominio.cobranca (empresa_id, unidade_id, competencia, vencimento, valor) VALUES (v_emp, v_uni, '2026-04-01', '2026-04-10', 500);
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO condominio.cobranca (empresa_id, unidade_id, competencia, vencimento, valor) VALUES (%s, %s, ''2026-04-01'', ''2026-04-10'', 500)', v_emp, v_uni), 'uq_cobranca');
    PERFORM pg_temp.espera_erro_dup(format('UPDATE condominio.cobranca SET situacao = ''PAGA'' WHERE unidade_id = %s', v_uni), 'ck_cobranca_paga');
    RAISE NOTICE 'OK: condomínio (reserva única, cobrança única e pagamento completo)';

    INSERT INTO escritorio.pessoa (empresa_id, codigo, nome, documento) VALUES (v_emp, 1, 'Cliente do escritório', '11222333000181') RETURNING id INTO v_pes;
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO escritorio.pessoa (empresa_id, codigo, nome, documento) VALUES (%s, 2, ''Outro'', ''11222333000181'')', v_emp), 'uq_pessoa_documento');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO escritorio.pessoa (empresa_id, codigo, nome, cliente, fornecedor) VALUES (%s, 3, ''Nada'', FALSE, FALSE)', v_emp), 'ck_pessoa_papel');
    INSERT INTO escritorio.honorario_cobranca (empresa_id, pessoa_id, competencia, valor, vencimento) VALUES (v_emp, v_pes, '2026-04-01', 800, '2026-04-10');
    PERFORM pg_temp.espera_erro_dup(format('INSERT INTO escritorio.honorario_cobranca (empresa_id, pessoa_id, competencia, valor, vencimento) VALUES (%s, %s, ''2026-04-01'', 800, ''2026-04-10'')', v_emp, v_pes), 'uq_honorario_cobranca_pessoa');
    RAISE NOTICE 'OK: cadastro próprio do escritório e honorário único por cliente e competência';

    INSERT INTO contabil.layout_importacao (nome, configuracao) VALUES ('Banco X', '{}');
    INSERT INTO contabil.layout_importacao (nome, configuracao, tipo) VALUES ('Banco X', '{}', 'EXTRATO');
    PERFORM pg_temp.espera_erro_dup('INSERT INTO contabil.layout_importacao (nome, configuracao, tipo) VALUES (''banco x'', ''{}'', ''EXTRATO'')', 'uq_layout_importacao_nome');
    DELETE FROM contabil.layout_importacao WHERE nome = 'Banco X';
    RAISE NOTICE 'OK: layout de extrato com nome único por tipo (planilha e extrato podem ter o mesmo nome)';
END $$;

