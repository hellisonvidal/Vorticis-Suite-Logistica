using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.ViewModels;

namespace Vorticis.Desktop.Tests;

/// <summary>Consistência do menu do módulo com o casco: toda chave de menu e de atalho tem uma tela registrada.</summary>
public class MenusTests
{
    private static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) =>
        itens.SelectMany(i => i.Filhos is null ? [i] : Folhas(i.Filhos));

    [Fact]
    public void Toda_chave_de_menu_do_contabil_tem_tela_registrada()
    {
        var vm = new MainViewModel();
        var chaves = Folhas(vm.Menus).Where(i => !i.EhSeparador && i.Habilitado && i.Chave is not null && i.Chave != TelasComuns.Sair)
            .Select(i => i.Chave!).ToList();
        Assert.Contains(Telas.Lancamento, chaves);
        Assert.Contains(Telas.Empresas, chaves);
        Assert.All(chaves, c => Assert.True(vm.ConheceTela(c), c));
        Assert.All(vm.Atalhos.Where(a => a.Chave is not null), a => Assert.True(vm.ConheceTela(a.Chave!), a.Chave));
    }

    [Fact]
    public void Fiscal_tem_menu_comum_e_itens_em_breve_desabilitados()
    {
        MainViewModelBase vm = new Vorticis.Fiscal.Desktop.MainViewModel();
        Assert.StartsWith("Vorticis ", vm.Modulo.Nome);
        Assert.Equal("_Cadastros", vm.Menus[0].Titulo);
        Assert.Equal("_Ajuda", vm.Menus[^1].Titulo);
        var proprios = vm.Menus.Skip(1).SkipLast(2);
        Assert.NotEmpty(proprios);
        // item habilitado tem chave; o que falta fica "em breve" (desabilitado e sem chave)
        Assert.All(Folhas(proprios).Where(i => !i.EhSeparador), i => Assert.Equal(i.Chave is not null, i.Habilitado));
        Assert.All(Folhas(vm.Menus).Where(i => i.Chave is not null && i.Chave != TelasComuns.Sair), i => Assert.True(vm.ConheceTela(i.Chave!), i.Chave));
    }

    [Fact]
    public void Cada_modulo_tem_cor_e_icone_proprios_para_nao_confundir_os_sistemas()
    {
        MainViewModelBase[] modulos = [new MainViewModel(), new Vorticis.Fiscal.Desktop.MainViewModel(), new Vorticis.Folha.Desktop.MainViewModel(), new Vorticis.Erp.Desktop.MainViewModel(), new Vorticis.Frota.Desktop.MainViewModel(), new Vorticis.Logistica.Desktop.MainViewModel()];
        Assert.Equal(["#1B7F5A", "#D97706", "#7E22CE", "#0891B2", "#475569", "#0F766E"], modulos.Select(m => m.Modulo.CorDestaque));
        Assert.Equal(modulos.Length, modulos.Select(m => m.Modulo.CorBarra).Distinct().Count());
        Assert.Equal("#14532D", modulos[0].Modulo.CorBarra);                      // verde-escuro: não confunde com o azul do Escritório
        // a cor chega ao casco como pincel (faixa sob os menus, login e tela inicial)
        Assert.Equal(Avalonia.Media.Color.Parse("#D97706"), ((Avalonia.Media.ISolidColorBrush)modulos[1].PincelDestaque).Color);
        Assert.Equal(Avalonia.Media.Color.Parse("#3B0764"), ((Avalonia.Media.ISolidColorBrush)modulos[2].PincelBarra).Color);
        Assert.Same(modulos[1].PincelDestaque, modulos[1].Login.PincelDestaque);
        Assert.Same(modulos[2].PincelDestaque, modulos[2].Inicio.PincelDestaque);
        // um ícone por executável (fonte em instalador/icone, embutido no casco e usado pelo exe e pelo instalador)
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !Directory.Exists(Path.Combine(pasta.FullName, "instalador", "icone"))) pasta = pasta.Parent;
        Assert.NotNull(pasta);
        var tamanhos = modulos.Select(m => new FileInfo(Path.Combine(pasta.FullName, "instalador", "icone", $"{m.Modulo.NomeExecutavel}.ico")))
                              .Select(f => { Assert.True(f.Exists, f.Name); return File.ReadAllBytes(f.FullName); }).ToList();
        Assert.Equal(modulos.Length, tamanhos.Select(b => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b))).Distinct().Count());   // ícones diferentes
        // o instalador tem ícone próprio, diferente dos módulos, e as cores do gerador de ícones são as dos módulos
        var instalador = File.ReadAllBytes(Path.Combine(pasta.FullName, "instalador", "icone", "VorticisInstalador.ico"));
        Assert.DoesNotContain(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(instalador)),
                              tamanhos.Select(b => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b))));
        Assert.Contains(@"SetupIconFile=icone\VorticisLogisticaInstalador.ico", File.ReadAllText(Path.Combine(pasta.FullName, "instalador", "VorticisLogistica.iss")));
        var gerador = File.ReadAllText(Path.Combine(pasta.FullName, "instalador", "icone", "gerar_icone.py"));
        Assert.All(modulos, m => Assert.Contains($"\"{m.Modulo.NomeExecutavel}\": (\"{m.Modulo.CorDestaque}\", \"{m.Modulo.CorBarra}\"", gerador));
    }

    [Fact]
    public void Menu_tem_os_menus_comuns_em_volta_dos_do_modulo()
    {
        var titulos = new MainViewModel().Menus.Select(m => m.Titulo).ToList();
        Assert.Equal("_Cadastros", titulos[0]);
        Assert.Equal("Con_figurações", titulos[^2]);
        Assert.Equal("_Ajuda", titulos[^1]);
        Assert.Contains("_Lançamentos", titulos);
    }

    [Fact]
    public void Calendario_fica_em_Consultas_no_contabil_e_em_Obrigacoes_no_fiscal_e_na_folha()
    {
        var contabil = new MainViewModel();
        Assert.DoesNotContain(contabil.Menus, m => m.Titulo.Replace("_", "") == "Obrigações");
        var consultas = Assert.Single(contabil.Menus, m => m.Titulo.Replace("_", "") == "Consultas");
        Assert.Contains(Folhas(consultas.Filhos!), i => i.Chave == TelasComuns.Obrigacoes);
        MainViewModelBase[] outros = [new Vorticis.Fiscal.Desktop.MainViewModel(), new Vorticis.Folha.Desktop.MainViewModel()];
        foreach (var vm in outros)
        {
            Assert.DoesNotContain(vm.Menus, m => m.Titulo.Contains("genda"));
            var obrig = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Obrigações");
            Assert.Contains(Folhas(obrig.Filhos!), i => i.Chave == TelasComuns.Obrigacoes);
        }
        // o ERP (rotina da empresa, não do escritório contábil), o Condomínio e a Frota não têm calendário de obrigações
        foreach (var sem in new MainViewModelBase[] { new Vorticis.Erp.Desktop.MainViewModel(), new Vorticis.Frota.Desktop.MainViewModel() })
        {
            Assert.False(sem.Modulo.UsaCalendarioObrigacoes);
            Assert.DoesNotContain(Folhas(sem.Menus), i => i.Chave == TelasComuns.Obrigacoes);
            Assert.DoesNotContain(sem.Menus, m => m.Titulo.Replace("_", "") == "Obrigações");
        }
    }

    [Fact]
    public void Erp_separa_contas_a_pagar_e_a_receber_e_agrupa_os_relatorios_financeiros_sem_transporte()
    {
        var vm = new Vorticis.Erp.Desktop.MainViewModel();
        Assert.DoesNotContain(vm.Menus, m => m.Titulo.Replace("_", "") == "Transporte");
        Assert.All(Folhas(vm.Menus).Where(i => i.Chave is not null && i.Chave != TelasComuns.Sair), i => Assert.True(vm.ConheceTela(i.Chave!), i.Chave));
        var fin = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Financeiro");
        Assert.Equal(["Contas a pagar", "Contas a receber", "Conciliação bancária"], fin.Filhos!.Where(i => !i.EhSeparador).Select(i => i.Titulo));
        var rel = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Relatórios");
        var folhasRel = Folhas(rel.Filhos!).Select(i => i.Chave).ToList();
        Assert.Contains(Vorticis.Fiscal.Desktop.ViewModels.Telas.ResumoVendas, folhasRel);
        Assert.Contains(Vorticis.Fiscal.Desktop.ViewModels.Telas.ResumoCompras, folhasRel);
        Assert.Contains(Vorticis.Fiscal.Desktop.ViewModels.Telas.RelRecebimentos, folhasRel);
        var subFin = Assert.Single(rel.Filhos!, m => m.Titulo.Replace("_", "") == "Financeiro");
        Assert.Contains(subFin.Filhos!, i => i.Chave == Vorticis.Fiscal.Desktop.ViewModels.Telas.RelDuplicatasPagar);
        // cada tela de contas mostra um tipo só
        var pagar = new Vorticis.Fiscal.Desktop.ViewModels.ContasPagarViewModel(vm);
        Assert.False(pagar.EscolheTipo);
        Assert.Equal("Contas a pagar", pagar.TituloTela);
    }

    [Fact]
    public void Fiscal_agrupa_os_resumos_em_Relatorios()
    {
        var vm = new Vorticis.Fiscal.Desktop.MainViewModel();
        var rel = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Relatórios");
        var resumos = Assert.Single(rel.Filhos!, m => m.Titulo.Replace("_", "") == "Resumos");
        Assert.Equal(["Resumo de vendas", "Resumo de compras", "Resumo por CFOP"], resumos.Filhos!.Select(i => i.Titulo));
    }

    [Fact]
    public void Contabil_exporta_ECD_e_ECF_em_Integracoes_sem_menu_Auditoria_nem_eSocial()
    {
        var vm = new MainViewModel();
        Assert.DoesNotContain(vm.Menus, m => m.Titulo.Replace("_", "") == "Auditoria");
        var integ = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Integrações");
        var exportar = Assert.Single(integ.Filhos!, m => m.Titulo.Replace("_", "") == "Exportar");
        var folhas = Folhas(exportar.Filhos!).ToList();
        Assert.Contains(folhas, i => i.Chave == Telas.EcdAssistente);
        Assert.Contains(folhas, i => i.Chave == Telas.EcfAssistente);
        Assert.DoesNotContain(folhas, i => i.Titulo.Contains("eSocial"));
    }

    [Fact]
    public void Todos_os_modulos_tem_Parametros_no_menu_Cadastros()
    {
        MainViewModelBase[] modulos = [new MainViewModel(), new Vorticis.Fiscal.Desktop.MainViewModel(), new Vorticis.Folha.Desktop.MainViewModel(), new Vorticis.Erp.Desktop.MainViewModel(), new Vorticis.Frota.Desktop.MainViewModel(), new Vorticis.Logistica.Desktop.MainViewModel()];
        foreach (var vm in modulos)
        {
            var cadastros = vm.Menus[0];
            Assert.Contains(cadastros.Filhos!, i => i.Titulo == "Parâmetros" && i.Chave == TelasComuns.Parametros);
            Assert.True(vm.ConheceTela(TelasComuns.Parametros));
        }
    }

    [Fact]
    public void Menu_da_folha_tem_telas_registradas_e_so_o_que_falta_fica_em_breve()
    {
        var vm = new Vorticis.Folha.Desktop.MainViewModel();
        Assert.Equal("_Cadastros", vm.Menus[0].Titulo);
        Assert.Equal("_Ajuda", vm.Menus[^1].Titulo);
        var proprios = Folhas(vm.Menus.Skip(1).SkipLast(2)).Where(i => !i.EhSeparador).ToList();
        var habilitados = proprios.Where(i => i.Habilitado).ToList();
        Assert.All(habilitados, i => Assert.True(vm.ConheceTela(i.Chave!), i.Titulo));
        Assert.Contains(habilitados, i => i.Chave == "folha.esocial");
        Assert.Contains(habilitados, i => i.Chave == "folha.prolabore");
        // Funcionários é submenu de Cadastros; a folha não tem Clientes nem Fornecedores
        var cadastros = Folhas(vm.Menus.Take(1)).Where(i => !i.EhSeparador).ToList();
        Assert.Contains(cadastros, i => i.Chave == "folha.funcionarios");
        Assert.DoesNotContain(cadastros, i => i.Chave is TelasComuns.Clientes or TelasComuns.Fornecedores);
        Assert.True(vm.UsaPeriodo);
        Assert.Equal(["FGTS (GFD) (em breve)", "DCTFWeb e INSS (em breve)", "RAIS e DIRF (em breve)"], proprios.Where(i => !i.Habilitado).Select(i => i.Titulo));
        Assert.All(vm.Atalhos.Where(a => a.Chave is not null), a => Assert.True(vm.ConheceTela(a.Chave!), a.Chave));
        // Folha e Pró-labore são submenus de Cálculos
        var calculos = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Cálculos");
        Assert.Equal(["Folha", "Pró-labore"], calculos.Filhos!.Select(m => m.Titulo.Replace("_", "")));
        Assert.DoesNotContain(vm.Menus, m => m.Titulo.Replace("_", "") is "Folha" or "Pró-labore");
    }

    [Fact]
    public async Task Competencia_da_folha_e_escolhida_por_mes_e_ano_e_a_lista_filtra_as_fechadas()
    {
        var vm = new Vorticis.Folha.Desktop.MainViewModel();
        var folha = vm.FolhaMensal;
        folha.Competencia = new DateTime(2025, 7, 1);
        Assert.Equal(2025, folha.AnoCompetencia);
        Assert.Equal(12, Vorticis.Desktop.Comum.Views.SeletorCompetencia.Meses.Count);
        Assert.All(Vorticis.Desktop.Comum.Views.SeletorCompetencia.Anos, a => Assert.Equal(4, a.ToString().Length));   // ano sempre com 4 dígitos

        // sem banco nenhuma competência está fechada: a lista tem os meses recentes, e "Só fechadas" a esvazia
        await vm.RecarregarPeriodosAsync();
        Assert.Contains(vm.ListaPeriodos, p => p.Periodo == vm.PeriodoTrabalho);
        Assert.Equal(vm.PeriodoTrabalho, vm.PeriodoEscolhido!.Periodo);
        var outro = vm.ListaPeriodos.First(p => p.Periodo != vm.PeriodoTrabalho);
        vm.PeriodoEscolhido = outro;
        Assert.Equal(outro.Periodo, vm.PeriodoTrabalho);
        vm.SomenteFechados = true;
        await vm.RecarregarPeriodosAsync();
        Assert.Empty(vm.ListaPeriodos);
        Assert.Equal(outro.Periodo, vm.PeriodoTrabalho);         // o filtro não troca a competência em uso
        Assert.Equal("03/2026 (fechada)", new OpcaoPeriodo(new DateOnly(2026, 3, 1), true).Texto);
    }

    [Fact]
    public void Contabil_esconde_Gerenciais_sem_o_parametro_e_tem_Depreciacao_e_Bloqueio_de_lancamentos()
    {
        var vm = new MainViewModel();
        var rel = Assert.Single(vm.Menus, m => m.Titulo.Replace("_", "") == "Relatórios");
        var gerenciais = Assert.Single(rel.Filhos!, m => m.Titulo.Replace("_", "") == "Gerenciais");
        Assert.Equal(ModuloContabil.RecursoGerencial, gerenciais.Recurso);
        Assert.False(vm.RecursoAtivo(gerenciais.Recurso));                     // sem empresa (e com o parâmetro desligado) o menu fica escondido
        Assert.True(vm.RecursoAtivo(null));
        var deprec = Assert.Single(rel.Filhos!, m => m.Titulo.Replace("_", "") == "Depreciação");
        Assert.Equal([Telas.RelacaoBens, Telas.ValorDepreciado, Telas.ValorResidual, Telas.DepreciacaoPorGrupo], deprec.Filhos!.Select(i => i.Chave));
        Assert.Contains(vm.Menus[0].Filhos!, i => i.Chave == Telas.BloqueioLancamentos);
        // "Bloquear lançamentos até" saiu dos parâmetros; participantes ganhou grupo próprio e a contabilidade gerencial ficou no lugar
        var p = new ParametrosContabilViewModel(vm);
        Assert.DoesNotContain(p.Grupos.SelectMany(g => g.Campos), c => c.Chave == "dataBloqueio");
        Assert.Equal(["usaCentroCusto", "usaGerencial"], p.Grupos.Single(g => g.Titulo.StartsWith("Centros de custo")).Campos.Select(c => c.Chave));
        Assert.Equal(["usaParticipantes"], p.Grupos.Single(g => g.Titulo == "Participantes").Campos.Select(c => c.Chave));
        Assert.Equal(["dataBloqueio"], new BloqueioLancamentosViewModel(vm).Grupos.SelectMany(g => g.Campos).Select(c => c.Chave));
    }

    [Fact]
    public void Contas_bancarias_e_formas_de_pagamento_ficam_em_Cadastros()
    {
        static List<string?> Cadastros(MainViewModelBase vm) => Folhas(vm.Menus[0].Filhos!).Select(i => i.Chave).ToList();
        Assert.Contains(TelasComuns.ContasBancarias, Cadastros(new MainViewModel()));
        Assert.Contains(Folhas(new MainViewModel().Menus[0].Filhos!), i => i.Titulo == "Bancos" && i.Chave == TelasComuns.ContasBancarias);
        Assert.DoesNotContain(TelasComuns.FormasPagamento, Cadastros(new MainViewModel()));
        foreach (MainViewModelBase vm in new MainViewModelBase[] { new Vorticis.Fiscal.Desktop.MainViewModel(), new Vorticis.Erp.Desktop.MainViewModel() })
        {
            Assert.Contains(TelasComuns.ContasBancarias, Cadastros(vm));
            Assert.Contains(TelasComuns.FormasPagamento, Cadastros(vm));
        }
        // a tela de cadastro mostra a simulação dos prazos digitados
        var formas = new FormasPagamentoViewModel(new MainViewModel()) { Prazos = "30/60" };
        Assert.Contains("1/2 em", formas.Exemplo);
        Assert.Equal((2m, 30m, 30m), (formas.Parcelas!.Value, formas.PrimeiroVencimento!.Value, formas.Intervalo!.Value));   // prazos → campos
        formas.Parcelas = 4;
        Assert.Equal("30/60/90/120", formas.Prazos);                                                                        // campos → prazos
        formas.IncluirCommand.Execute(null);
        Assert.Equal("1", formas.Codigo);                                                                                   // código automático ao incluir
        formas.Prazos = "x";
        Assert.StartsWith("Prazos inválidos", formas.Exemplo);
        Assert.True(new Vorticis.Desktop.Comum.Views.TemplatesComuns().Match(formas));
    }

    [Fact]
    public async Task Nota_com_imobilizado_entrega_os_itens_para_a_tela_de_bens()
    {
        var vm = new Vorticis.Fiscal.Desktop.MainViewModel();
        Assert.True(vm.ConheceTela(Vorticis.Fiscal.Desktop.ViewModels.Telas.BensDaNota));
        var item = new Vorticis.Fiscal.Documentos.ItemFiscal(1, null, "Notebook", null, null, "1551", "UN", 1, 4500m, 4500m, 0, 0, 0, 0, 4500m,
            null, null, 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);
        var p = new Vorticis.Fiscal.Desktop.ViewModels.PendenciaImobilizado("123", "7", "Fornecedor", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), [item]);
        Vorticis.Fiscal.Desktop.ViewModels.PendenciaImobilizado.Entregar(vm, p);
        Assert.Same(p, Vorticis.Fiscal.Desktop.ViewModels.PendenciaImobilizado.Retirar(vm));
        Assert.Null(Vorticis.Fiscal.Desktop.ViewModels.PendenciaImobilizado.Retirar(vm));       // entregue uma vez só
        // a linha traz descrição e valor do item; o grupo traz vida útil e contas sugeridas
        var linha = new Vorticis.Fiscal.Desktop.ViewModels.BemDaNotaLinha(item, null);
        Assert.Equal(("Notebook", 4500m), (linha.Descricao, linha.Valor));
        linha.Grupo = new Vorticis.Application.Imobilizado.GrupoBem(1, "08", "Computadores", 20m, true, false, "1.2.3.01", "1.2.3.91", "4.1.9.01", null);
        Assert.Equal((60m, "1.2.3.01", "1.2.3.91", "4.1.9.01"), (linha.VidaUtil!.Value, linha.ContaBem, linha.ContaAcumulada, linha.ContaDespesa));
        await Task.CompletedTask;
    }
}
