using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Relatorios;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Linha da lista de duplicatas; <see cref="Marcada"/> serve à baixa em lote.</summary>
public sealed partial class DuplicataLinha(Duplicata duplicata, DateOnly hoje) : ObservableObject
{
    public Duplicata Duplicata { get; } = duplicata;
    public long Id => Duplicata.Id;
    [ObservableProperty] private bool _marcada;
    public string Vencimento => Duplicata.Vencimento.ToString("dd/MM/yyyy");
    public string Tipo => Duplicata.Tipo.Rotulo();
    public string Documento => Duplicata.Titulo;
    public string Participante => Duplicata.ParticipanteNome ?? "";
    public string Valor => Numeros.Moeda(Duplicata.Valor);
    public string Saldo => Numeros.Moeda(Duplicata.Saldo);
    public string Situacao => Duplicata.Situacao.Rotulo();
    public string Atraso => Duplicata.Vencida(hoje) ? $"{Duplicata.DiasAtraso(hoje)} dia(s)" : "";
    public string Origem => Duplicata.Origem == "NOTA" ? "Nota" : "Manual";
}

public sealed record BaixaLinha(BaixaDuplicata Baixa)
{
    public long Id => Baixa.Id;
    public string Data => Baixa.Data.ToString("dd/MM/yyyy");
    public string Principal => Numeros.Moeda(Baixa.Principal);
    public string Juros => Numeros.MoedaOuVazio(Baixa.Juros);
    public string Multa => Numeros.MoedaOuVazio(Baixa.Multa);
    public string Desconto => Numeros.MoedaOuVazio(Baixa.Desconto);
    public string Liquido => Numeros.Moeda(Baixa.Liquido);
    public string Conta => Baixa.ContaDisponivel ?? "";
    public string Extrato => Baixa.PartidaExtratoId is null ? "" : "Conciliada";
}

/// <summary>
/// Lançamentos › Duplicatas: contas a pagar (notas de entrada) e a receber (notas de saída), originadas das notas ou lançadas à mão. Filtra, inclui e altera,
/// baixa (total, parcial ou em lote) gerando o lançamento contábil e estorna baixas.
/// </summary>
/// <summary>ERP › Financeiro › Contas a pagar: a tela de duplicatas só com as a pagar.</summary>
public sealed class ContasPagarViewModel(IContexto contexto) : DuplicatasViewModel(contexto, TipoDuplicata.Pagar);

/// <summary>ERP › Financeiro › Contas a receber: a tela de duplicatas só com as a receber.</summary>
public sealed class ContasReceberViewModel(IContexto contexto) : DuplicatasViewModel(contexto, TipoDuplicata.Receber);

public partial class DuplicatasViewModel : ModuloViewModel
{
    public DuplicatasViewModel(IContexto contexto, TipoDuplicata? tipoFixo = null) : base(contexto)
    {
        TipoFixo = tipoFixo;
        if (tipoFixo is { } t) (_tipoFiltro, _formTipo) = (t == TipoDuplicata.Pagar ? 1 : 2, t == TipoDuplicata.Pagar ? 0 : 1);
    }

    /// <summary>Tela de um tipo só (Contas a pagar ou Contas a receber): o tipo não aparece para escolher.</summary>
    public TipoDuplicata? TipoFixo { get; }
    public bool EscolheTipo => TipoFixo is null;
    public string TituloTela => TipoFixo switch
    {
        TipoDuplicata.Pagar => "Contas a pagar",
        TipoDuplicata.Receber => "Contas a receber",
        _ => "Duplicatas: contas a pagar e a receber",
    };

    public static IReadOnlyList<string> Tipos { get; } = ["A pagar e a receber", "A pagar", "A receber"];
    public static IReadOnlyList<string> TiposNova { get; } = ["A pagar (fornecedor)", "A receber (cliente)"];
    public static IReadOnlyList<string> Situacoes { get; } = ["Em aberto (abertas e parciais)", "Todas", "Abertas", "Parciais", "Liquidadas", "Canceladas"];

    public ObservableCollection<DuplicataLinha> Lista { get; } = [];
    public ObservableCollection<BaixaLinha> Baixas { get; } = [];

    // filtros
    [ObservableProperty] private int _tipoFiltro;
    [ObservableProperty] private DateTime? _vencimentoInicio;
    [ObservableProperty] private DateTime? _vencimentoFim;
    [ObservableProperty] private string _participanteFiltro = "";
    [ObservableProperty] private int _situacaoFiltro;
    [ObservableProperty] private bool _somenteVencidas;
    [ObservableProperty] private string _resumo = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelarDuplicataCommand), nameof(ExcluirCommand), nameof(BaixarCommand))] private DuplicataLinha? _selecionada;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(EstornarBaixaCommand))] private BaixaLinha? _baixaSelecionada;

    // inclusão / edição
    [ObservableProperty] private int _formTipo;
    [ObservableProperty] private string _formParticipante = "";
    [ObservableProperty] private string _formDocumento = "";
    [ObservableProperty] private string _formParcela = "001";
    [ObservableProperty] private DateTime? _formEmissao = DateTime.Today;
    [ObservableProperty] private DateTime? _formVencimento = DateTime.Today.AddDays(30);
    [ObservableProperty] private string _formValor = "";
    [ObservableProperty] private string _formObservacao = "";
    [ObservableProperty] private string _titulo = "Nova duplicata";
    private long _formId;

    // baixa
    [ObservableProperty] private DateTime? _baixaData = DateTime.Today;
    [ObservableProperty] private string _baixaPrincipal = "";
    [ObservableProperty] private string _baixaJuros = "";
    [ObservableProperty] private string _baixaMulta = "";
    [ObservableProperty] private string _baixaDesconto = "";
    [ObservableProperty] private string _baixaConta = "";

    public bool PodeManter => Contexto.Pode(Permissao.FiscalDuplicatasManter);
    public bool PodeBaixar => Contexto.Pode(Permissao.FiscalDuplicatasBaixar);

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeManter));
        OnPropertyChanged(nameof(PodeBaixar));
    }

    public Task CarregarAsync() => ConsultarAsync();

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    private FiltroDuplicatas Filtro() => new(
        TipoFiltro switch { 1 => TipoDuplicata.Pagar, 2 => TipoDuplicata.Receber, _ => null },
        VencimentoInicio is { } vi ? DateOnly.FromDateTime(vi) : null, VencimentoFim is { } vf ? DateOnly.FromDateTime(vf) : null,
        Participante: string.IsNullOrWhiteSpace(ParticipanteFiltro) ? null : ParticipanteFiltro.Trim(),
        Situacao: SituacaoFiltro switch { 2 => SituacaoDuplicata.Aberta, 3 => SituacaoDuplicata.Parcial, 4 => SituacaoDuplicata.Liquidada, 5 => SituacaoDuplicata.Cancelada, _ => null },
        SomenteAbertas: SituacaoFiltro == 0, SomenteVencidas: SomenteVencidas, Hoje: Hoje);

    [RelayCommand]
    private Task ConsultarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var geracao = NovaGeracao();
        var r = await app.Fiscal().Duplicatas.ConsultarAsync(empresa, Filtro(), usuario);
        if (!EhAtual(geracao)) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var selecionadaId = Selecionada?.Id;
        Lista.Clear();
        foreach (var d in r.Valor) Lista.Add(new DuplicataLinha(d, Hoje));
        Selecionada = Lista.FirstOrDefault(l => l.Id == selecionadaId);
        var abertas = r.Valor.Where(d => d.EmAberto).ToList();
        Resumo = $"{r.Valor.Count} duplicata(s). Saldo em aberto: a pagar {Numeros.Moeda(abertas.Where(d => d.Tipo == TipoDuplicata.Pagar).Sum(d => d.Saldo))} · " +
                 $"a receber {Numeros.Moeda(abertas.Where(d => d.Tipo == TipoDuplicata.Receber).Sum(d => d.Saldo))} · vencidas {abertas.Count(d => d.Vencida(Hoje))}.";
    }, Permissao.FiscalDocumentosConsultar);

    partial void OnSelecionadaChanged(DuplicataLinha? value)
    {
        Baixas.Clear();
        if (value is null) return;
        var d = value.Duplicata;
        _formId = d.Id;
        Titulo = $"Duplicata {d.Titulo} — {d.Situacao.Rotulo()}";
        (FormTipo, FormParticipante, FormDocumento, FormParcela, FormEmissao, FormVencimento, FormValor, FormObservacao) =
            (d.Tipo == TipoDuplicata.Pagar ? 0 : 1, d.ParticipanteNome ?? "", d.NumeroDocumento, d.Parcela, d.Emissao.ToDateTime(TimeOnly.MinValue),
             d.Vencimento.ToDateTime(TimeOnly.MinValue), Numeros.Moeda(d.Valor), d.Observacao ?? "");
        BaixaPrincipal = d.EmAberto ? Numeros.Moeda(d.Saldo) : "";
        (BaixaJuros, BaixaMulta, BaixaDesconto) = ("", "", "");
        _ = CarregarBaixasAsync(d.Id);
    }

    private async Task CarregarBaixasAsync(long duplicataId)
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var r = await app.Fiscal().Duplicatas.BaixasAsync(empresa, [duplicataId], sessao.Id);
        if (Selecionada?.Id != duplicataId || !r.Sucesso) return;   // a seleção mudou durante a consulta
        Baixas.Clear();
        foreach (var b in r.Valor) Baixas.Add(new BaixaLinha(b));
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        _formId = 0;
        Titulo = "Nova duplicata";
        (FormTipo, FormParticipante, FormDocumento, FormParcela, FormEmissao, FormVencimento, FormValor, FormObservacao) =
            (TipoFixo == TipoDuplicata.Receber ? 1 : 0, "", "", "001", DateTime.Today, DateTime.Today.AddDays(30), "", "");
        Baixas.Clear();
        Erros.Clear();
        Mensagem = null;
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var tipo = FormTipo == 0 ? TipoDuplicata.Pagar : TipoDuplicata.Receber;
        if (string.IsNullOrWhiteSpace(FormParticipante)) { Erros.Add($"Informe o {(tipo == TipoDuplicata.Pagar ? "fornecedor" : "cliente")}."); return; }
        var part = await Resolvedores.ParticipanteAsync(app, empresa, usuario, FormParticipante);
        if (part is null) { Erros.Add($"Participante \"{FormParticipante.Trim()}\" não encontrado (informe o código, o nome ou o CPF/CNPJ do cadastro)."); return; }
        var valor = Numeros.LerValor(FormValor);
        if (valor is null) { Erros.Add("Informe o valor da duplicata."); return; }
        var dados = new DadosDuplicata(_formId, tipo, part.Value.Id, FormDocumento, FormParcela, Data(FormEmissao, default), Data(FormVencimento, default), valor.Value,
            string.IsNullOrWhiteSpace(FormObservacao) ? null : FormObservacao.Trim());
        var r = await app.Fiscal().Duplicatas.GravarAsync(empresa, dados, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        SituacaoFiltro = 1;   // mostra também as liquidadas e canceladas, para a duplicata gravada sempre aparecer
        await ConsultarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.Id == r.Valor);
        Mensagem = "Duplicata gravada.";
    }, Permissao.FiscalDuplicatasManter);

    private bool TemSelecionada() => Selecionada is not null;

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task CancelarDuplicataAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        if (!await Contexto.ConfirmarAsync("Cancelar duplicata", $"Cancelar a duplicata {s.Documento} de {s.Participante}?", "Cancelar duplicata", "Voltar")) return;
        var r = await app.Fiscal().Duplicatas.CancelarAsync(empresa, s.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        Mensagem = "Duplicata cancelada.";
    }, Permissao.FiscalDuplicatasManter);

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        if (!await Contexto.ConfirmarAsync("Excluir duplicata", $"Excluir a duplicata {s.Documento} de {s.Participante}?", "Excluir", "Cancelar")) return;
        var r = await app.Fiscal().Duplicatas.ExcluirAsync(empresa, s.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionada = null;
        await ConsultarAsync();
        Mensagem = "Duplicata excluída.";
    }, Permissao.FiscalDuplicatasManter);

    /// <summary>Baixa a duplicata selecionada com os valores da área de baixa (total ou parcial, com juros, multa e desconto).</summary>
    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task BaixarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        var principal = Numeros.LerValor(BaixaPrincipal);
        if (principal is null) { Erros.Add("Informe o valor principal da baixa."); return; }
        decimal Opcional(string t, string nome, out bool erro)
        {
            erro = false;
            if (string.IsNullOrWhiteSpace(t)) return 0m;
            if (Numeros.LerValor(t) is { } v) return v;
            Erros.Add($"Valor de {nome} inválido.");
            erro = true;
            return 0m;
        }
        var juros = Opcional(BaixaJuros, "juros", out var e1);
        var multa = Opcional(BaixaMulta, "multa", out var e2);
        var desconto = Opcional(BaixaDesconto, "desconto", out var e3);
        if (e1 || e2 || e3) return;
        var r = await app.Fiscal().Duplicatas.BaixarAsync(empresa, new NovaBaixa(s.Id, Data(BaixaData, Hoje), principal.Value, juros, multa, desconto, BaixaConta), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var id = s.Id;
        await ConsultarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.Id == id);
        var lote = await app.Pendencias.ModoAsync(empresa, usuario) is { Sucesso: true } m && m.Valor == Vorticis.Application.Contabil.ModoContabilizacao.Lote;
        Mensagem = lote ? "Baixa registrada; o lançamento contábil será gerado na Contabilização em lote." : "Baixa registrada e lançamento contábil gerado.";
    }, Permissao.FiscalDuplicatasBaixar);

    /// <summary>Baixa o saldo total de todas as duplicatas marcadas, na data e conta da área de baixa.</summary>
    [RelayCommand]
    private Task BaixarMarcadasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ids = Lista.Where(l => l.Marcada).Select(l => l.Id).ToList();
        if (ids.Count == 0) { Erros.Add("Marque as duplicatas que deseja baixar (primeira coluna da lista)."); return; }
        if (!await Contexto.ConfirmarAsync("Baixar duplicatas marcadas", $"Baixar o saldo total de {ids.Count} duplicata(s) em {Data(BaixaData, Hoje):dd/MM/yyyy}, pela conta {BaixaConta}?", "Baixar", "Cancelar")) return;
        var r = await app.Fiscal().Duplicatas.BaixarLoteAsync(empresa, ids, Data(BaixaData, Hoje), BaixaConta, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await ConsultarAsync();
        foreach (var f in r.Valor.Falhas) foreach (var e in f.Erros) Erros.Add($"{f.Duplicata}: {e}");
        Mensagem = r.Valor.Resumo;
    }, Permissao.FiscalDuplicatasBaixar);

    private bool TemBaixaSelecionada() => BaixaSelecionada is not null;

    [RelayCommand(CanExecute = nameof(TemBaixaSelecionada))]
    private Task EstornarBaixaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (BaixaSelecionada is not { } b) return;
        if (!await Contexto.ConfirmarAsync("Estornar baixa", $"Estornar a baixa de {b.Data} no valor de {b.Principal}? O lançamento contábil será estornado e o valor volta ao saldo da duplicata.", "Estornar", "Cancelar")) return;
        var r = await app.Fiscal().Duplicatas.EstornarBaixaAsync(empresa, b.Id, null, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var id = Selecionada?.Id;
        await ConsultarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.Id == id);
        Mensagem = "Baixa estornada.";
    }, Permissao.FiscalDuplicatasBaixar);
}

/// <summary>Base dos relatórios de duplicatas: filtros de vencimento, participante e data-base.</summary>
public abstract partial class RelatorioDuplicatasViewModel(IContexto contexto, TipoDuplicata tipo) : RelatorioFiscalViewModel(contexto)
{
    public static IReadOnlyList<string> Situacoes { get; } = ["Em aberto", "Todas"];

    protected TipoDuplicata Tipo { get; } = tipo;
    private string _descricao = "";

    [ObservableProperty] private DateTime? _vencimentoInicio;
    [ObservableProperty] private DateTime? _vencimentoFim;
    [ObservableProperty] private string _participante = "";
    [ObservableProperty] private int _situacao;
    [ObservableProperty] private DateTime? _dataBase = DateTime.Today;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RotuloParticipante))] private int _tipoPosicao;

    public abstract string TituloTela { get; }
    protected virtual TipoDuplicata TipoAtual => Tipo;
    public string RotuloParticipante => TipoAtual == TipoDuplicata.Pagar ? "Fornecedor" : "Cliente";
    public virtual bool MostraSituacao => true;
    public virtual bool MostraVencimento => true;
    public virtual bool MostraTipo => false;

    protected override string Parametros => _descricao;
    protected override string NomeArquivo => $"{Prefixo}_{DateTime.Today:yyyyMMdd}";
    protected abstract string Prefixo { get; }
    protected DateOnly Base => Data(DataBase, DateOnly.FromDateTime(DateTime.Today));

    protected FiltroDuplicatas Filtro(bool somenteAbertas) => new(TipoAtual, VencimentoInicio is { } vi ? DateOnly.FromDateTime(vi) : null, VencimentoFim is { } vf ? DateOnly.FromDateTime(vf) : null,
        Participante: string.IsNullOrWhiteSpace(Participante) ? null : Participante.Trim(), SomenteAbertas: somenteAbertas, Hoje: Base, Limite: 100000);

    private void Descrever() =>
        _descricao = $"Data-base {Base:dd/MM/yyyy}" + (VencimentoInicio is { } vi ? $" · vencimento de {vi:dd/MM/yyyy}" : "") + (VencimentoFim is { } vf ? $" até {vf:dd/MM/yyyy}" : "") +
                     (string.IsNullOrWhiteSpace(Participante) ? "" : $" · {RotuloParticipante}: {Participante.Trim()}") + (MostraSituacao ? $" · {Situacoes[Situacao]}" : "");

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Fiscal().Duplicatas.RelatorioAsync(empresa, Filtro(MostraSituacao ? Situacao == 0 : true), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        Descrever();
        Mostrar(Montar(r.Valor));
        Mensagem = r.Valor.Count == 0 ? "Nenhuma duplicata encontrada." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);

    protected abstract TabelaRelatorio Montar(IReadOnlyList<Duplicata> lista);
}

public sealed class RelDuplicatasPagarViewModel(IContexto contexto) : RelatorioDuplicatasViewModel(contexto, TipoDuplicata.Pagar)
{
    public override string TituloTela => "Duplicatas a pagar";
    protected override string TituloRelatorio => "Duplicatas a pagar";
    protected override string Prefixo => "duplicatas_a_pagar";
    protected override TabelaRelatorio Montar(IReadOnlyList<Duplicata> lista) => TabelasFiscais.Duplicatas(TipoAtual, lista, Base);
}

public sealed class RelDuplicatasReceberViewModel(IContexto contexto) : RelatorioDuplicatasViewModel(contexto, TipoDuplicata.Receber)
{
    public override string TituloTela => "Duplicatas a receber";
    protected override string TituloRelatorio => "Duplicatas a receber";
    protected override string Prefixo => "duplicatas_a_receber";
    protected override TabelaRelatorio Montar(IReadOnlyList<Duplicata> lista) => TabelasFiscais.Duplicatas(TipoAtual, lista, Base);
}

/// <summary>Posição por vencimento (aging) das duplicatas a pagar ou a receber na data-base: a vencer e vencidas de 1 a 30, 31 a 60, 61 a 90 e mais de 90 dias.</summary>
public sealed partial class RelPosicaoDuplicatasViewModel(IContexto contexto) : RelatorioDuplicatasViewModel(contexto, TipoDuplicata.Pagar)
{
    public static IReadOnlyList<string> TiposPosicao { get; } = ["Contas a pagar", "Contas a receber"];

    public override string TituloTela => "Posição por vencimento";
    public override bool MostraSituacao => false;
    public override bool MostraVencimento => false;
    public override bool MostraTipo => true;
    protected override string TituloRelatorio => $"Posição por vencimento — {TiposPosicao[TipoPosicao].ToLowerInvariant()}";
    protected override string Prefixo => TipoPosicao == 0 ? "posicao_a_pagar" : "posicao_a_receber";
    protected override TipoDuplicata TipoAtual => TipoPosicao == 0 ? TipoDuplicata.Pagar : TipoDuplicata.Receber;
    protected override TabelaRelatorio Montar(IReadOnlyList<Duplicata> lista) => TabelasFiscais.PosicaoPorVencimento(TipoAtual, Aging.Posicao(lista, Base), Base);
}

public sealed partial class SugestaoLinha(SugestaoConciliacao sugestao) : ObservableObject
{
    public SugestaoConciliacao Sugestao { get; } = sugestao;
    [ObservableProperty] private bool _marcada = true;
    public string Tipo => Sugestao.Duplicata.Tipo.Rotulo();
    public string Documento => Sugestao.Duplicata.Titulo;
    public string Participante => Sugestao.Duplicata.ParticipanteNome ?? "";
    public string Vencimento => Sugestao.Duplicata.Vencimento.ToString("dd/MM/yyyy");
    public string Valor => Numeros.Moeda(Sugestao.Duplicata.Saldo);
    public string DataExtrato => Sugestao.Movimento.Data.ToString("dd/MM/yyyy");
    public string Extrato => string.IsNullOrWhiteSpace(Sugestao.Movimento.Historico) ? "Movimento bancário" : Sugestao.Movimento.Historico;
    public string Motivo => Sugestao.Motivo;
}

/// <summary>
/// Lançamentos › Conciliação de duplicatas: sugere a baixa de duplicatas em aberto a partir do extrato bancário (OFX) já importado, casando por valor,
/// data próxima do vencimento e/ou documento. Ao confirmar, baixa a duplicata e marca a partida bancária como conciliada.
/// </summary>
public partial class ConciliacaoDuplicatasViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<SugestaoLinha> Sugestoes { get; } = [];

    [ObservableProperty] private string _contaBanco = "";
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private string _tolerancia = "5";
    [ObservableProperty] private string _resumo = "";

    public bool PodeBaixar => Contexto.Pode(Permissao.FiscalDuplicatasBaixar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeBaixar));

    [RelayCommand]
    private Task SugerirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (!int.TryParse(Tolerancia.Trim(), out var dias)) { Erros.Add("Informe a tolerância em dias (número inteiro)."); return; }
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var r = await app.Fiscal().Duplicatas.SugerirConciliacaoAsync(empresa, ContaBanco, ini, Data(Fim, ini), dias, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Sugestoes.Clear(); return; }
        Sugestoes.Clear();
        foreach (var s in r.Valor) Sugestoes.Add(new SugestaoLinha(s));
        Resumo = r.Valor.Count == 0 ? "Nenhuma sugestão: confira a conta do banco, o período e se o extrato foi importado em Lançamentos › Importação (aba Extratos)."
                                    : $"{r.Valor.Count} sugestão(ões). Desmarque as que não quiser e use Baixar marcadas.";
    }, Permissao.FiscalDuplicatasBaixar);

    [RelayCommand]
    private Task BaixarMarcadasAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var marcadas = Sugestoes.Where(s => s.Marcada).ToList();
        if (marcadas.Count == 0) { Erros.Add("Marque ao menos uma sugestão."); return; }
        if (!await Contexto.ConfirmarAsync("Baixar pelas sugestões", $"Baixar {marcadas.Count} duplicata(s) e conciliar com o extrato?", "Baixar", "Cancelar")) return;
        var ok = 0;
        foreach (var s in marcadas)
        {
            var r = await app.Fiscal().Duplicatas.ConciliarComExtratoAsync(empresa, s.Sugestao.Duplicata.Id, s.Sugestao.Movimento.PartidaBancoId, usuario);
            if (r.Sucesso) ok++;
            else foreach (var e in r.Erros) Erros.Add($"{s.Documento}: {e}");
        }
        var antes = Erros.ToList();
        await SugerirAsync();   // limpa as mensagens e recarrega as sugestões restantes
        foreach (var e in antes) Erros.Add(e);
        Mensagem = $"{ok} duplicata(s) baixada(s) e conciliada(s) com o extrato.";
    }, Permissao.FiscalDuplicatasBaixar);
}
