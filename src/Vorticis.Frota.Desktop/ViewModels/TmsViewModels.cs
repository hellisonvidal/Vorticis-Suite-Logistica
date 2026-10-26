using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Frota.Desktop.ViewModels;

/// <summary>Código do TMS gravado no banco com o rótulo mostrado na tela.</summary>
public sealed record OpcaoTms(string Valor)
{
    public override string ToString() => RegrasTms.Rotulo(Valor);
    public static IReadOnlyList<OpcaoTms> De(IEnumerable<string> valores) => [.. valores.Select(v => new OpcaoTms(v))];
}

/// <summary>UF e município (base do IBGE) escolhidos em dois combos; usado na origem e no destino do CT-e.</summary>
public sealed partial class SeletorMunicipio : ObservableObject
{
    public static IReadOnlyList<string> Ufs { get; } = [.. Vorticis.Domain.Cadastros.Uf.Todas.Order()];
    public ObservableCollection<Municipio> Municipios { get; } = [];
    [ObservableProperty] private string? _uf;
    [ObservableProperty] private Municipio? _municipio;

    partial void OnUfChanged(string? value)
    {
        Municipios.Clear();
        foreach (var m in BaseMunicipios.DaUf(value).OrderBy(m => m.Nome, StringComparer.CurrentCultureIgnoreCase)) Municipios.Add(m);
        Municipio = null;
    }

    public void Definir(ParadaRota? p)
    {
        Uf = p?.Uf;
        Municipio = p is null ? null : Municipios.FirstOrDefault(m => m.Ibge == p.MunicipioIbge);
    }

    public ParadaRota Parada => Municipio is { } m ? new ParadaRota(m.Uf, m.Ibge, m.Nome) : new ParadaRota(Uf ?? "", "", "");
}

/// <summary>Base das telas do TMS: participantes (clientes e fornecedores) e permissão FROTA_TMS.</summary>
public abstract partial class TmsBase(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaTms)
{
    public ObservableCollection<Opcao> ParticipantesOpcoes { get; } = [];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private string _totais = "";

    partial void OnInicioChanged(DateTime? value) => AoMudarPeriodo();
    partial void OnFimChanged(DateTime? value) => AoMudarPeriodo();
    /// <summary>Telas com período recarregam a lista ao mudar as datas.</summary>
    protected virtual void AoMudarPeriodo() { }

    protected async Task CarregarParticipantesAsync(VorticisApp app, long empresa, long usuario, string vazio = "(escolha)")
    {
        var participantes = await app.Frota().ParticipantesAsync(empresa, usuario);
        ParticipantesOpcoes.Clear();
        ParticipantesOpcoes.Add(new Opcao(null, vazio));
        foreach (var p in participantes) ParticipantesOpcoes.Add(new Opcao(p.Id, p.ToString()));
    }

    protected void SalvarPdf(string arquivo, Func<byte[]> gerar, string oque)
    {
        try { Mensagem = $"{oque} salvo em PDF: {ArquivosGerados.Salvar(arquivo, gerar())}"; }
        catch (Exception ex) { Erros.Add($"Não foi possível gerar o PDF: {ex.Message}"); }
    }

    protected static string M(decimal v) => Formato.Moeda(v);
}

// ---------------------------------------------------------------------------------------------- tabelas de frete

public sealed record TabelaFreteLinha(TabelaFrete T)
{
    public int Codigo => T.Codigo;
    public string Descricao => T.Descricao;
    public string Abrangencia => T.Abrangencia;
    public string ValorKg => T.ValorKg.ToString("N4", Formato.PtBr);
    public string Minimo => Formato.Moeda(T.FreteMinimo);
    public string Prazo => T.PrazoDias > 0 ? $"{T.PrazoDias} dia(s)" : "";
    public string Situacao => T.Ativa ? "Ativa" : "Inativa";
}

/// <summary>Cadastros › Tabelas de frete: componentes do frete por rota ou por UF de origem e destino, com simulação.</summary>
public partial class TabelasFreteViewModel(IContexto contexto) : TmsBase(contexto)
{
    public ObservableCollection<TabelaFreteLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> RotasOpcoes { get; } = [];
    public static IReadOnlyList<string> UfsOpcionais { get; } = ["(todas)", .. Vorticis.Domain.Cadastros.Uf.Todas.Order()];
    [ObservableProperty] private TabelaFreteLinha? _selecionada;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private Opcao? _rota;
    [ObservableProperty] private string? _ufOrigem = "(todas)";
    [ObservableProperty] private string? _ufDestino = "(todas)";
    [ObservableProperty] private string _valorKg = "";
    [ObservableProperty] private string _freteMinimo = "";
    [ObservableProperty] private string _fatorCubagem = "300";
    [ObservableProperty] private string _adValorem = "";
    [ObservableProperty] private string _gris = "";
    [ObservableProperty] private string _pedagio = "";
    [ObservableProperty] private string _taxaColeta = "";
    [ObservableProperty] private string _taxaEntrega = "";
    [ObservableProperty] private string _outrasTaxas = "";
    [ObservableProperty] private string _aliquotaIcms = "";
    [ObservableProperty] private string _prazoDias = "";
    [ObservableProperty] private bool _ativa = true;
    [ObservableProperty] private string _simPeso = "1000";
    [ObservableProperty] private string _simM3 = "";
    [ObservableProperty] private string _simValor = "10000";
    [ObservableProperty] private string _simulacao = "";

    partial void OnSelecionadaChanged(TabelaFreteLinha? value)
    {
        if (value?.T is not { } t) return;
        (Codigo, Descricao, Ativa) = (t.Codigo.ToString(), t.Descricao, t.Ativa);
        Rota = Escolher(RotasOpcoes, t.RotaId, () => t.RotaNome ?? "(rota)");
        (UfOrigem, UfDestino) = (t.UfOrigem ?? "(todas)", t.UfDestino ?? "(todas)");
        (ValorKg, FreteMinimo, FatorCubagem) = (t.ValorKg.ToString("0.####", Formato.PtBr), Ext.Num(t.FreteMinimo), Ext.Num(t.FatorCubagem));
        (AdValorem, Gris, Pedagio) = (t.AdValoremPct.ToString("0.####", Formato.PtBr), t.GrisPct.ToString("0.####", Formato.PtBr), Ext.Num(t.PedagioFracao));
        (TaxaColeta, TaxaEntrega, OutrasTaxas, AliquotaIcms, PrazoDias) = (Ext.Num(t.TaxaColeta), Ext.Num(t.TaxaEntrega), Ext.Num(t.OutrasTaxas), Ext.Num(t.AliquotaIcms), t.PrazoDias.ToString());
        Simular();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().TabelasFreteAsync(empresa, sessao.Id);
        var rotas = await app.Frota().RotasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        RotasOpcoes.Clear();
        RotasOpcoes.Add(new Opcao(null, "(nenhuma: vale pelas UFs)"));
        foreach (var r in rotas.Where(r => r.Ativa)) RotasOpcoes.Add(new Opcao(r.Id, $"{r.Codigo} – {r.Descricao}"));
        Lista.Clear();
        foreach (var t in itens) Lista.Add(new TabelaFreteLinha(t));
        Totais = $"{itens.Count} tabela(s) · {itens.Count(t => t.Ativa)} ativa(s)";
        Rota ??= RotasOpcoes.FirstOrDefault();
        if (Selecionada is null && Codigo == "") Codigo = ((itens.Count == 0 ? 0 : itens.Max(t => t.Codigo)) + 1).ToString();
    }

    private static decimal V(string s) => Formato.LerValor(s) ?? 0;
    private static string? Uf(string? s) => s is null or "(todas)" ? null : s;

    private TabelaFrete Montar() => new(Selecionada?.T.Id ?? 0, Ext.Int(Codigo) ?? 0, Descricao, Rota?.Id, Uf(UfOrigem), Uf(UfDestino), V(ValorKg), V(FreteMinimo), V(FatorCubagem),
                                        V(AdValorem), V(Gris), V(Pedagio), V(TaxaColeta), V(TaxaEntrega), V(OutrasTaxas), V(AliquotaIcms), Ext.Int(PrazoDias) ?? 0, Ativa);

    [RelayCommand]
    private void Simular()
    {
        var f = RegrasTms.Calcular(Montar(), V(SimPeso), V(SimM3), V(SimValor), coleta: true, entrega: true);
        Simulacao = $"Peso taxado {f.PesoTaxado.ToString("N2", Formato.PtBr)} kg · frete-peso {M(f.FretePeso)} · ad valorem {M(f.AdValorem)} · GRIS {M(f.Gris)} · pedágio {M(f.Pedagio)} · "
                    + $"taxas {M(f.Taxas)} · ICMS {M(f.Icms)} · TOTAL {M(f.Total)}";
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        Codigo = ((Lista.Count == 0 ? 0 : Lista.Max(t => t.Codigo)) + 1).ToString();
        (Descricao, UfOrigem, UfDestino, ValorKg, FreteMinimo, FatorCubagem, AdValorem, Gris, Pedagio) = ("", "(todas)", "(todas)", "", "", "300", "", "", "");
        (TaxaColeta, TaxaEntrega, OutrasTaxas, AliquotaIcms, PrazoDias, Ativa, Simulacao) = ("", "", "", "", "", true, "");
        Rota = RotasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Frota().GravarTabelaFreteAsync(empresa, Montar(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.T.Id == r.Valor);
        Mensagem = "Tabela de frete gravada.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.T is not { } t) { Erros.Add("Selecione a tabela."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir tabela de frete", $"Excluir a tabela {t.Codigo} – {t.Descricao}?")) return;
        var r = await app.Frota().ExcluirTabelaFreteAsync(empresa, t.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Tabela de frete excluída.";
    }, Permissao.FrotaTms);
}

// ---------------------------------------------------------------------------------------------- cotações

public sealed record CotacaoLinha(Cotacao C)
{
    public int Numero => C.Numero;
    public string Data => Ext.Data(C.Data);
    public string Validade => Ext.Data(C.Validade);
    public string Cliente => C.Cliente ?? "";
    public string Trecho => $"{C.Origem} → {C.Destino}";
    public string Peso => Ext.Num(C.PesoKg);
    public string Valor => Formato.Moeda(C.ValorTotal);
    public string Situacao => RegrasTms.Rotulo(C.Situacao);
}

/// <summary>Emissão › Cotações de frete: o valor sai da tabela de frete; aprovada, a cotação é escolhida no CT-e.</summary>
public partial class CotacoesViewModel(IContexto contexto) : TmsBase(contexto)
{
    private List<TabelaFrete> _tabelas = [];
    public ObservableCollection<CotacaoLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> TabelasOpcoes { get; } = [];
    public static IReadOnlyList<OpcaoFiltro> FiltroSituacoes { get; } =
        [new(null, "(todas)"), .. RegrasTms.SituacoesCotacao.Select(s => new OpcaoFiltro(s, RegrasTms.Rotulo(s)))];
    [ObservableProperty] private OpcaoFiltro? _filtroSituacao = FiltroSituacoes[0];
    [ObservableProperty] private CotacaoLinha? _selecionada;
    [ObservableProperty] private DateTime? _data = DateTime.Today;
    [ObservableProperty] private DateTime? _validade = DateTime.Today.AddDays(15);
    [ObservableProperty] private Opcao? _cliente;
    [ObservableProperty] private Opcao? _tabela;
    [ObservableProperty] private string _origem = "";
    [ObservableProperty] private string _destino = "";
    [ObservableProperty] private string _peso = "";
    [ObservableProperty] private string _volumeM3 = "";
    [ObservableProperty] private string _valorMercadoria = "";
    [ObservableProperty] private bool _coleta;
    [ObservableProperty] private bool _entrega = true;
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _calculo = "";

    protected override void AoMudarPeriodo() => _ = CarregarAsync();
    partial void OnFiltroSituacaoChanged(OpcaoFiltro? value) => _ = CarregarAsync();
    partial void OnTabelaChanged(Opcao? value) => Calcular();
    partial void OnPesoChanged(string value) => Calcular();
    partial void OnVolumeM3Changed(string value) => Calcular();
    partial void OnValorMercadoriaChanged(string value) => Calcular();
    partial void OnColetaChanged(bool value) => Calcular();
    partial void OnEntregaChanged(bool value) => Calcular();

    private void Calcular()
    {
        if (_tabelas.FirstOrDefault(t => t.Id == Tabela?.Id) is not { } t) { Calculo = "Escolha a tabela de frete."; return; }
        var f = RegrasTms.Calcular(t, Formato.LerValor(Peso) ?? 0, Formato.LerValor(VolumeM3) ?? 0, Formato.LerValor(ValorMercadoria) ?? 0, Coleta, Entrega);
        Calculo = $"Frete-peso {M(f.FretePeso)} + ad valorem {M(f.AdValorem)} + GRIS {M(f.Gris)} + pedágio {M(f.Pedagio)} + taxas {M(f.Taxas)} + ICMS {M(f.Icms)} = {M(f.Total)}"
                  + (t.PrazoDias > 0 ? $" · prazo {t.PrazoDias} dia(s)" : "");
    }

    partial void OnSelecionadaChanged(CotacaoLinha? value)
    {
        if (value?.C is not { } c) return;
        (Data, Validade) = (Ext.Dt(c.Data), Ext.Dt(c.Validade));
        Cliente = Escolher(ParticipantesOpcoes, c.ClienteId, () => c.Cliente ?? "");
        Tabela = Escolher(TabelasOpcoes, c.TabelaId, () => c.Tabela ?? "");
        (Origem, Destino, Peso, VolumeM3, ValorMercadoria) = (c.Origem, c.Destino, Ext.Num(c.PesoKg), Ext.Num(c.VolumeM3), Ext.Num(c.ValorMercadoria));
        (Coleta, Entrega, Observacao) = (c.Coleta, c.Entrega, c.Observacao ?? "");
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarParticipantesAsync(app, empresa, sessao.Id);
        var tabelas = await app.Frota().TabelasFreteAsync(empresa, sessao.Id);
        var itens = await app.Frota().CotacoesAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), FiltroSituacao?.Valor, sessao.Id);
        if (!EhAtual(g)) return;
        _tabelas = tabelas;
        TabelasOpcoes.Clear();
        TabelasOpcoes.Add(new Opcao(null, "(escolha)"));
        foreach (var t in tabelas.Where(t => t.Ativa)) TabelasOpcoes.Add(new Opcao(t.Id, $"{t} ({t.Abrangencia})"));
        Lista.Clear();
        foreach (var c in itens) Lista.Add(new CotacaoLinha(c));
        Totais = $"{itens.Count} cotação(ões) · {itens.Count(c => c.Situacao == "APROVADA")} aprovada(s) · {itens.Count(c => c.Situacao == "CONVERTIDA")} convertida(s) em CT-e · "
                 + $"valor aprovado {M(itens.Where(c => c.Situacao is "APROVADA" or "CONVERTIDA").Sum(c => c.ValorTotal))}";
        Cliente ??= ParticipantesOpcoes.FirstOrDefault();
        Tabela ??= TabelasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Data, Validade, Origem, Destino, Peso, VolumeM3, ValorMercadoria, Coleta, Entrega, Observacao) = (DateTime.Today, DateTime.Today.AddDays(15), "", "", "", "", "", false, true, "");
        (Cliente, Tabela) = (ParticipantesOpcoes.FirstOrDefault(), TabelasOpcoes.FirstOrDefault());
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var c = new Cotacao(Selecionada?.C.Id ?? 0, Selecionada?.C.Numero ?? 0, Ext.Dia(Data), Ext.Dia(Validade), Cliente?.Id ?? 0, Tabela?.Id ?? 0, Origem, Destino,
                            Formato.LerValor(Peso) ?? 0, Formato.LerValor(VolumeM3) ?? 0, Formato.LerValor(ValorMercadoria) ?? 0, Coleta, Entrega, 0, "ABERTA", Observacao);
        var r = await app.Frota().GravarCotacaoAsync(empresa, c, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.C.Id == r.Valor.Id);
        Mensagem = $"Cotação nº {r.Valor.Numero} gravada: {M(r.Valor.ValorTotal)}.";
    }, Permissao.FrotaTms);

    private Task Mudar(string situacao, string texto) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.C is not { } c) { Erros.Add("Selecione a cotação."); return; }
        var r = await app.Frota().MudarSituacaoCotacaoAsync(empresa, c.Id, situacao, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.C.Id == c.Id);
        Mensagem = $"Cotação nº {c.Numero} {texto}.";
    }, Permissao.FrotaTms);

    [RelayCommand] private Task AprovarAsync() => Mudar("APROVADA", "aprovada: escolha-a no CT-e (Emissão › Conhecimentos de transporte)");
    [RelayCommand] private Task RecusarAsync() => Mudar("RECUSADA", "recusada");
    [RelayCommand] private Task ReabrirAsync() => Mudar("ABERTA", "reaberta");

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.C is not { } c) { Erros.Add("Selecione a cotação."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir cotação", $"Excluir a cotação nº {c.Numero}?")) return;
        var r = await app.Frota().ExcluirCotacaoAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Cotação excluída.";
    }, Permissao.FrotaTms);
}

// ---------------------------------------------------------------------------------------------- conhecimentos (CT-e)

public sealed record CteLinha(Cte C)
{
    public string Numero => $"{C.Serie}-{C.Numero}";
    public string Emissao => Ext.Data(C.Emissao);
    public string Tomador => C.Tomador ?? "";
    public string Destinatario => C.Destinatario ?? "";
    public string Trecho => C.Trecho;
    public string Peso => Ext.Num(C.PesoKg);
    public string Valor => Formato.Moeda(C.ValorTotal);
    public string Previsao => Ext.Data(C.PrevisaoEntrega);
    public string Prazo => RegrasTms.SituacaoPrazo(C.PrevisaoEntrega, C.Situacao, C.EntregueEm, Ext.Hoje);
    public string Situacao => RegrasTms.Rotulo(C.Situacao);
    public string Fatura => C.FaturaNumero is { } f ? f.ToString() : "";
}

/// <summary>
/// Emissão › Conhecimentos de transporte (CT-e): digitação com o frete pela tabela (ou pela cotação aprovada / ordem de coleta), emissão com a chave
/// de acesso, cancelamento e minuta em PDF. A transmissão à SEFAZ fica para depois: a chave vem do emissor usado pela transportadora.
/// </summary>
public partial class CtesViewModel(IContexto contexto) : TmsBase(contexto)
{
    private List<TabelaFrete> _tabelas = [];
    private List<Cotacao> _cotacoes = [];
    private List<Coleta> _coletas = [];
    private List<Rota> _rotas = [];
    public ObservableCollection<CteLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> TabelasOpcoes { get; } = [];
    public ObservableCollection<Opcao> CotacoesOpcoes { get; } = [];
    public ObservableCollection<Opcao> ColetasOpcoes { get; } = [];
    public ObservableCollection<Opcao> ViagensOpcoes { get; } = [];
    public SeletorMunicipio Origem { get; } = new() { Uf = "SP" };
    public SeletorMunicipio Destino { get; } = new() { Uf = "SP" };
    public static IReadOnlyList<OpcaoFiltro> FiltroSituacoes { get; } =
        [new(null, "(todas)"), .. RegrasTms.SituacoesCte.Select(s => new OpcaoFiltro(s, RegrasTms.Rotulo(s)))];
    [ObservableProperty] private OpcaoFiltro? _filtroSituacao = FiltroSituacoes[0];
    [ObservableProperty] private CteLinha? _selecionado;
    [ObservableProperty] private string _serie = "1";
    [ObservableProperty] private DateTime? _emissao = DateTime.Today;
    [ObservableProperty] private string _cfop = "";
    [ObservableProperty] private Opcao? _remetente;
    [ObservableProperty] private Opcao? _destinatario;
    [ObservableProperty] private Opcao? _tomador;
    [ObservableProperty] private Opcao? _tabela;
    [ObservableProperty] private Opcao? _cotacao;
    [ObservableProperty] private Opcao? _coleta;
    [ObservableProperty] private Opcao? _viagem;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private string _produto = "";
    [ObservableProperty] private string _volumes = "";
    [ObservableProperty] private string _peso = "";
    [ObservableProperty] private string _volumeM3 = "";
    [ObservableProperty] private string _valorMercadoria = "";
    [ObservableProperty] private string _chavesNfe = "";
    [ObservableProperty] private bool _cobraColeta;
    [ObservableProperty] private bool _cobraEntrega = true;
    [ObservableProperty] private string _fretePeso = "";
    [ObservableProperty] private string _adValorem = "";
    [ObservableProperty] private string _gris = "";
    [ObservableProperty] private string _pedagio = "";
    [ObservableProperty] private string _taxas = "";
    [ObservableProperty] private string _aliquotaIcms = "";
    [ObservableProperty] private DateTime? _previsao;
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _resumoFrete = "";
    [ObservableProperty] private string _chave = "";
    [ObservableProperty] private string _protocolo = "";
    [ObservableProperty] private string _situacaoTexto = "Novo (digitado)";

    protected override void AoMudarPeriodo() => _ = CarregarAsync();
    partial void OnFiltroSituacaoChanged(OpcaoFiltro? value) => _ = CarregarAsync();
    partial void OnFretePesoChanged(string value) => Resumir();
    partial void OnAdValoremChanged(string value) => Resumir();
    partial void OnGrisChanged(string value) => Resumir();
    partial void OnPedagioChanged(string value) => Resumir();
    partial void OnTaxasChanged(string value) => Resumir();
    partial void OnAliquotaIcmsChanged(string value) => Resumir();

    private static decimal V(string s) => Formato.LerValor(s) ?? 0;
    private ComponentesFrete Componentes() => RegrasTms.ComIcms(V(FretePeso), V(AdValorem), V(Gris), V(Pedagio), V(Taxas), V(AliquotaIcms));
    private void Resumir() { var f = Componentes(); ResumoFrete = $"Sem ICMS {M(f.SemIcms)} · ICMS {M(f.Icms)} · TOTAL DA PRESTAÇÃO {M(f.Total)}"; }

    private void Preencher(ComponentesFrete f)
    {
        (FretePeso, AdValorem, Gris, Pedagio, Taxas, AliquotaIcms) = (Ext.Num(f.FretePeso), Ext.Num(f.AdValorem), Ext.Num(f.Gris), Ext.Num(f.Pedagio), Ext.Num(f.Taxas), Ext.Num(f.AliquotaIcms));
        Resumir();
    }

    /// <summary>A cotação aprovada traz o tomador, a tabela, a carga e o frete.</summary>
    partial void OnCotacaoChanged(Opcao? value)
    {
        if (_cotacoes.FirstOrDefault(c => c.Id == value?.Id) is not { } c || Selecionado is not null) return;
        Tomador = Escolher(ParticipantesOpcoes, c.ClienteId);
        Tabela = Escolher(TabelasOpcoes, c.TabelaId);
        (Peso, VolumeM3, ValorMercadoria, CobraColeta, CobraEntrega) = (Ext.Num(c.PesoKg), Ext.Num(c.VolumeM3), Ext.Num(c.ValorMercadoria), c.Coleta, c.Entrega);
        CalcularPelaTabela();
    }

    /// <summary>A ordem de coleta traz remetente, destinatário, carga, veículo, motorista e (pela rota) a origem e o destino.</summary>
    partial void OnColetaChanged(Opcao? value)
    {
        if (_coletas.FirstOrDefault(c => c.Id == value?.Id) is not { } c || Selecionado is not null) return;
        Remetente = Escolher(ParticipantesOpcoes, c.RemetenteId);
        Destinatario = Escolher(ParticipantesOpcoes, c.DestinatarioId);
        if (Tomador?.Id is null) Tomador = Remetente;
        (Produto, Volumes, Peso, ValorMercadoria, CobraColeta) = (c.Mercadoria ?? Produto, c.Volumes.ToString(), Ext.Num(c.PesoKg), Ext.Num(c.ValorMercadoria), true);
        Veiculo = Escolher(VeiculosOpcoes, c.VeiculoId);
        Motorista = Escolher(MotoristasOpcoes, c.MotoristaId);
        if (_rotas.FirstOrDefault(r => r.Id == c.RotaId) is { Paradas.Count: >= 2 } rota) { Origem.Definir(rota.Paradas[0]); Destino.Definir(rota.Paradas[^1]); }
    }

    partial void OnSelecionadoChanged(CteLinha? value)
    {
        if (value?.C is not { } c) return;
        (Serie, Emissao, Cfop, Produto, Volumes, Peso, VolumeM3) = (c.Serie.ToString(), Ext.Dt(c.Emissao), c.Cfop ?? "", c.Produto ?? "", c.Volumes.ToString(), Ext.Num(c.PesoKg), Ext.Num(c.VolumeM3));
        (ValorMercadoria, ChavesNfe, Previsao, Observacao, Chave, Protocolo) = (Ext.Num(c.ValorMercadoria), c.ChavesNfe ?? "", Ext.DtOpc(c.PrevisaoEntrega), c.Observacao ?? "", c.Chave ?? "", c.Protocolo ?? "");
        Remetente = Escolher(ParticipantesOpcoes, c.RemetenteId, () => c.Remetente ?? "");
        Destinatario = Escolher(ParticipantesOpcoes, c.DestinatarioId, () => c.Destinatario ?? "");
        Tomador = Escolher(ParticipantesOpcoes, c.TomadorId, () => c.Tomador ?? "");
        Tabela = Escolher(TabelasOpcoes, c.TabelaId, () => "(tabela)");
        Cotacao = Escolher(CotacoesOpcoes, c.CotacaoId, () => "(cotação convertida)");
        Coleta = Escolher(ColetasOpcoes, c.ColetaId, () => "(coleta)");
        Viagem = Escolher(ViagensOpcoes, c.ViagemId, () => "(viagem)");
        Veiculo = Escolher(VeiculosOpcoes, c.VeiculoId, () => c.Placa ?? "");
        Motorista = Escolher(MotoristasOpcoes, c.MotoristaId, () => c.MotoristaNome ?? "");
        Origem.Definir(c.Origem);
        Destino.Definir(c.Destino);
        Preencher(c.Frete);
        SituacaoTexto = RegrasTms.Rotulo(c.Situacao) + (c.Recebedor is { } r ? $" – recebido por {r}" : "") + (c.FaturaNumero is { } f ? $" – fatura nº {f}" : "");
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, "(nenhum)");
        await CarregarParticipantesAsync(app, empresa, sessao.Id);
        var tabelas = await app.Frota().TabelasFreteAsync(empresa, sessao.Id);
        var cotacoes = await app.Frota().CotacoesAsync(empresa, Ext.Hoje.AddDays(-180), Ext.Hoje.AddDays(30), "APROVADA", sessao.Id);
        var coletas = await app.Frota().ColetasAsync(empresa, Ext.Hoje.AddDays(-90), Ext.Hoje.AddDays(30), null, sessao.Id);
        var viagens = await app.Frota().ViagensAsync(empresa, Ext.Hoje.AddDays(-90), Ext.Hoje.AddDays(30), sessao.Id);
        var rotas = await app.Frota().RotasAsync(empresa, sessao.Id);
        var itens = await app.Frota().CtesAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), FiltroSituacao?.Valor, sessao.Id);
        if (!EhAtual(g)) return;
        (_tabelas, _cotacoes, _coletas, _rotas) = (tabelas, cotacoes, coletas, rotas);
        void Encher(ObservableCollection<Opcao> o, string vazio, IEnumerable<Opcao> itens2) { o.Clear(); o.Add(new Opcao(null, vazio)); foreach (var i in itens2) o.Add(i); }
        Encher(TabelasOpcoes, "(nenhuma)", tabelas.Where(t => t.Ativa).Select(t => new Opcao(t.Id, $"{t} ({t.Abrangencia})")));
        Encher(CotacoesOpcoes, "(nenhuma)", cotacoes.Select(c => new Opcao(c.Id, $"Nº {c.Numero} – {c.Cliente} – {M(c.ValorTotal)}")));
        Encher(ColetasOpcoes, "(nenhuma)", coletas.Where(c => c.Situacao != "CANCELADA").Select(c => new Opcao(c.Id, $"Nº {c.Numero} – {c.AgendadaEm:dd/MM} – {c.Remetente ?? c.EnderecoColeta}")));
        Encher(ViagensOpcoes, "(nenhuma)", viagens.Where(v => v.Situacao != "CANCELADA").Select(v => new Opcao(v.Id, $"Nº {v.Numero} – {v.Origem} → {v.Destino}")));
        Lista.Clear();
        foreach (var c in itens) Lista.Add(new CteLinha(c));
        var validos = itens.Where(c => c.Situacao != "CANCELADO").ToList();
        Totais = $"{itens.Count} CT-e · {itens.Count(c => c.Situacao == "DIGITADO")} digitado(s) · {itens.Count(c => c.Situacao == "EM_TRANSITO")} em trânsito · "
                 + $"{itens.Count(c => c.Situacao == "ENTREGUE")} entregue(s) · frete {M(validos.Sum(c => c.ValorTotal))} · ICMS {M(validos.Sum(c => c.Frete.Icms))}";
        if (Selecionado is null && Remetente is null) Novo();
    }

    [RelayCommand]
    private void CalcularPelaTabela()
    {
        Erros.Clear();
        if (_tabelas.FirstOrDefault(t => t.Id == Tabela?.Id) is not { } t) { Erros.Add("Escolha a tabela de frete para calcular."); return; }
        Preencher(RegrasTms.Calcular(t, V(Peso), V(VolumeM3), V(ValorMercadoria), CobraColeta, CobraEntrega));
        if (!RegrasTms.Atende(t, Origem.Uf, Destino.Uf, null) && t.RotaId is null) Mensagem = "Atenção: a tabela escolhida é de outras UFs de origem/destino.";
        Previsao = Ext.DtOpc(RegrasTms.Previsao(Ext.Dia(Emissao), t.PrazoDias)) ?? Previsao;
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Serie, Emissao, Cfop, Produto, Volumes, Peso, VolumeM3, ValorMercadoria, ChavesNfe) = (Serie is "" ? "1" : Serie, DateTime.Today, "", "", "", "", "", "", "");
        (CobraColeta, CobraEntrega, Previsao, Observacao, Chave, Protocolo, SituacaoTexto) = (false, true, null, "", "", "", "Novo (digitado)");
        (Remetente, Destinatario, Tomador) = (ParticipantesOpcoes.FirstOrDefault(), ParticipantesOpcoes.FirstOrDefault(), ParticipantesOpcoes.FirstOrDefault());
        (Tabela, Cotacao, Coleta, Viagem, Veiculo, Motorista) = (TabelasOpcoes.FirstOrDefault(), CotacoesOpcoes.FirstOrDefault(), ColetasOpcoes.FirstOrDefault(),
                                                                ViagensOpcoes.FirstOrDefault(), VeiculosOpcoes.FirstOrDefault(), MotoristasOpcoes.FirstOrDefault());
        Preencher(RegrasTms.ComIcms(0, 0, 0, 0, 0, 0));
    }

    private Cte Montar() => new(Selecionado?.C.Id ?? 0, Ext.Int(Serie) ?? -1, Selecionado?.C.Numero ?? 0, Ext.Dia(Emissao), Cfop, Remetente?.Id ?? 0, Destinatario?.Id ?? 0,
                                Tomador?.Id ?? 0, Origem.Parada, Destino.Parada, Tabela?.Id, Cotacao?.Id, Coleta?.Id, Viagem?.Id, Veiculo?.Id, Motorista?.Id, Produto,
                                Ext.Int(Volumes) ?? 0, V(Peso), V(VolumeM3), V(ValorMercadoria), ChavesNfe, Componentes(), Ext.DiaOpc(Previsao), null, null, "DIGITADO", null, null,
                                null, Observacao);

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Frota().GravarCteAsync(empresa, Montar(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.C.Id == r.Valor.Id);
        Mensagem = $"CT-e {r.Valor.Serie}-{r.Valor.Numero} gravado (digitado): {M(r.Valor.ValorTotal)}. Depois de autorizado no emissor, informe a chave e clique em Registrar emissão.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task EmitirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione o CT-e."); return; }
        var r = await app.Frota().EmitirCteAsync(empresa, c.Id, Chave, Protocolo, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.C.Id == c.Id);
        Mensagem = $"CT-e {c.Serie}-{c.Numero} emitido. Inclua-o num manifesto (Emissão › Manifestos de carga) e acompanhe a entrega em Operações › Entregas e ocorrências.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione o CT-e."); return; }
        if (!await Contexto.ConfirmarAsync("Cancelar CT-e", $"Cancelar o CT-e {c.Serie}-{c.Numero}? Se ele já foi autorizado, cancele também no emissor da SEFAZ.")) return;
        var r = await app.Frota().CancelarCteAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"CT-e {c.Serie}-{c.Numero} cancelado.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione o CT-e."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir CT-e", $"Excluir o CT-e digitado {c.Serie}-{c.Numero}?")) return;
        var r = await app.Frota().ExcluirCteAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "CT-e excluído.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private void Imprimir()
    {
        Erros.Clear();
        Mensagem = null;
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione o CT-e."); return; }
        SalvarPdf($"minuta_cte_{c.Serie}_{c.Numero}.pdf", () => ServicoFrota.MinutaCtePdf(c, Contexto.DescricaoEmpresa, Contexto.Sessao?.Nome ?? ""), "Minuta do CT-e");
    }
}

// ---------------------------------------------------------------------------------------------- manifestos (MDF-e)

public sealed partial class CteMarcado(Cte c, bool marcado) : ObservableObject
{
    public Cte C { get; } = c;
    [ObservableProperty] private bool _marcado = marcado;
    public string Texto => $"{C.Serie}-{C.Numero} · {C.Destinatario} · {C.Trecho} · {Ext.Num(C.PesoKg)} kg · {Formato.Moeda(C.ValorTotal)}";
}

public sealed record ManifestoLinha(Manifesto M)
{
    public string Numero => $"{M.Serie}-{M.Numero}";
    public string Emissao => Ext.Data(M.Emissao);
    public string Placa => RegrasFrota.FormatarPlaca(M.Placa ?? "");
    public string Motorista => M.MotoristaNome ?? "";
    public string Percurso => $"{M.UfInicio}{(M.UfsPercurso is null ? "" : " › " + M.UfsPercurso.Replace(",", " › "))} › {M.UfFim}";
    public int Ctes => M.Ctes.Count;
    public string Peso => Ext.Num(M.PesoKg);
    public string Valor => Formato.Moeda(M.ValorMercadoria);
    public string Situacao => RegrasTms.Rotulo(M.Situacao) + (M.EncerradoEm is { } d ? $" em {d:dd/MM/yyyy}" : "");
}

/// <summary>Emissão › Manifestos de carga (MDF-e): reúne os CT-e emitidos de um veículo e motorista; encerrar ao fim da viagem.</summary>
public partial class ManifestosViewModel(IContexto contexto) : TmsBase(contexto)
{
    public ObservableCollection<ManifestoLinha> Lista { get; } = [];
    public ObservableCollection<CteMarcado> Ctes { get; } = [];
    public ObservableCollection<Opcao> ViagensOpcoes { get; } = [];
    public static IReadOnlyList<string> Ufs { get; } = SeletorMunicipio.Ufs;
    [ObservableProperty] private ManifestoLinha? _selecionado;
    [ObservableProperty] private string _serie = "1";
    [ObservableProperty] private DateTime? _emissao = DateTime.Today;
    [ObservableProperty] private string? _ufInicio = "SP";
    [ObservableProperty] private string? _ufFim = "SP";
    [ObservableProperty] private string _ufsPercurso = "";
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private Opcao? _viagem;
    [ObservableProperty] private string _chave = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private DateTime? _encerramento = DateTime.Today;

    protected override void AoMudarPeriodo() => _ = CarregarAsync();

    partial void OnSelecionadoChanged(ManifestoLinha? value)
    {
        if (value?.M is not { } m) return;
        (Serie, Emissao, UfInicio, UfFim, UfsPercurso, Chave, Observacao) = (m.Serie.ToString(), Ext.Dt(m.Emissao), m.UfInicio, m.UfFim, m.UfsPercurso ?? "", m.Chave ?? "", m.Observacao ?? "");
        Veiculo = Escolher(VeiculosOpcoes, m.VeiculoId, () => m.Placa ?? "");
        Motorista = Escolher(MotoristasOpcoes, m.MotoristaId, () => m.MotoristaNome ?? "");
        Viagem = Escolher(ViagensOpcoes, m.ViagemId, () => "(viagem)");
        _ = CarregarCtesAsync(m);
    }

    private async Task CarregarCtesAsync(Manifesto? m)
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        Ctes.Clear();
        var marcados = m?.Ctes.ToHashSet() ?? [];
        if (m is { Situacao: not "ABERTO" })
        {
            // encerrado ou cancelado: mostra só os CT-e que estavam nele
            foreach (var c in (await app.Frota().CtesAsync(empresa, DateOnly.MinValue, DateOnly.MaxValue, null, sessao.Id)).Where(c => marcados.Contains(c.Id))) Ctes.Add(new CteMarcado(c, true));
            return;
        }
        foreach (var c in await app.Frota().CtesParaManifestoAsync(empresa, m?.Id ?? 0, sessao.Id)) Ctes.Add(new CteMarcado(c, marcados.Contains(c.Id)));
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id);
        var viagens = await app.Frota().ViagensAsync(empresa, Ext.Hoje.AddDays(-90), Ext.Hoje.AddDays(30), sessao.Id);
        var itens = await app.Frota().ManifestosAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        if (!EhAtual(g)) return;
        ViagensOpcoes.Clear();
        ViagensOpcoes.Add(new Opcao(null, "(nenhuma)"));
        foreach (var v in viagens.Where(v => v.Situacao != "CANCELADA")) ViagensOpcoes.Add(new Opcao(v.Id, $"Nº {v.Numero} – {v.Origem} → {v.Destino}"));
        Lista.Clear();
        foreach (var m in itens) Lista.Add(new ManifestoLinha(m));
        Totais = $"{itens.Count} manifesto(s) · {itens.Count(m => m.Situacao == "ABERTO")} aberto(s) · {itens.Sum(m => m.Ctes.Count)} CT-e";
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Motorista ??= MotoristasOpcoes.FirstOrDefault();
        Viagem ??= ViagensOpcoes.FirstOrDefault();
        if (Selecionado is null) await CarregarCtesAsync(null);
    }

    [RelayCommand]
    private async Task NovoAsync()
    {
        Selecionado = null;
        (Emissao, UfsPercurso, Chave, Observacao) = (DateTime.Today, "", "", "");
        (Veiculo, Motorista, Viagem) = (VeiculosOpcoes.FirstOrDefault(), MotoristasOpcoes.FirstOrDefault(), ViagensOpcoes.FirstOrDefault());
        await CarregarCtesAsync(null);
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var m = new Manifesto(Selecionado?.M.Id ?? 0, Ext.Int(Serie) ?? 1, Selecionado?.M.Numero ?? 0, Ext.Dia(Emissao), UfInicio ?? "", UfFim ?? "", UfsPercurso, Veiculo?.Id ?? 0,
                              Motorista?.Id ?? 0, Viagem?.Id, Chave, "ABERTO", null, Observacao, [.. Ctes.Where(c => c.Marcado).Select(c => c.C.Id)]);
        var r = await app.Frota().GravarManifestoAsync(empresa, m, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.M.Id == r.Valor.Id);
        Mensagem = $"Manifesto {r.Valor.Serie}-{r.Valor.Numero} gravado com {m.Ctes.Count} CT-e (em trânsito).";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task EncerrarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione o manifesto."); return; }
        var r = await app.Frota().EncerrarManifestoAsync(empresa, m.Id, Ext.Dia(Encerramento), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"Manifesto {m.Serie}-{m.Numero} encerrado.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione o manifesto."); return; }
        if (!await Contexto.ConfirmarAsync("Cancelar manifesto", $"Cancelar o manifesto {m.Serie}-{m.Numero}? Os CT-e sem ocorrência voltam a \"emitido\".")) return;
        var r = await app.Frota().CancelarManifestoAsync(empresa, m.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await NovoAsync();
        await CarregarAsync();
        Mensagem = $"Manifesto {m.Serie}-{m.Numero} cancelado.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task ImprimirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione o manifesto."); return; }
        var r = await app.Frota().ManifestoPdfAsync(empresa, m.Id, Contexto.DescricaoEmpresa, Contexto.Sessao?.Nome ?? "", usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        SalvarPdf($"manifesto_{m.Serie}_{m.Numero}.pdf", () => r.Valor, "Manifesto de carga");
    });
}

// ---------------------------------------------------------------------------------------------- entregas e ocorrências

public sealed record OcorrenciaLinha(Ocorrencia O)
{
    public string DataHora => O.DataHora.ToString("dd/MM/yyyy HH:mm");
    public string Tipo => RegrasTms.Rotulo(O.Tipo);
    public string Descricao => O.Descricao ?? "";
    public string Recebedor => O.Recebedor ?? "";
    public string Usuario => O.Usuario ?? "";
}

/// <summary>Operações › Entregas e ocorrências: CT-e em trânsito com o prazo (atrasados primeiro) e o registro das ocorrências até a entrega.</summary>
public partial class EntregasViewModel(IContexto contexto) : TmsBase(contexto)
{
    public ObservableCollection<CteLinha> Lista { get; } = [];
    public ObservableCollection<OcorrenciaLinha> Ocorrencias { get; } = [];
    public static IReadOnlyList<OpcaoTms> Tipos { get; } = OpcaoTms.De(RegrasTms.TiposOcorrencia);
    [ObservableProperty] private CteLinha? _selecionado;
    [ObservableProperty] private OpcaoTms? _tipo = Tipos[0];
    [ObservableProperty] private DateTime? _dia = DateTime.Today;
    [ObservableProperty] private string _hora = DateTime.Now.ToString("HH:mm");
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _recebedor = "";
    [ObservableProperty] private string _detalhe = "";

    partial void OnSelecionadoChanged(CteLinha? value)
    {
        Detalhe = value?.C is { } c ? $"CT-e {c.Serie}-{c.Numero} · {c.Remetente} → {c.Destinatario} · {c.Trecho} · previsão {Ext.Data(c.PrevisaoEntrega)} · {RegrasTms.Rotulo(c.Situacao)}" : "";
        _ = CarregarOcorrenciasAsync();
    }

    private async Task CarregarOcorrenciasAsync()
    {
        Ocorrencias.Clear();
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa || Selecionado?.C is not { } c) return;
        foreach (var o in await app.Frota().OcorrenciasAsync(empresa, c.Id, sessao.Id)) Ocorrencias.Add(new OcorrenciaLinha(o));
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().CtesEmAbertoAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var anterior = Selecionado?.C.Id;
        Lista.Clear();
        foreach (var c in itens.OrderBy(c => RegrasTms.SituacaoPrazo(c.PrevisaoEntrega, c.Situacao, c.EntregueEm, Ext.Hoje) == "Atrasado" ? 0 : 1)) Lista.Add(new CteLinha(c));
        Totais = $"{itens.Count} CT-e a entregar · {Lista.Count(l => l.Prazo == "Atrasado")} atrasado(s) · {itens.Count(c => c.Situacao == "EM_TRANSITO")} em trânsito";
        Selecionado = Lista.FirstOrDefault(l => l.C.Id == anterior);
    }

    [RelayCommand]
    private Task RegistrarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.C is not { } c) { Erros.Add("Selecione o CT-e."); return; }
        if (!TimeOnly.TryParse(Hora, out var hora)) { Erros.Add("Informe a hora no formato 14:30."); return; }
        var r = await app.Frota().RegistrarOcorrenciaAsync(empresa, c.Id, Tipo?.Valor ?? "", Ext.Dia(Dia).ToDateTime(hora), Descricao, Recebedor, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (Descricao, Recebedor) = ("", "");
        await CarregarAsync();
        if (Selecionado is null) Ocorrencias.Clear();
        Mensagem = r.Valor == "ENTREGUE" ? $"Entrega do CT-e {c.Serie}-{c.Numero} registrada." : $"Ocorrência registrada: CT-e {RegrasTms.Rotulo(r.Valor).ToLowerInvariant()}.";
    }, Permissao.FrotaTms);
}

// ---------------------------------------------------------------------------------------------- faturas de frete

public sealed record FaturaLinha(FaturaFrete F)
{
    public int Numero => F.Numero;
    public string Emissao => Ext.Data(F.Emissao);
    public string Vencimento => Ext.Data(F.Vencimento);
    public string Cliente => F.Cliente ?? "";
    public int Ctes => F.Ctes;
    public string Valor => Formato.Moeda(F.ValorTotal);
    public string Saldo => F.Situacao == "ABERTA" ? Formato.Moeda(F.Saldo ?? 0) : "";
    public string Situacao => F.Situacao == "CANCELADA" ? "Cancelada" : (F.Saldo ?? 0) == 0 ? "Recebida" : F.Vencimento < Ext.Hoje ? "Vencida" : "A receber";
}

/// <summary>Emissão › Faturas de frete: reúne os CT-e emitidos do mesmo cliente (tomador) e gera a conta a receber.</summary>
public partial class FaturasFreteViewModel(IContexto contexto) : TmsBase(contexto)
{
    public ObservableCollection<FaturaLinha> Lista { get; } = [];
    public ObservableCollection<CteMarcado> Ctes { get; } = [];
    [ObservableProperty] private FaturaLinha? _selecionada;
    [ObservableProperty] private Opcao? _cliente;
    [ObservableProperty] private DateTime? _emissao = DateTime.Today;
    [ObservableProperty] private DateTime? _vencimento = DateTime.Today.AddDays(30);
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _totalMarcado = "";

    protected override void AoMudarPeriodo() => _ = CarregarAsync();
    partial void OnClienteChanged(Opcao? value) => _ = CarregarCtesAsync();

    private async Task CarregarCtesAsync()
    {
        foreach (var c in Ctes) c.PropertyChanged -= Recalcular;
        Ctes.Clear();
        TotalMarcado = "";
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa || Cliente?.Id is not { } cliente) return;
        foreach (var c in await app.Frota().CtesAFaturarAsync(empresa, cliente, sessao.Id))
        {
            var m = new CteMarcado(c, true);
            m.PropertyChanged += Recalcular;
            Ctes.Add(m);
        }
        Recalcular(null, EventArgs.Empty);
    }

    private void Recalcular(object? s, EventArgs e)
    {
        var marcados = Ctes.Where(c => c.Marcado).ToList();
        TotalMarcado = Ctes.Count == 0 ? "Nenhum CT-e emitido deste cliente sem fatura." : $"{marcados.Count} de {Ctes.Count} CT-e marcados · total {M(marcados.Sum(c => c.C.ValorTotal))}";
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarParticipantesAsync(app, empresa, sessao.Id);
        var itens = await app.Frota().FaturasAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var f in itens) Lista.Add(new FaturaLinha(f));
        var abertas = itens.Where(f => f.Situacao == "ABERTA").ToList();
        Totais = $"{abertas.Count} fatura(s) · faturado {M(abertas.Sum(f => f.ValorTotal))} · a receber {M(abertas.Sum(f => f.Saldo ?? 0))}";
        Cliente ??= ParticipantesOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Cliente?.Id is not { } cliente) { Erros.Add("Escolha o cliente (tomador)."); return; }
        var marcados = Ctes.Where(c => c.Marcado).Select(c => c.C.Id).ToList();
        var r = await app.Frota().GerarFaturaAsync(empresa, cliente, marcados, Ext.Dia(Emissao), Ext.Dia(Vencimento), Observacao, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Observacao = "";
        await CarregarAsync();
        await CarregarCtesAsync();
        Selecionada = Lista.FirstOrDefault(l => l.F.Id == r.Valor.Id);
        Mensagem = $"Fatura nº {r.Valor.Numero} gerada ({M(r.Valor.ValorTotal)}); a conta a receber está em Financeiro › Contas a receber.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.F is not { } f) { Erros.Add("Selecione a fatura."); return; }
        if (!await Contexto.ConfirmarAsync("Cancelar fatura", $"Cancelar a fatura nº {f.Numero}? A conta a receber é apagada e os CT-e voltam a ficar sem fatura.")) return;
        var r = await app.Frota().CancelarFaturaAsync(empresa, f.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        await CarregarCtesAsync();
        Mensagem = $"Fatura nº {f.Numero} cancelada.";
    }, Permissao.FrotaTms);

    [RelayCommand]
    private Task ImprimirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.F is not { } f) { Erros.Add("Selecione a fatura."); return; }
        var r = await app.Frota().FaturaPdfAsync(empresa, f, Contexto.DescricaoEmpresa, Contexto.Sessao?.Nome ?? "", usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        SalvarPdf($"fatura_frete_{f.Numero}.pdf", () => r.Valor, "Fatura de frete");
    });
}
