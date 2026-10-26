using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Desktop.ViewModels;

namespace Vorticis.Fiscal.Desktop;

/// <summary>
/// Módulo Fiscal: digitação de notas de entrada e saída, produtos, importação de XML (NF-e, NFC-e, NFS-e nacional), consulta, contabilização automática, livros de entradas e saídas,
/// duplicatas (contas a pagar e a receber), apuração do Simples Nacional, Lucro Presumido e Lucro Real, PIS/COFINS, retenções e guias de recolhimento.
/// </summary>
public sealed class ModuloFiscal : IModuloDesktop
{
    public void PrepararServicos(Vorticis.Infra.VorticisApp app) => _ = app.Fiscal();

    public string Nome => "Vorticis Fiscal";
    public Vorticis.Application.Cadastros.ModulosEmpresa ModuloEmpresa => Vorticis.Application.Cadastros.ModulosEmpresa.Fiscal;
    public string NomeCurto => "Fiscal";
    public string Subtitulo => "Escrituração e obrigações fiscais";
    public string NomeExecutavel => "VorticisFiscal";
    public string CorDestaque => "#D97706";   // âmbar (ícone e selo do módulo)
    public string CorBarra => "#78350F";      // marrom-âmbar

    /// <summary>Recurso ligado com a contabilização em lote (padrão): mostra Documentos fiscais › Contabilizar notas fiscais.</summary>
    public const string RecursoContabilizacaoLote = "contabilizacao_lote";

    public async Task<IReadOnlySet<string>> RecursosAtivosAsync(Vorticis.Desktop.Comum.ViewModels.MainViewModelBase main)
    {
        if (main.App is not { } app || main.EmpresaId is not { } empresa || main.Sessao is not { } sessao) return new HashSet<string>();
        var online = await app.Fiscal().Contabilizacao.ContabilizacaoOnlineAsync(empresa, sessao.Id);
        return online is { Sucesso: true, Valor: true } ? new HashSet<string>() : new HashSet<string> { RecursoContabilizacaoLote };
    }

    public IReadOnlyList<DefinicaoGrafico> Graficos => GraficosFiscais.Todos;

    public IReadOnlyList<DefinicaoImportacao> Importacoes => ImportacoesFiscais.Todas;

    public IReadOnlyList<ItemMenu> ItensCadastros { get; } =
    [
        ItemMenu.Tela("Produtos", Telas.Produtos),
        ItemMenu.Tela("Naturezas de entrada", Telas.Naturezas),
        ItemMenu.Tela("Regras de contabilização", Telas.RegrasContabilizacao),
        ItemMenu.Tela("Formas de pagamento", Vorticis.Desktop.Comum.ViewModels.TelasComuns.FormasPagamento),
        ItemMenu.Tela("Contas bancárias", Vorticis.Desktop.Comum.ViewModels.TelasComuns.ContasBancarias),
    ];

    public IReadOnlyList<ItemMenu> Menus { get; } =
    [
        ItemMenu.Submenu("_Lançamentos",
            ItemMenu.Submenu("_Documentos fiscais",
                ItemMenu.Tela("Entrada", Telas.NotaEntrada),
                ItemMenu.Tela("Saída", Telas.NotaSaida)),
            ItemMenu.Tela("Duplicatas (contas a pagar e a receber)", Telas.Duplicatas),
            ItemMenu.Tela("Conciliação de duplicatas", Telas.ConciliacaoDuplicatas),
            ItemMenu.Separador,
            ItemMenu.Submenu("_SPED Fiscal",
                ItemMenu.Tela("Contas de energia, gás, água e comunicação", Telas.ContasConsumo),
                ItemMenu.Tela("Ajustes da apuração do ICMS", Telas.AjustesIcms),
                ItemMenu.Tela("Inventário de estoque (bloco H)", Telas.Inventario),
                ItemMenu.Separador,
                ItemMenu.Tela("Gerar EFD ICMS/IPI (SPED Fiscal)", Telas.EfdIcms))),
        ItemMenu.Submenu("_Documentos fiscais",
            ItemMenu.Tela("Importação de XML (NF-e, NFC-e, NFS-e)", Telas.ImportacaoXml),
            ItemMenu.Tela("Consulta de documentos", Telas.Documentos),
            ItemMenu.Tela("Contabilizar notas fiscais", Telas.ContabilizarNotas).SoCom(RecursoContabilizacaoLote),
            ItemMenu.Separador,
            ItemMenu.EmBreve("Conhecimentos de transporte (CT-e)")),
        ItemMenu.Submenu("_Apuração",
            ItemMenu.Tela("Simples Nacional (DAS)", Telas.Simples),
            ItemMenu.Tela("Lucro Presumido (IRPJ e CSLL)", Telas.Presumido),
            ItemMenu.Tela("Lucro Real (IRPJ, CSLL e LALUR)", Telas.Real),
            ItemMenu.Tela("PIS e COFINS", Telas.PisCofins),
            ItemMenu.EmBreve("ICMS e IPI"),
            ItemMenu.Separador,
            ItemMenu.Tela("Histórico de receita e folha", Telas.BaseMensal),
            ItemMenu.Tela("Contas dos tributos (provisões)", Telas.ContasTributos),
            ItemMenu.Separador,
            ItemMenu.Tela("Fechamento do período fiscal", Telas.FechamentoFiscal)),
        ItemMenu.Submenu("_Obrigações",
            ItemMenu.Tela("Retenções (IRRF, CSRF, INSS, ISS)", Telas.Retencoes),
            ItemMenu.Tela("Guias de recolhimento", Telas.Guias),
            ItemMenu.Separador,
            ItemMenu.Submenu("SPED",
                ItemMenu.Tela("EFD ICMS/IPI (SPED Fiscal)", Telas.EfdIcms),
                ItemMenu.Tela("EFD-Contribuições (PIS/COFINS)", Telas.EfdContribuicoes))),
        ItemMenu.Submenu("_Relatórios",
            ItemMenu.Submenu("_Livros",
                ItemMenu.Tela("Livro de Registro de Entradas", Telas.LivroEntradas),
                ItemMenu.Tela("Livro de Registro de Saídas", Telas.LivroSaidas),
                ItemMenu.Tela("Livro de Apuração do ICMS", Telas.LivroApuracaoIcms),
                ItemMenu.Tela("Livro de Apuração do IPI", Telas.LivroApuracaoIpi)),
            ItemMenu.Submenu("_Resumos",
                ItemMenu.Tela("Resumo de vendas", Telas.ResumoVendas),
                ItemMenu.Tela("Resumo de compras", Telas.ResumoCompras),
                ItemMenu.Tela("Resumo por CFOP", Telas.ResumoCfop)),
            ItemMenu.Separador,
            ItemMenu.Submenu("_Duplicatas",
                ItemMenu.Tela("Duplicatas a pagar", Telas.RelDuplicatasPagar),
                ItemMenu.Tela("Duplicatas a receber", Telas.RelDuplicatasReceber),
                ItemMenu.Tela("Posição por vencimento", Telas.RelPosicaoDuplicatas),
                ItemMenu.Separador,
                ItemMenu.Tela("Relatório de recebimentos", Telas.RelRecebimentos),
                ItemMenu.Tela("Relatório de pagamentos", Telas.RelPagamentos)),
            ItemMenu.Separador,
            ItemMenu.Tela("Produtos cadastrados", Telas.RelProdutos),
            ItemMenu.Tela("Itens das notas fiscais", Telas.RelItensNotas)),
    ];

    public IReadOnlyList<AtalhoTela> Atalhos { get; } =
    [
        new("Ctrl+I", Telas.ImportacaoXml, "Importação de XML"),
        new("Ctrl+D", Telas.Documentos, "Consulta de documentos"),
    ];

    public void RegistrarTelas(CatalogoTelas c)
    {
        c.Instancia("ImportacaoXml", m => new ImportacaoXmlViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.ImportacaoXml, "Documentos fiscais › Importação de XML", m => m.Pode(Permissao.FiscalDocumentosImportar));
        c.Instancia("Documentos", m => new DocumentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.Documentos, "Documentos fiscais › Consulta de documentos", m => m.Pode(Permissao.FiscalDocumentosConsultar));
        c.Instancia("RegrasContabilizacao", m => new RegrasContabilizacaoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.RegrasContabilizacao, "Cadastros › Regras de contabilização", m => m.Pode(Permissao.FiscalContabilizar), v => v.CarregarAsync());
        c.Instancia("ContabilizarNotas", m => new ContabilizarNotasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.ContabilizarNotas, "Documentos fiscais › Contabilizar notas fiscais", m => m.Pode(Permissao.FiscalContabilizar));

        c.Instancia("NotaEntrada", m => new NotaEntradaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.NotaEntrada, "Lançamentos › Documentos fiscais › Entrada", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("NotaSaida", m => new NotaSaidaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.NotaSaida, "Lançamentos › Documentos fiscais › Saída", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("BensDaNota", m => new BensDaNotaViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.BensDaNota, "Lançamentos › Bens do imobilizado da nota", m => m.Pode(Permissao.FiscalDocumentosImportar), v => v.CarregarAsync());
        c.Instancia("Duplicatas", m => new DuplicatasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.Duplicatas, "Lançamentos › Duplicatas", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("ConciliacaoDuplicatas", m => new ConciliacaoDuplicatasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.ConciliacaoDuplicatas, "Lançamentos › Conciliação de duplicatas", m => m.Pode(Permissao.FiscalDuplicatasBaixar));
        c.Instancia("Produtos", m => new ProdutosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.Produtos, "Cadastros › Produtos", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("Naturezas", m => new NaturezasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.Naturezas, "Cadastros › Naturezas de entrada", m => m.Pode(Permissao.FiscalDocumentosConsultar), v => v.CarregarAsync());
        c.Instancia("ParametrosModulo", m => new ParametrosFiscalViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Vorticis.Desktop.Comum.ViewModels.TelasComuns.Parametros, "Cadastros › Parâmetros", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());

        c.Instancia("Simples", m => new ApuracaoSimplesViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.Simples, "Apuração › Simples Nacional", m => m.Pode(Permissao.FiscalApurar));
        c.Instancia("Presumido", m => new ApuracaoPresumidoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.Presumido, "Apuração › Lucro Presumido", m => m.Pode(Permissao.FiscalApurar));
        c.Instancia("Real", m => new ApuracaoRealViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.Real, "Apuração › Lucro Real", m => m.Pode(Permissao.FiscalApurar));
        c.Instancia("PisCofins", m => new ApuracaoPisCofinsViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.PisCofins, "Apuração › PIS e COFINS", m => m.Pode(Permissao.FiscalApurar));
        c.Instancia("BaseMensal", m => new BaseMensalViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.BaseMensal, "Apuração › Histórico de receita e folha", m => m.Pode(Permissao.FiscalApurar) || m.Pode(Permissao.FiscalRetencoesManter), v => v.CarregarAsync());
        c.Instancia("AjustesIcms", m => new AjustesIcmsViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.AjustesIcms, "Lançamentos › SPED Fiscal › Ajustes da apuração do ICMS", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
        c.Instancia("ContasConsumo", m => new ContasConsumoViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.ContasConsumo, "Lançamentos › SPED Fiscal › Contas de consumo", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
        c.Instancia("Inventario", m => new InventarioViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.Inventario, "Lançamentos › SPED Fiscal › Inventário de estoque", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
        c.Instancia("FechamentoFiscal", m => new FechamentoFiscalViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.FechamentoFiscal, "Apuração › Fechamento do período fiscal", m => m.TemAcessoNaEmpresa, v => v.CarregarAsync());
        c.Instancia("ContasTributos", m => new ContasTributosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.ContasTributos, "Apuração › Contas dos tributos", m => m.Pode(Permissao.FiscalApurar), v => v.CarregarAsync());

        c.Instancia("Retencoes", m => new RetencoesViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.Retencoes, "Obrigações › Retenções", m => m.Pode(Permissao.FiscalRelatorios) || m.Pode(Permissao.FiscalRetencoesManter), v => v.ConsultarAsync());
        c.Instancia("Guias", m => new GuiasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.Guias, "Obrigações › Guias de recolhimento", m => m.Pode(Permissao.FiscalRelatorios) || m.Pode(Permissao.FiscalRetencoesManter), v => v.ConsultarAsync());

        c.Instancia("EfdIcms", m => new EfdIcmsAssistenteViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(), aoLimpar: v => v.Limpar(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.EfdIcms, "Obrigações › SPED › EFD ICMS/IPI", m => m.Pode(Permissao.FiscalSpedGerar), v => v.CarregarAsync());
        c.Instancia("EfdContribuicoes", m => new EfdContribuicoesAssistenteViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes(), aoLimpar: v => v.Limpar(),
                    aoTrocarEmpresa: (v, ativa) => ativa ? v.CarregarAsync() : Task.CompletedTask)
            .Tela(Telas.EfdContribuicoes, "Obrigações › SPED › EFD-Contribuições", m => m.Pode(Permissao.FiscalSpedGerar), v => v.CarregarAsync());

        c.Instancia("LivroEntradas", m => new LivroEntradasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.LivroEntradas, "Relatórios › Livros › Livro de Registro de Entradas", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("LivroApuracaoIcms", m => new LivroApuracaoIcmsViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.LivroApuracaoIcms, "Relatórios › Livros › Livro de Apuração do ICMS", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("LivroApuracaoIpi", m => new LivroApuracaoIpiViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.LivroApuracaoIpi, "Relatórios › Livros › Livro de Apuração do IPI", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("LivroSaidas", m => new LivroSaidasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.LivroSaidas, "Relatórios › Livros › Livro de Registro de Saídas", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelProdutos", m => new RelatorioProdutosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelProdutos, "Relatórios › Produtos cadastrados", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelItensNotas", m => new RelatorioItensNotasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelItensNotas, "Relatórios › Itens das notas fiscais", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("ResumoCfop", m => new ResumoCfopViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.ResumoCfop, "Relatórios › Resumos › Resumo por CFOP", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("ResumoVendas", m => new ResumoVendasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.ResumoVendas, "Relatórios › Resumos › Resumo de vendas", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("ResumoCompras", m => new ResumoComprasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.ResumoCompras, "Relatórios › Resumos › Resumo de compras", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelRecebimentos", m => new RelRecebimentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelRecebimentos, "Relatórios › Duplicatas › Relatório de recebimentos", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelPagamentos", m => new RelPagamentosViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelPagamentos, "Relatórios › Duplicatas › Relatório de pagamentos", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelDuplicatasPagar", m => new RelDuplicatasPagarViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelDuplicatasPagar, "Relatórios › Duplicatas › Duplicatas a pagar", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelDuplicatasReceber", m => new RelDuplicatasReceberViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelDuplicatasReceber, "Relatórios › Duplicatas › Duplicatas a receber", m => m.Pode(Permissao.FiscalRelatorios));
        c.Instancia("RelPosicaoDuplicatas", m => new RelPosicaoDuplicatasViewModel(m), atualizarPermissoes: v => v.AtualizarPermissoes())
            .Tela(Telas.RelPosicaoDuplicatas, "Relatórios › Duplicatas › Posição por vencimento", m => m.Pode(Permissao.FiscalRelatorios));
    }
}
