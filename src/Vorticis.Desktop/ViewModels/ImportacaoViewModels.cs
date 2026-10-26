using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Importacao;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Desktop.ViewModels;

/// <summary>Linha da pré-visualização: um lançamento lido e a sua situação.</summary>
public sealed record LancamentoPreviaLinha(ItemImportacao Item)
{
    public int Linha => Item.Lancamento.Linha;
    public string Data => Item.Lancamento.Data.ToString("dd/MM/yyyy");
    public string Partidas => string.Join("  ·  ", Item.Lancamento.Partidas.Select(p => $"{p.DC} {p.CodigoConta}"));
    public string Valor => Numeros.Moeda(Item.Lancamento.Total);
    public string Historico => Item.Lancamento.Partidas.FirstOrDefault()?.Historico ?? "";
    public string Situacao => Item.JaImportado ? "Já importado" : Item.Erros.Count == 0 ? "OK" : string.Join(" ", Item.Erros);
    public bool TemErro => !Item.JaImportado && Item.Erros.Count > 0;
}

/// <summary>Base das abas de importação de lançamentos: pré-visualização validada e importação (tudo ou nada).</summary>
public abstract partial class ImportacaoLancamentosBase(IContexto contexto) : ModuloViewModel(contexto)
{
    private IReadOnlyList<LancamentoLido> _lidos = [];

    public ObservableCollection<LancamentoPreviaLinha> Previa { get; } = [];
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ImportarCommand))] private bool _podeImportar;

    protected abstract OrigemLancamento Origem { get; }

    /// <summary>Lê a fonte e devolve os lançamentos, ou null (com os erros já em <see cref="ModuloViewModel.Erros"/>).</summary>
    protected abstract IReadOnlyList<LancamentoLido>? Ler();

    protected void LimparPrevia()
    {
        _lidos = [];
        Previa.Clear();
        Resumo = "";
        PodeImportar = false;
    }

    [RelayCommand]
    private Task PreVisualizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        LimparPrevia();
        if (Ler() is not { } lidos) return;
        var r = await app.Importacao.PrepararAsync(empresa, lidos, Origem, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _lidos = lidos;
        foreach (var i in r.Valor) Previa.Add(new LancamentoPreviaLinha(i));
        var validos = r.Valor.Count(i => i.Valido);
        var comErro = r.Valor.Count(i => i.Erros.Count > 0 && !i.JaImportado);
        var jaImportados = r.Valor.Count(i => i.JaImportado);
        Resumo = $"{r.Valor.Count} lançamento(s) lido(s): {validos} pronto(s) para importar, total {Numeros.Moeda(r.Valor.Where(i => i.Valido).Sum(i => i.Lancamento.Total))}" +
                 (comErro > 0 ? $" · {comErro} com erro (corrija e pré-visualize de novo)" : "") +
                 (jaImportados > 0 ? $" · {jaImportados} já importado(s), serão ignorados" : "");
        PodeImportar = validos > 0 && comErro == 0;
    }, Permissao.LancamentoIncluir);

    [RelayCommand(CanExecute = nameof(PodeImportar))]
    private Task ImportarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var validos = Previa.Count(p => p.Item.Valido);
        if (!await Contexto.ConfirmarAsync("Importar lançamentos",
                $"Serão gravados {validos} lançamento(s), numa única operação (se um falhar, nenhum é gravado).", "Importar", "Cancelar")) return;
        var r = await app.Importacao.ImportarAsync(empresa, _lidos, Origem, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        LimparPrevia();
        Mensagem = $"{r.Valor.Count} lançamento(s) importado(s): nº {r.Valor.Min(l => l.Numero)} a {r.Valor.Max(l => l.Numero)}.";
    }, Permissao.LancamentoIncluir);

    protected TabelaImportada? LerPlanilha(string arquivo)
    {
        if (!File.Exists(arquivo)) { Erros.Add("Selecione o arquivo."); return null; }
        var r = LeitorPlanilha.Ler(arquivo);
        if (r.Sucesso) return r.Valor;
        foreach (var e in r.Erros) Erros.Add(e);
        return null;
    }

    protected IReadOnlyList<LancamentoLido>? Resultado(LeituraLancamentos leitura)
    {
        foreach (var e in leitura.Erros) Erros.Add(e);
        if (leitura.Erros.Count > 0) return null;
        if (leitura.Lancamentos.Count == 0) { Erros.Add("Nenhum lançamento encontrado no arquivo."); return null; }
        return leitura.Lancamentos;
    }
}

/// <summary>Aba Planilhas: qualquer planilha, com layouts criados pelo usuário (colunas → campos do lançamento).</summary>
/// <summary>Uma linha da planilha carregada, como está no arquivo (para o usuário ver antes de definir as colunas).</summary>
public sealed record LinhaPlanilha(int Numero, string[] Celulas);

public partial class ImportacaoPlanilhaViewModel(IContexto contexto) : ImportacaoLancamentosBase(contexto)
{
    /// <summary>Quantas linhas da planilha aparecem na visualização.</summary>
    public const int LinhasVisualizadas = 200;

    public const string Nenhuma = "(nenhuma)";
    private TabelaImportada? _tabela;
    private bool _carregandoLayout;

    protected override OrigemLancamento Origem => OrigemLancamento.Planilha;

    public ObservableCollection<LayoutSalvo> Layouts { get; } = [];
    public ObservableCollection<string> OpcoesColunas { get; } = [Nenhuma];
    [ObservableProperty] private LayoutSalvo? _layoutSelecionado;
    [ObservableProperty] private string _arquivo = "";

    [ObservableProperty] private string _nomeLayout = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PorPartida))] private bool _porLancamento = true;
    public bool PorPartida
    {
        get => !PorLancamento;
        set => PorLancamento = !value;
    }
    [ObservableProperty] private string _colunaData = Nenhuma;
    [ObservableProperty] private string _colunaValor = Nenhuma;
    [ObservableProperty] private string _colunaHistorico = Nenhuma;
    [ObservableProperty] private string _colunaDocumento = Nenhuma;
    [ObservableProperty] private string _colunaCentroCusto = Nenhuma;
    [ObservableProperty] private string _colunaContaDebito = Nenhuma;
    [ObservableProperty] private string _colunaContaCredito = Nenhuma;
    [ObservableProperty] private string _colunaAgrupador = Nenhuma;
    [ObservableProperty] private string _colunaConta = Nenhuma;
    [ObservableProperty] private string _colunaDC = Nenhuma;
    [ObservableProperty] private string _colunaValorDebito = Nenhuma;
    [ObservableProperty] private string _colunaValorCredito = Nenhuma;
    [ObservableProperty] private string _contaDebitoPadrao = "";
    [ObservableProperty] private string _contaCreditoPadrao = "";
    [ObservableProperty] private string _historicoPadrao = "";
    [ObservableProperty] private string _mascaraContas = "";
    [ObservableProperty] private string _amostra = "";

    // Planilha carregada (visualização crua, com o papel de cada coluna no layout)
    public ObservableCollection<LinhaPlanilha> LinhasPlanilha { get; } = [];
    [ObservableProperty] private IReadOnlyList<string> _colunasPlanilha = [];
    public bool TemPlanilha => ColunasPlanilha.Count > 0;

    /// <summary>Papel da coluna no layout atual (ex.: "Data", "Conta débito"), para o cabeçalho da visualização.</summary>
    public string PapelDaColuna(string coluna)
    {
        var papeis = new (string Coluna, string Papel)[]
        {
            (ColunaData, "Data"), (ColunaValor, "Valor"), (ColunaHistorico, "Histórico"), (ColunaDocumento, "Documento"),
            (ColunaCentroCusto, "C. custo"), (ColunaAgrupador, "Agrupador"),
        };
        var porModelo = PorLancamento
            ? new[] { (ColunaContaDebito, "Conta débito"), (ColunaContaCredito, "Conta crédito") }
            : new[] { (ColunaConta, "Conta"), (ColunaDC, "D/C"), (ColunaValorDebito, "Valor débito"), (ColunaValorCredito, "Valor crédito") };
        return string.Join(", ", papeis.Concat(porModelo).Where(p => p.Item1 == coluna).Select(p => p.Item2));
    }

    /// <summary>Lê (de novo) o arquivo informado e mostra a planilha para definir as colunas.</summary>
    [RelayCommand]
    private void CarregarPlanilha()
    {
        Erros.Clear();
        if (string.IsNullOrWhiteSpace(Arquivo) || !File.Exists(Arquivo)) { Erros.Add("Informe o arquivo da planilha (.xlsx ou .csv)."); return; }
        OnArquivoChanged(Arquivo);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app) return;
        var g = NovaGeracao();
        var lista = await app.Importacao.ListarLayoutsAsync();
        if (!EhAtual(g)) return;
        var id = LayoutSelecionado?.Id;
        Layouts.Clear();
        foreach (var l in lista) Layouts.Add(l);
        LayoutSelecionado = Layouts.FirstOrDefault(l => l.Id == id);
    }

    partial void OnArquivoChanged(string value)
    {
        _tabela = null;
        if (!File.Exists(value)) return;
        Erros.Clear();
        Mensagem = null;
        LimparPrevia();
        _tabela = LerPlanilha(value);
        LinhasPlanilha.Clear();
        ColunasPlanilha = _tabela?.Colunas ?? [];
        OnPropertyChanged(nameof(TemPlanilha));
        if (_tabela is null) return;
        for (var i = 0; i < Math.Min(_tabela.Linhas.Count, LinhasVisualizadas); i++)
            LinhasPlanilha.Add(new LinhaPlanilha(_tabela.NumerosLinha[i],
                Enumerable.Range(0, _tabela.Colunas.Count).Select(c => _tabela.Valor(i, c)).ToArray()));
        foreach (var c in _tabela.Colunas.Where(c => !OpcoesColunas.Contains(c))) OpcoesColunas.Add(c);
        Amostra = $"{_tabela.Linhas.Count} linha(s). Colunas: {string.Join(" | ", _tabela.Colunas)}";
        if (LayoutSelecionado is null && NomeLayout.Length == 0) SugerirColunas();
    }

    /// <summary>Novo layout: tenta reconhecer as colunas pelos nomes mais comuns.</summary>
    private void SugerirColunas()
    {
        if (_tabela is null) return;
        string Achar(params string[] nomes) => _tabela.Coluna(nomes) is var i and >= 0 ? _tabela.Colunas[i] : Nenhuma;
        ColunaData = Achar("DATA", "DT", "DATA_LANCAMENTO", "DT_LCTO", "VENCIMENTO", "DATA_MOVIMENTO");
        ColunaValor = Achar("VALOR", "VLR", "VALOR_R", "VL_LCTO", "TOTAL");
        ColunaHistorico = Achar("HISTORICO", "DESCRICAO", "HIST", "COMPLEMENTO");
        ColunaDocumento = Achar("DOCUMENTO", "DOC", "NF", "NOTA", "NUMERO_DOCUMENTO");
        ColunaContaDebito = Achar("DEBITO", "CONTA_DEBITO", "CONTA_DEBITADA", "CTA_DEBITO", "D");
        ColunaContaCredito = Achar("CREDITO", "CONTA_CREDITO", "CONTA_CREDITADA", "CTA_CREDITO", "C");
        ColunaConta = Achar("CONTA", "COD_CONTA", "CODIGO_CONTA");
        ColunaDC = Achar("DC", "D_C", "IND_DC", "NATUREZA");
        ColunaAgrupador = Achar("LANCAMENTO", "LOTE", "NUMERO", "NUM_LCTO");
        PorLancamento = ColunaConta == Nenhuma || ColunaContaDebito != Nenhuma;
    }

    partial void OnLayoutSelecionadoChanged(LayoutSalvo? value)
    {
        if (value is null) return;
        var l = value.Layout;
        _carregandoLayout = true;
        try
        {
            string C(string? nome)
            {
                if (string.IsNullOrWhiteSpace(nome)) return Nenhuma;
                if (!OpcoesColunas.Contains(nome)) OpcoesColunas.Add(nome);
                return nome;
            }
            NomeLayout = value.Nome;
            PorLancamento = l.Modelo == ModeloLayout.UmaLinhaPorLancamento;
            (ColunaData, ColunaValor, ColunaHistorico, ColunaDocumento, ColunaCentroCusto) =
                (C(l.ColunaData), C(l.ColunaValor), C(l.ColunaHistorico), C(l.ColunaDocumento), C(l.ColunaCentroCusto));
            (ColunaContaDebito, ColunaContaCredito, ColunaAgrupador, ColunaConta, ColunaDC, ColunaValorDebito, ColunaValorCredito) =
                (C(l.ColunaContaDebito), C(l.ColunaContaCredito), C(l.ColunaAgrupador), C(l.ColunaConta), C(l.ColunaDC), C(l.ColunaValorDebito), C(l.ColunaValorCredito));
            (ContaDebitoPadrao, ContaCreditoPadrao, HistoricoPadrao, MascaraContas) =
                (l.ContaDebitoPadrao ?? "", l.ContaCreditoPadrao ?? "", l.HistoricoPadrao ?? "", l.MascaraContas ?? "");
        }
        finally { _carregandoLayout = false; }
        LimparPrevia();
    }

    public LayoutImportacao LayoutAtual()
    {
        static string? N(string s) => s == Nenhuma || string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var porLancamento = PorLancamento;
        return new LayoutImportacao
        {
            Nome = NomeLayout.Trim(),
            Modelo = porLancamento ? ModeloLayout.UmaLinhaPorLancamento : ModeloLayout.UmaLinhaPorPartida,
            ColunaData = N(ColunaData), ColunaValor = N(ColunaValor), ColunaHistorico = N(ColunaHistorico),
            ColunaDocumento = N(ColunaDocumento), ColunaCentroCusto = N(ColunaCentroCusto),
            ColunaContaDebito = porLancamento ? N(ColunaContaDebito) : null, ColunaContaCredito = porLancamento ? N(ColunaContaCredito) : null,
            ContaDebitoPadrao = porLancamento ? N(ContaDebitoPadrao) : null, ContaCreditoPadrao = porLancamento ? N(ContaCreditoPadrao) : null,
            ColunaAgrupador = porLancamento ? null : N(ColunaAgrupador), ColunaConta = porLancamento ? null : N(ColunaConta),
            ColunaDC = porLancamento ? null : N(ColunaDC), ColunaValorDebito = porLancamento ? null : N(ColunaValorDebito),
            ColunaValorCredito = porLancamento ? null : N(ColunaValorCredito),
            HistoricoPadrao = N(HistoricoPadrao), MascaraContas = N(MascaraContas),
        };
    }

    [RelayCommand]
    private void NovoLayout()
    {
        LayoutSelecionado = null;
        NomeLayout = "";
        foreach (var p in new Action<string>[] { v => ColunaData = v, v => ColunaValor = v, v => ColunaHistorico = v, v => ColunaDocumento = v,
                     v => ColunaCentroCusto = v, v => ColunaContaDebito = v, v => ColunaContaCredito = v, v => ColunaAgrupador = v,
                     v => ColunaConta = v, v => ColunaDC = v, v => ColunaValorDebito = v, v => ColunaValorCredito = v })
            p(Nenhuma);
        (ContaDebitoPadrao, ContaCreditoPadrao, HistoricoPadrao, MascaraContas) = ("", "", "", "");
        SugerirColunas();
        LimparPrevia();
    }

    [RelayCommand]
    private Task SalvarLayoutAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Importacao.SalvarLayoutAsync(empresa, LayoutSelecionado?.Id, LayoutAtual(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var nome = NomeLayout.Trim();
        await CarregarAsync();
        LayoutSelecionado = Layouts.FirstOrDefault(l => l.Id == r.Valor);
        Mensagem = $"Layout \"{nome}\" salvo. Ele fica disponível para todas as empresas.";
    }, Permissao.LancamentoIncluir);

    [RelayCommand]
    private Task ExcluirLayoutAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (LayoutSelecionado is not { } l) { Erros.Add("Escolha o layout a excluir."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir layout", $"Excluir o layout \"{l.Nome}\"?", "Excluir", "Cancelar")) return;
        var r = await app.Importacao.ExcluirLayoutAsync(empresa, l.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        NovoLayout();
        await CarregarAsync();
        Mensagem = $"Layout \"{l.Nome}\" excluído.";
    }, Permissao.LancamentoIncluir);

    protected override IReadOnlyList<LancamentoLido>? Ler()
    {
        _tabela ??= LerPlanilha(Arquivo);
        return _tabela is null ? null : Resultado(ImportadorLancamentos.Ler(_tabela, LayoutAtual()));
    }

    partial void OnPorLancamentoChanged(bool value) { if (!_carregandoLayout) LimparPrevia(); }
}

public partial class TransacaoExtratoLinha(TransacaoExtrato transacao, string contrapartida) : ObservableObject
{
    public TransacaoExtrato Transacao { get; } = transacao;
    public string Data => Transacao.Data.ToString("dd/MM/yyyy");
    public string Descricao => Transacao.Descricao;
    public string Documento => Transacao.Documento ?? "";
    public string Entrada => Transacao.Valor > 0 ? Numeros.Moeda(Transacao.Valor) : "";
    public string Saida => Transacao.Valor < 0 ? Numeros.Moeda(-Transacao.Valor) : "";
    [ObservableProperty] private bool _importar = true;
    [ObservableProperty] private string _contrapartida = contrapartida;
    [ObservableProperty] private string _situacao = "";
}

/// <summary>Linha do documento carregado (PDF/TXT/CSV) como o layout separa: as colunas numeradas e se virou movimento.</summary>
public sealed record LinhaDocumentoLinha(LinhaDocumentoExtrato L)
{
    public string Numero => L.Numero.ToString();
    public string Movimento => L.Transacao is { } t ? $"{t.Data:dd/MM/yyyy} {Numeros.Moeda(t.Valor)}" : "";
    public string C1 => Col(1);
    public string C2 => Col(2);
    public string C3 => Col(3);
    public string C4 => Col(4);
    public string C5 => Col(5);
    public string C6 => Col(6);
    public string Demais => L.Colunas.Count > 6 ? string.Join("  |  ", L.Colunas.Skip(6)) : "";
    private string Col(int n) => n <= L.Colunas.Count ? L.Colunas[n - 1] : "";
}

/// <summary>
/// Integrações › Importar › Extratos bancários: primeiro o usuário carrega o arquivo (OFX, PDF, TXT ou CSV). O OFX é lido direto;
/// PDF, TXT e CSV mostram o documento carregado e o layout (sugerido pelo próprio documento ou um layout salvo), que o usuário ajusta
/// vendo o resultado e pode salvar com um nome. Cada movimento vira um lançamento contra a conta contábil do banco.
/// </summary>
public partial class ImportacaoExtratoViewModel(IContexto contexto) : ImportacaoLancamentosBase(contexto)
{
    private ExtratoBancario? _extrato;
    private IReadOnlyList<string>? _linhasDocumento;
    private bool _aplicandoLayout;

    protected override OrigemLancamento Origem => OrigemLancamento.Extrato;

    public ObservableCollection<TransacaoExtratoLinha> Transacoes { get; } = [];
    public ObservableCollection<LinhaDocumentoLinha> LinhasDocumento { get; } = [];
    public ObservableCollection<LayoutExtratoSalvo> Layouts { get; } = [];
    public static IReadOnlyList<string> Separadores { get; } = ["Espaços (PDF ou TXT)", "Ponto e vírgula (CSV)", "Tabulação"];
    public static IReadOnlyList<string> FormatosData => LeitorExtratoTexto.FormatosData;

    [ObservableProperty] private string _arquivo = "";
    [ObservableProperty] private string _contaBanco = "";
    [ObservableProperty] private string _contrapartidaEntradas = "";
    [ObservableProperty] private string _contrapartidaSaidas = "";
    [ObservableProperty] private string _prefixoHistorico = "";
    [ObservableProperty] private string _dadosExtrato = "";
    /// <summary>O arquivo carregado é PDF, TXT ou CSV (precisa de layout); no OFX o painel de layout fica escondido.</summary>
    [ObservableProperty] private bool _usaLayout;
    [ObservableProperty] private LayoutExtratoSalvo? _layoutSelecionado;
    [ObservableProperty] private string _nomeLayout = "";
    [ObservableProperty] private int _separador;
    [ObservableProperty] private decimal? _colunaData = 1;
    [ObservableProperty] private decimal? _colunaDescricao = 0;
    [ObservableProperty] private decimal? _colunaValor = 0;
    [ObservableProperty] private decimal? _colunaDocumento = 0;
    [ObservableProperty] private string _formatoData = "dd/MM/yyyy";
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private string _ignorar = "SALDO";
    [ObservableProperty] private bool _inverterSinal;

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app) return;
        var g = NovaGeracao();
        var lista = await app.Importacao.ListarLayoutsExtratoAsync();
        if (!EhAtual(g)) return;
        var id = LayoutSelecionado?.Id;
        Layouts.Clear();
        foreach (var l in lista) Layouts.Add(l);
        _aplicandoLayout = true;
        LayoutSelecionado = Layouts.FirstOrDefault(l => l.Id == id);
        _aplicandoLayout = false;
    }

    partial void OnArquivoChanged(string value)
    {
        if (File.Exists(value)) LerExtrato();
    }

    /// <summary>Carrega o arquivo escolhido: OFX vira movimentos na hora; PDF, TXT e CSV mostram o documento e aplicam o layout.</summary>
    [RelayCommand]
    private void LerExtrato()
    {
        Erros.Clear();
        Mensagem = null;
        LimparPrevia();
        Transacoes.Clear();
        LinhasDocumento.Clear();
        _extrato = null;
        _linhasDocumento = null;
        DadosExtrato = "";
        if (!File.Exists(Arquivo)) { Erros.Add("Selecione o arquivo do extrato (OFX, PDF, TXT ou CSV)."); return; }
        var bytes = File.ReadAllBytes(Arquivo);
        var texto = LeitorDocumentoExtrato.EhPdf(Arquivo) ? "" : LeitorPlanoReferencial.Decodificar(bytes);
        UsaLayout = !texto.Contains("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (!UsaLayout)
        {
            var r = LeitorOfx.Ler(texto);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            MostrarExtrato(r.Valor);
            return;
        }
        var linhas = LeitorDocumentoExtrato.Ler(Arquivo);
        if (!linhas.Sucesso) { MostrarResultado(linhas, ""); return; }
        _linhasDocumento = linhas.Valor;
        if (LayoutSelecionado is null && NomeLayout.Length == 0) SugerirLayout();
        else AplicarLayout();
    }

    /// <summary>Monta o layout olhando o documento carregado (separador, coluna e formato da data; valor = último da linha).</summary>
    [RelayCommand]
    private void SugerirLayout()
    {
        if (_linhasDocumento is null) { Erros.Add("Carregue primeiro o arquivo do extrato."); return; }
        PreencherLayout(LeitorExtratoTexto.Sugerir(_linhasDocumento, (int)(Ano ?? DateTime.Today.Year)) with { Ignorar = Ignorar, InverterSinal = InverterSinal });
        AplicarLayout();
    }

    private void PreencherLayout(LayoutExtrato l)
    {
        _aplicandoLayout = true;
        (Separador, ColunaData, ColunaDescricao, ColunaValor, ColunaDocumento) = ((int)l.Separador, l.ColunaData, l.ColunaDescricao, l.ColunaValor, l.ColunaDocumento);
        (FormatoData, Ignorar, InverterSinal) = (l.FormatoData, l.Ignorar, l.InverterSinal);
        _aplicandoLayout = false;
    }

    public LayoutExtrato LayoutAtual() => new(NomeLayout.Trim(), (SeparadorExtrato)Math.Clamp(Separador, 0, 2), (int)(ColunaData ?? 1), (int)(ColunaDescricao ?? 0),
                                              (int)(ColunaValor ?? 0), (int)(ColunaDocumento ?? 0), FormatoData, Ignorar ?? "", InverterSinal);

    partial void OnLayoutSelecionadoChanged(LayoutExtratoSalvo? value)
    {
        if (value is null || _aplicandoLayout) return;
        NomeLayout = value.Nome;
        PreencherLayout(value.Layout);
        AplicarLayout();
    }

    partial void OnSeparadorChanged(int value) => AoMudarLayout();
    partial void OnColunaDataChanged(decimal? value) => AoMudarLayout();
    partial void OnColunaDescricaoChanged(decimal? value) => AoMudarLayout();
    partial void OnColunaValorChanged(decimal? value) => AoMudarLayout();
    partial void OnColunaDocumentoChanged(decimal? value) => AoMudarLayout();
    partial void OnFormatoDataChanged(string value) => AoMudarLayout();
    partial void OnAnoChanged(decimal? value) => AoMudarLayout();
    partial void OnIgnorarChanged(string value) => AoMudarLayout();
    partial void OnInverterSinalChanged(bool value) => AoMudarLayout();
    private void AoMudarLayout() { if (!_aplicandoLayout) AplicarLayout(); }

    /// <summary>Aplica o layout ao documento carregado: mostra as linhas separadas em colunas e os movimentos reconhecidos.</summary>
    [RelayCommand]
    private void AplicarLayout()
    {
        if (_linhasDocumento is null) return;
        LimparPrevia();
        var linhas = LeitorExtratoTexto.Aplicar(_linhasDocumento, LayoutAtual(), (int)(Ano ?? DateTime.Today.Year));
        LinhasDocumento.Clear();
        foreach (var l in linhas) LinhasDocumento.Add(new LinhaDocumentoLinha(l));
        MostrarExtrato(LeitorExtratoTexto.Extrato(linhas, "EXTRATO"), $"{linhas.Count} linha(s) no documento · ");
    }

    private void MostrarExtrato(ExtratoBancario e, string prefixo = "")
    {
        _extrato = e;
        Transacoes.Clear();
        foreach (var t in e.Transacoes) Transacoes.Add(new TransacaoExtratoLinha(t, Padrao(t)));
        DadosExtrato = prefixo + (UsaLayout ? "" : $"Banco {e.Banco} · agência {e.Agencia} · conta {e.Conta}") +
                       (e.Inicio is { } i && e.Fim is { } f ? $"{(UsaLayout ? "" : " · ")}{i:dd/MM/yyyy} a {f:dd/MM/yyyy} · " : UsaLayout ? "" : " · ") +
                       $"{e.Transacoes.Count} movimento(s): entradas {Numeros.Moeda(e.Transacoes.Where(t => t.Valor > 0).Sum(t => t.Valor))}," +
                       $" saídas {Numeros.Moeda(-e.Transacoes.Where(t => t.Valor < 0).Sum(t => t.Valor))}" +
                       (e.SaldoFinal is { } s ? $" · saldo final no banco {Numeros.Moeda(s)}" : "");
    }

    [RelayCommand]
    private void NovoLayout()
    {
        _aplicandoLayout = true;
        LayoutSelecionado = null;
        _aplicandoLayout = false;
        NomeLayout = "";
        if (_linhasDocumento is not null) SugerirLayout();
    }

    [RelayCommand]
    private Task SalvarLayoutAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Importacao.SalvarLayoutExtratoAsync(empresa, LayoutSelecionado?.Id, LayoutAtual(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var nome = NomeLayout.Trim();
        await CarregarAsync();
        _aplicandoLayout = true;
        LayoutSelecionado = Layouts.FirstOrDefault(l => l.Id == r.Valor);
        _aplicandoLayout = false;
        Mensagem = $"Layout \"{nome}\" salvo. Da próxima vez, escolha-o em Layout salvo depois de carregar o extrato desse banco.";
    }, Permissao.LancamentoIncluir);

    [RelayCommand]
    private Task ExcluirLayoutAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (LayoutSelecionado is not { } l) { Erros.Add("Escolha o layout a excluir."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir layout", $"Excluir o layout de extrato \"{l.Nome}\"?", "Excluir", "Cancelar")) return;
        var r = await app.Importacao.ExcluirLayoutAsync(empresa, l.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        NovoLayout();
        await CarregarAsync();
        Mensagem = $"Layout \"{l.Nome}\" excluído.";
    }, Permissao.LancamentoIncluir);

    private string Padrao(TransacaoExtrato t) => (t.Valor > 0 ? ContrapartidaEntradas : ContrapartidaSaidas).Trim();

    /// <summary>Preenche a contrapartida padrão nas linhas que estão sem contrapartida.</summary>
    [RelayCommand]
    private void AplicarContrapartidas()
    {
        foreach (var l in Transacoes.Where(l => string.IsNullOrWhiteSpace(l.Contrapartida))) l.Contrapartida = Padrao(l.Transacao);
        LimparPrevia();
    }

    protected override IReadOnlyList<LancamentoLido>? Ler()
    {
        if (_extrato is null) { Erros.Add("Carregue o extrato primeiro."); return null; }
        if (string.IsNullOrWhiteSpace(ContaBanco)) { Erros.Add("Informe a conta contábil do banco (ex.: 1.1.01.002)."); return null; }
        var linhas = Transacoes.Where(l => l.Importar).ToList();
        if (linhas.Count == 0) { Erros.Add(UsaLayout ? "Nenhum movimento reconhecido: ajuste o layout até os movimentos aparecerem na lista." : "Marque ao menos um movimento para importar."); return null; }
        var semContra = linhas.Where(l => string.IsNullOrWhiteSpace(l.Contrapartida)).ToList();
        foreach (var l in semContra) Erros.Add($"{l.Data} {l.Descricao}: informe a contrapartida.");
        if (semContra.Count > 0) return null;
        // no extrato sem identificação do banco (PDF/TXT/CSV), a conta contábil do banco separa os movimentos de bancos diferentes
        var extrato = UsaLayout ? _extrato with { Conta = ContaBanco.Trim() } : _extrato;
        return ServicoImportacao.LancamentosDoExtrato(extrato, linhas.Select(l => l.Transacao).ToList(), ContaBanco.Trim(),
                                                      linhas.Select(l => l.Contrapartida.Trim()).ToList(), PrefixoHistorico);
    }
}

/// <summary>Aba Lançamentos contábeis: arquivo no layout padrão do Vorticis (uma linha por partida).</summary>
public partial class ImportacaoLancamentosPadraoViewModel(IContexto contexto) : ImportacaoLancamentosBase(contexto)
{
    protected override OrigemLancamento Origem => OrigemLancamento.Planilha;

    public static string Colunas => string.Join(" ; ", LayoutImportacao.ColunasPadrao);

    [ObservableProperty] private string _arquivo = "";
    [ObservableProperty] private string? _modeloGerado;

    partial void OnArquivoChanged(string value) => LimparPrevia();

    /// <summary>Gera uma planilha modelo (.xlsx) com as colunas do layout padrão e um exemplo.</summary>
    [RelayCommand]
    private void GerarModelo()
    {
        Erros.Clear();
        var colunas = LayoutImportacao.ColunasPadrao.Select(c => new ColunaRelatorio(c)).ToList();
        var exemplo = new List<LinhaRelatorio>
        {
            new(["1", "05/03/2026", "4.2.02.001", "D", "1000,00", "Aluguel de março", "", "123"]),
            new(["1", "05/03/2026", "1.1.01.002", "C", "1000,00", "Aluguel de março", "", "123"]),
        };
        try
        {
            ModeloGerado = ArquivosGerados.Salvar("modelo_importacao_lancamentos.xlsx",
                ExportadorExcel.Gerar([new TabelaRelatorio("Lançamentos", colunas, exemplo)]));
            Mensagem = $"Modelo gerado: {ModeloGerado}";
        }
        catch (IOException ex) { Erros.Add(ex.Message); }
    }

    protected override IReadOnlyList<LancamentoLido>? Ler() =>
        LerPlanilha(Arquivo) is { } t ? Resultado(ImportadorLancamentos.Ler(t, LayoutImportacao.Padrao)) : null;
}
