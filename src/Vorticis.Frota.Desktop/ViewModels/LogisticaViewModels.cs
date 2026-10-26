using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Frota.Desktop.ViewModels;

/// <summary>Opção de filtro com texto (ex.: "(todas)") e valor opcional.</summary>
public sealed record OpcaoFiltro(string? Valor, string Texto)
{
    public override string ToString() => Texto;
}

// ---------------------------------------------------------------------------------------------- frotas

public sealed record FrotaLinha(FrotaCadastro F)
{
    public int Codigo => F.Codigo;
    public string Descricao => F.Descricao;
    public string Tipo => RegrasFrota.Rotulo(F.Tipo);
    public string Responsavel => F.Responsavel ?? "";
    public string CentroCusto => F.CentroCustoNome ?? "";
    public int Veiculos => F.Veiculos;
    public string Situacao => F.Ativa ? "Ativa" : "Inativa";
}

/// <summary>Cadastros › Frotas: própria, agregada ou de terceiros; o vínculo dos veículos é feito em Cadastros › Veículos (campo Frota).</summary>
public partial class FrotasViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaCadastros)
{
    public ObservableCollection<FrotaLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> CentrosOpcoes { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Tipos { get; } = OpcaoCodigo.De(RegrasLogistica.TiposFrota);
    [ObservableProperty] private FrotaLinha? _selecionada;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private OpcaoCodigo? _tipo = Tipos[0];
    [ObservableProperty] private string _responsavel = "";
    [ObservableProperty] private Opcao? _centroCusto;
    [ObservableProperty] private bool _ativa = true;

    partial void OnSelecionadaChanged(FrotaLinha? value)
    {
        if (value?.F is not { } f) return;
        (Codigo, Descricao, Responsavel, Ativa) = (f.Codigo.ToString(), f.Descricao, f.Responsavel ?? "", f.Ativa);
        Tipo = Tipos.FirstOrDefault(t => t.Valor == f.Tipo);
        CentroCusto = CentrosOpcoes.FirstOrDefault(c => c.Id == f.CentroCustoId) ?? CentrosOpcoes.FirstOrDefault();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().FrotasAsync(empresa, sessao.Id);
        var centros = await app.Frota().CentrosCustoAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        CentrosOpcoes.Clear();
        CentrosOpcoes.Add(new Opcao(null, "(nenhum)"));
        foreach (var c in centros) CentrosOpcoes.Add(new Opcao(c.Id, c.Texto));
        Lista.Clear();
        foreach (var f in itens) Lista.Add(new FrotaLinha(f));
        if (Selecionada is null && string.IsNullOrEmpty(Codigo)) Nova();
        CentroCusto ??= CentrosOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        Codigo = ((Lista.Count == 0 ? 0 : Lista.Max(l => l.F.Codigo)) + 1).ToString();
        (Descricao, Responsavel, Ativa, Tipo, CentroCusto) = ("", "", true, Tipos[0], CentrosOpcoes.FirstOrDefault());
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var f = new FrotaCadastro(Selecionada?.F.Id ?? 0, Ext.Int(Codigo) ?? 0, Descricao, Tipo?.Valor ?? "", Responsavel, CentroCusto?.Id, Ativa);
        var r = await app.Frota().GravarFrotaAsync(empresa, f, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.F.Id == r.Valor);
        Mensagem = "Frota gravada.";
    }, Permissao.FrotaCadastros);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.F is not { } f) { Erros.Add("Selecione a frota."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir frota", $"Excluir a frota {f.Descricao}? Os veículos e centros de custo vinculados ficam sem frota.")) return;
        var r = await app.Frota().ExcluirFrotaAsync(empresa, f.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionada = null;
        await CarregarAsync();
        Nova();
        Mensagem = "Frota excluída.";
    }, Permissao.FrotaCadastros);
}

// ---------------------------------------------------------------------------------------------- rotas

public sealed record RotaLinha(Rota R)
{
    public int Codigo => R.Codigo;
    public string Descricao => R.Descricao;
    public string Trajeto => R.Trajeto;
    public string Distancia => R.DistanciaKm.ToString("N1", Formato.PtBr) + " km";
    public string Tempo => R.TempoHoras.ToString("N1", Formato.PtBr) + " h";
    public string Pedagios => Formato.Moeda(R.Pedagios);
    public string Situacao => R.Ativa ? "Ativa" : "Inativa";
}

public sealed record ParadaLinha(int Ordem, string Papel, ParadaRota P)
{
    public string Texto => $"{Ordem + 1}. {Papel}: {P.Municipio}/{P.Uf}";
}

/// <summary>
/// Cadastros › Rotas: origem, paradas intermediárias e destino (UF + município do IBGE). A distância é estimada em linha reta (Haversine) vezes o fator
/// rodoviário (Parâmetros › Frota), pode ser buscada num serviço de rotas na internet ou digitada.
/// </summary>
public partial class RotasViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaCadastros)
{
    private readonly List<ParadaRota> _paradas = [];
    private ConfiguracaoFrota _config = new();
    private bool _interno;

    public ObservableCollection<RotaLinha> Lista { get; } = [];
    public ObservableCollection<ParadaLinha> Paradas { get; } = [];
    public ObservableCollection<Municipio> MunicipiosUf { get; } = [];
    public static IReadOnlyList<string> Ufs { get; } = [.. Vorticis.Domain.Cadastros.Uf.Todas.Order()];
    [ObservableProperty] private RotaLinha? _selecionada;
    [ObservableProperty] private ParadaLinha? _paradaSelecionada;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string? _uf = "SP";
    [ObservableProperty] private Municipio? _municipio;
    [ObservableProperty] private string _distanciaKm = "";
    [ObservableProperty] private string _tempoHoras = "";
    [ObservableProperty] private string _pedagios = "";
    [ObservableProperty] private bool _ativa = true;
    [ObservableProperty] private string _origemDistancia = "ESTIMADA";
    [ObservableProperty] private string _resumoDistancia = "";

    partial void OnUfChanged(string? value)
    {
        MunicipiosUf.Clear();
        foreach (var m in BaseMunicipios.DaUf(value).OrderBy(m => m.Nome, StringComparer.CurrentCultureIgnoreCase)) MunicipiosUf.Add(m);
        Municipio = null;
    }

    /// <summary>Digitar a distância passa a rota para "informada manualmente" (não é mais recalculada ao mudar as paradas).</summary>
    partial void OnDistanciaKmChanged(string value)
    {
        if (!_interno) { OrigemDistancia = "MANUAL"; AtualizarResumo(); }
    }

    partial void OnSelecionadaChanged(RotaLinha? value)
    {
        if (value?.R is not { } r) return;
        _interno = true;
        (Codigo, Descricao, Ativa, Pedagios, OrigemDistancia) = (r.Codigo.ToString(), r.Descricao, r.Ativa, Ext.Num(r.Pedagios), r.DistanciaOrigem);
        (DistanciaKm, TempoHoras) = (Ext.Num(r.DistanciaKm), Ext.Num(r.TempoHoras));
        _paradas.Clear();
        _paradas.AddRange(r.Paradas);
        _interno = false;
        RefazerParadas();
        AtualizarResumo();
    }

    private void RefazerParadas()
    {
        Paradas.Clear();
        for (var i = 0; i < _paradas.Count; i++)
            Paradas.Add(new ParadaLinha(i, i == 0 ? "Origem" : i == _paradas.Count - 1 ? "Destino" : "Parada", _paradas[i]));
    }

    private void AtualizarResumo() => ResumoDistancia = OrigemDistancia switch
    {
        "ESTIMADA" => string.Create(Formato.PtBr, $"Estimada: distância em linha reta entre as cidades × fator rodoviário {_config.FatorRodoviario:0.00} (Parâmetros › Frota)."),
        "RODOVIARIA" => "Distância rodoviária obtida no serviço de rotas (OpenStreetMap).",
        _ => "Distância informada por você.",
    };

    private void Recalcular()
    {
        if (OrigemDistancia != "ESTIMADA") return;
        _interno = true;
        var km = RegrasLogistica.EstimarKm(_paradas, _config.FatorRodoviario);
        DistanciaKm = km is { } k ? Ext.Num(k) : "";
        TempoHoras = km is { } k2 ? Ext.Num(RegrasLogistica.EstimarHoras(k2, _config.VelocidadeMedia)) : "";
        _interno = false;
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().RotasAsync(empresa, sessao.Id);
        var cfg = await app.Frota().ConfiguracaoAsync(empresa);
        if (!EhAtual(g)) return;
        _config = cfg;
        Lista.Clear();
        foreach (var r in itens) Lista.Add(new RotaLinha(r));
        if (MunicipiosUf.Count == 0) OnUfChanged(Uf);
        if (Selecionada is null && string.IsNullOrEmpty(Codigo)) Nova();
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        Codigo = ((Lista.Count == 0 ? 0 : Lista.Max(l => l.R.Codigo)) + 1).ToString();
        _interno = true;
        (Descricao, Ativa, Pedagios, OrigemDistancia, DistanciaKm, TempoHoras) = ("", true, "", "ESTIMADA", "", "");
        _paradas.Clear();
        _interno = false;
        RefazerParadas();
        AtualizarResumo();
    }

    [RelayCommand]
    private void AdicionarParada()
    {
        if (Municipio is not { } m) { Erros.Clear(); Erros.Add("Escolha a UF e o município."); return; }
        Erros.Clear();
        _paradas.Add(new ParadaRota(m.Uf, m.Ibge, m.Nome));
        RefazerParadas();
        Recalcular();
    }

    [RelayCommand]
    private void RemoverParada()
    {
        if (ParadaSelecionada is not { } p) return;
        _paradas.RemoveAt(p.Ordem);
        RefazerParadas();
        Recalcular();
    }

    [RelayCommand]
    private void SubirParada() => Mover(-1);

    [RelayCommand]
    private void DescerParada() => Mover(1);

    private void Mover(int delta)
    {
        if (ParadaSelecionada is not { } p || p.Ordem + delta is var n && (n < 0 || n >= _paradas.Count)) return;
        (_paradas[p.Ordem], _paradas[n]) = (_paradas[n], _paradas[p.Ordem]);
        RefazerParadas();
        ParadaSelecionada = Paradas[n];
        Recalcular();
    }

    /// <summary>Volta para a distância estimada (linha reta × fator) e recalcula.</summary>
    [RelayCommand]
    private void RecalcularEstimativa()
    {
        OrigemDistancia = "ESTIMADA";
        Recalcular();
        AtualizarResumo();
    }

    [RelayCommand]
    private Task BuscarRodoviariaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Frota().BuscarDistanciaRodoviariaAsync(empresa, _paradas, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }   // sem internet: mantém a estimativa que já está na tela
        _interno = true;
        (DistanciaKm, TempoHoras, OrigemDistancia) = (Ext.Num(r.Valor.Km), Ext.Num(r.Valor.Horas), "RODOVIARIA");
        _interno = false;
        AtualizarResumo();
        Mensagem = $"Distância rodoviária: {r.Valor.Km.ToString("N1", Formato.PtBr)} km (estimativa em linha reta substituída).";
    }, Permissao.FrotaCadastros);

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var rota = new Rota(Selecionada?.R.Id ?? 0, Ext.Int(Codigo) ?? 0, Descricao, Formato.LerValor(DistanciaKm) ?? 0, OrigemDistancia, Formato.LerValor(TempoHoras) ?? 0,
                            Formato.LerValor(Pedagios) ?? 0, Ativa, [.. _paradas]);
        var r = await app.Frota().GravarRotaAsync(empresa, rota, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.R.Id == r.Valor);
        Mensagem = "Rota gravada.";
    }, Permissao.FrotaCadastros);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.R is not { } rota) { Erros.Add("Selecione a rota."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir rota", $"Excluir a rota {rota.Descricao}?")) return;
        var r = await app.Frota().ExcluirRotaAsync(empresa, rota.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionada = null;
        await CarregarAsync();
        Nova();
        Mensagem = "Rota excluída.";
    }, Permissao.FrotaCadastros);
}

// ---------------------------------------------------------------------------------------------- diárias

public sealed record DiariaLinha(Diaria D)
{
    public int Numero => D.Numero;
    public string Data => Ext.Data(D.Data);
    public string Colaborador => D.Colaborador ?? "";
    public string Funcao => RegrasFrota.Rotulo(D.Funcao);
    public string Diaria => $"{D.Quantidade:0.##} × {RegrasFrota.Rotulo(D.Tipo)}";
    public string Placa => RegrasFrota.FormatarPlaca(D.Placa ?? "");
    public string Frota => D.FrotaNome ?? "";
    public string Total => Formato.Moeda(D.Total);
    public string Adiantamento => D.Adiantamento > 0 ? Formato.Moeda(D.Adiantamento) : "";
    public string Saldo => D.Saldo is { } s ? (s > 0 ? $"{Formato.Moeda(s)} a devolver" : s < 0 ? $"{Formato.Moeda(-s)} a pagar" : "zerado") : "";
    public string Situacao => RegrasFrota.Rotulo(D.Situacao);
}

/// <summary>
/// Operação › Diárias: diárias de motoristas e ajudantes por viagem ou período, com adiantamento, pagamento (conta a pagar no Financeiro e pendência de
/// contabilização, se a contabilização for em lote) e acerto (prestação de contas).
/// </summary>
public partial class DiariasViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    private Dictionary<string, decimal> _valores = [];

    public ObservableCollection<DiariaLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> FrotasOpcoes { get; } = [];
    public ObservableCollection<Opcao> RotasOpcoes { get; } = [];
    public ObservableCollection<Opcao> ViagensOpcoes { get; } = [];
    public ObservableCollection<Opcao> CentrosOpcoes { get; } = [];
    public ObservableCollection<Opcao> FiltroColaboradores { get; } = [];
    public ObservableCollection<Opcao> FiltroFrotas { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Tipos { get; } = OpcaoCodigo.De(RegrasLogistica.TiposDiaria);
    public static IReadOnlyList<OpcaoCodigo> Funcoes { get; } = OpcaoCodigo.De(RegrasLogistica.Funcoes);
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private Opcao? _filtroColaborador;
    [ObservableProperty] private Opcao? _filtroFrota;
    [ObservableProperty] private DiariaLinha? _selecionada;
    [ObservableProperty] private DateTime? _data = DateTime.Today;
    [ObservableProperty] private Opcao? _colaborador;
    [ObservableProperty] private OpcaoCodigo? _funcao = Funcoes[0];
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _frota;
    [ObservableProperty] private Opcao? _rota;
    [ObservableProperty] private Opcao? _viagem;
    [ObservableProperty] private Opcao? _centro;
    [ObservableProperty] private OpcaoCodigo? _tipo = Tipos[0];
    [ObservableProperty] private string _quantidade = "1";
    [ObservableProperty] private string _valorUnitario = "";
    [ObservableProperty] private string _adiantamento = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private DateTime? _vencimento = DateTime.Today;
    [ObservableProperty] private string _valorGasto = "";
    [ObservableProperty] private string _total = "";
    [ObservableProperty] private string _totais = "";

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFiltroColaboradorChanged(Opcao? value) => _ = CarregarAsync();
    partial void OnFiltroFrotaChanged(Opcao? value) => _ = CarregarAsync();
    partial void OnQuantidadeChanged(string value) => AtualizarTotal();
    partial void OnValorUnitarioChanged(string value) => AtualizarTotal();

    /// <summary>Tipo novo traz o valor da tabela (Parâmetros › Frota), que ainda pode ser alterado à mão.</summary>
    partial void OnTipoChanged(OpcaoCodigo? value)
    {
        if (Selecionada is null && value is not null && _valores.GetValueOrDefault(value.Valor) is > 0 and var v) ValorUnitario = Ext.Num(v);
    }

    private void AtualizarTotal() => Total = Formato.Moeda(RegrasLogistica.TotalDiaria(Formato.LerValor(Quantidade) ?? 0, Formato.LerValor(ValorUnitario) ?? 0));

    partial void OnSelecionadaChanged(DiariaLinha? value)
    {
        if (value?.D is not { } d) return;
        Data = Ext.Dt(d.Data);
        Colaborador = Escolher(MotoristasOpcoes, d.MotoristaId, () => d.Colaborador ?? "");
        Veiculo = Escolher(VeiculosOpcoes, d.VeiculoId);
        Frota = Escolher(FrotasOpcoes, d.FrotaId);
        Rota = Escolher(RotasOpcoes, d.RotaId);
        Viagem = Escolher(ViagensOpcoes, d.ViagemId);
        Centro = Escolher(CentrosOpcoes, d.CentroCustoId);
        Funcao = Funcoes.FirstOrDefault(f => f.Valor == d.Funcao);
        Tipo = Tipos.FirstOrDefault(t => t.Valor == d.Tipo);
        (Quantidade, ValorUnitario, Adiantamento, Observacao) = (Ext.Num(d.Quantidade), Ext.Num(d.ValorUnitario), Ext.Num(d.Adiantamento), d.Observacao ?? "");
        ValorGasto = Ext.Num(d.ValorGasto);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, "(nenhum)");
        var frotas = await app.Frota().FrotasAsync(empresa, sessao.Id);
        var rotas = await app.Frota().RotasAsync(empresa, sessao.Id);
        var centros = await app.Frota().CentrosCustoAsync(empresa, sessao.Id);
        var hoje = Ext.Hoje;
        var viagens = await app.Frota().ViagensAsync(empresa, hoje.AddDays(-90), hoje, sessao.Id);
        _valores = await app.Frota().ValoresDiariaAsync(empresa);
        var filtroM = FiltroColaborador?.Id;
        var filtroF = FiltroFrota?.Id;
        var itens = await app.Frota().DiariasAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), filtroM, filtroF, sessao.Id);
        if (!EhAtual(g)) return;
        FrotasOpcoes.Clear(); FrotasOpcoes.Add(new Opcao(null, "(automática)"));
        foreach (var f in frotas.Where(f => f.Ativa)) FrotasOpcoes.Add(new Opcao(f.Id, $"{f.Codigo} – {f.Descricao}"));
        RotasOpcoes.Clear(); RotasOpcoes.Add(new Opcao(null, "(nenhuma)"));
        foreach (var r in rotas.Where(r => r.Ativa)) RotasOpcoes.Add(new Opcao(r.Id, $"{r.Codigo} – {r.Descricao}"));
        ViagensOpcoes.Clear(); ViagensOpcoes.Add(new Opcao(null, "(nenhuma)"));
        foreach (var v in viagens.Where(v => v.Situacao != "CANCELADA")) ViagensOpcoes.Add(new Opcao(v.Id, $"Nº {v.Numero} – {v.Origem} → {v.Destino}"));
        CentrosOpcoes.Clear(); CentrosOpcoes.Add(new Opcao(null, "(automático)"));
        foreach (var c in centros) CentrosOpcoes.Add(new Opcao(c.Id, c.Texto));
        FiltroColaboradores.Clear(); FiltroColaboradores.Add(new Opcao(null, "(todos)"));
        foreach (var m in MotoristasOpcoes.Where(m => m.Id is not null)) FiltroColaboradores.Add(m);
        FiltroFrotas.Clear(); FiltroFrotas.Add(new Opcao(null, "(todas)"));
        foreach (var f in FrotasOpcoes.Where(f => f.Id is not null)) FiltroFrotas.Add(f);
        (FiltroColaborador, FiltroFrota) = (FiltroColaboradores.FirstOrDefault(o => o.Id == filtroM) ?? FiltroColaboradores[0], FiltroFrotas.FirstOrDefault(o => o.Id == filtroF) ?? FiltroFrotas[0]);
        Lista.Clear();
        foreach (var d in itens) Lista.Add(new DiariaLinha(d));
        Totais = $"{itens.Count} diária(s) · total {Formato.Moeda(itens.Sum(d => d.Total))} · adiantamentos {Formato.Moeda(itens.Sum(d => d.Adiantamento))}";
        Colaborador ??= MotoristasOpcoes.FirstOrDefault();
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Frota ??= FrotasOpcoes.FirstOrDefault();
        Rota ??= RotasOpcoes.FirstOrDefault();
        Viagem ??= ViagensOpcoes.FirstOrDefault();
        Centro ??= CentrosOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Data, Funcao, Tipo, Quantidade, Adiantamento, Observacao, ValorGasto) = (DateTime.Today, Funcoes[0], Tipos[0], "1", "", "", "");
        ValorUnitario = _valores.GetValueOrDefault(Tipos[0].Valor) is > 0 and var v ? Ext.Num(v) : "";
        (Colaborador, Veiculo, Frota, Rota, Viagem, Centro) = (MotoristasOpcoes.FirstOrDefault(), VeiculosOpcoes.FirstOrDefault(), FrotasOpcoes.FirstOrDefault(),
                                                              RotasOpcoes.FirstOrDefault(), ViagensOpcoes.FirstOrDefault(), CentrosOpcoes.FirstOrDefault());
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var d = new Diaria(Selecionada?.D.Id ?? 0, Selecionada?.D.Numero ?? 0, Ext.Dia(Data), Colaborador?.Id ?? 0, Funcao?.Valor ?? "", Veiculo?.Id, Frota?.Id, Viagem?.Id, Rota?.Id,
                           Tipo?.Valor ?? "", Formato.LerValor(Quantidade) ?? 0, Formato.LerValor(ValorUnitario) ?? 0, Formato.LerValor(Adiantamento) ?? 0, null, "LANCADA",
                           Centro?.Id, null, Observacao);
        var r = await app.Frota().GravarDiariaAsync(empresa, d, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.D.Numero == r.Valor);
        Mensagem = $"Diária nº {r.Valor} gravada.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task PagarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.D is not { } d) { Erros.Add("Selecione a diária."); return; }
        var r = await app.Frota().PagarDiariaAsync(empresa, d.Id, Ext.Dia(Vencimento), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var n = d.Numero;
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.D.Numero == n);
        Mensagem = $"Diária nº {n} paga: conta a pagar de R$ {Formato.Moeda(r.Valor.Valor)} gerada no Financeiro"
                   + (r.Valor.LancamentoId is not null ? " e lançamento contábil gerado." : r.Valor.PendenciaRegistrada ? " e pendência de contabilização registrada (Contabilização em lote)." : ".")
                   + (r.Valor.Aviso is { } aviso ? " " + aviso : "");
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task AcertarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.D is not { } d) { Erros.Add("Selecione a diária."); return; }
        if (Formato.LerValor(ValorGasto) is not { } gasto) { Erros.Add("Informe o valor gasto."); return; }
        var r = await app.Frota().AcertarDiariaAsync(empresa, d.Id, gasto, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var n = d.Numero;
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.D.Numero == n);
        Mensagem = r.Valor > 0 ? $"Diária nº {n} acertada: o colaborador devolve R$ {Formato.Moeda(r.Valor)}."
            : r.Valor < 0 ? $"Diária nº {n} acertada: a empresa paga R$ {Formato.Moeda(-r.Valor)} ao colaborador." : $"Diária nº {n} acertada: sem saldo.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.D is not { } d) { Erros.Add("Selecione a diária."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir diária", $"Excluir a diária nº {d.Numero}?")) return;
        var r = await app.Frota().ExcluirDiariaAsync(empresa, d.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Diária excluída.";
    }, Permissao.FrotaOperacao);
}

// ---------------------------------------------------------------------------------------------- coletas

public sealed record ColetaLinha(Coleta C)
{
    public int Numero => C.Numero;
    public string Agendada => C.AgendadaEm.ToString("dd/MM/yyyy HH:mm");
    public string Remetente => C.Remetente ?? "";
    public string Destinatario => C.Destinatario ?? "";
    public string Endereco => C.EnderecoColeta;
    public string Mercadoria => C.Mercadoria ?? "";
    public string Peso => C.PesoKg.ToString("N1", Formato.PtBr);
    public string Placa => RegrasFrota.FormatarPlaca(C.Placa ?? "");
    public string Motorista => C.MotoristaNome ?? "";
    public string Situacao => RegrasFrota.Rotulo(C.Situacao);
}

/// <summary>Operação › Coletas › Ordens de coleta: agendamento, andamento (agendada, em coleta, coletada, cancelada) e impressão da ordem em PDF.</summary>
public partial class ColetasViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    public ObservableCollection<ColetaLinha> Lista { get; } = [];
    public ObservableCollection<Opcao> ParticipantesOpcoes { get; } = [];
    public ObservableCollection<Opcao> RotasOpcoes { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Situacoes { get; } = OpcaoCodigo.De(RegrasLogistica.SituacoesColeta);
    public static IReadOnlyList<OpcaoFiltro> FiltroSituacoes { get; } =
        [new(null, "(todas)"), .. RegrasLogistica.SituacoesColeta.Select(s => new OpcaoFiltro(s, RegrasFrota.Rotulo(s)))];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private OpcaoFiltro? _filtroSituacao = FiltroSituacoes[0];
    [ObservableProperty] private ColetaLinha? _selecionada;
    [ObservableProperty] private DateTime? _dia = DateTime.Today;
    [ObservableProperty] private string _hora = "08:00";
    [ObservableProperty] private Opcao? _remetente;
    [ObservableProperty] private Opcao? _destinatario;
    [ObservableProperty] private string _endereco = "";
    [ObservableProperty] private string _mercadoria = "";
    [ObservableProperty] private string _volumes = "";
    [ObservableProperty] private string _peso = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private Opcao? _rota;
    [ObservableProperty] private OpcaoCodigo? _situacao = Situacoes[0];
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _totais = "";

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFiltroSituacaoChanged(OpcaoFiltro? value) => _ = CarregarAsync();

    partial void OnSelecionadaChanged(ColetaLinha? value)
    {
        if (value?.C is not { } c) return;
        (Dia, Hora) = (c.AgendadaEm.Date, c.AgendadaEm.ToString("HH:mm"));
        Remetente = Escolher(ParticipantesOpcoes, c.RemetenteId);
        Destinatario = Escolher(ParticipantesOpcoes, c.DestinatarioId);
        Veiculo = Escolher(VeiculosOpcoes, c.VeiculoId);
        Motorista = Escolher(MotoristasOpcoes, c.MotoristaId);
        Rota = Escolher(RotasOpcoes, c.RotaId);
        (Endereco, Mercadoria, Volumes, Peso, Valor, Observacao) = (c.EnderecoColeta, c.Mercadoria ?? "", c.Volumes.ToString(), Ext.Num(c.PesoKg), Ext.Num(c.ValorMercadoria), c.Observacao ?? "");
        Situacao = Situacoes.FirstOrDefault(s => s.Valor == c.Situacao);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, "(nenhum)");
        var participantes = await app.Frota().ParticipantesAsync(empresa, sessao.Id);
        var rotas = await app.Frota().RotasAsync(empresa, sessao.Id);
        var itens = await app.Frota().ColetasAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), FiltroSituacao?.Valor, sessao.Id);
        if (!EhAtual(g)) return;
        ParticipantesOpcoes.Clear(); ParticipantesOpcoes.Add(new Opcao(null, "(nenhum)"));
        foreach (var p in participantes) ParticipantesOpcoes.Add(new Opcao(p.Id, p.ToString()));
        RotasOpcoes.Clear(); RotasOpcoes.Add(new Opcao(null, "(nenhuma)"));
        foreach (var r in rotas.Where(r => r.Ativa)) RotasOpcoes.Add(new Opcao(r.Id, $"{r.Codigo} – {r.Descricao}"));
        Lista.Clear();
        foreach (var c in itens) Lista.Add(new ColetaLinha(c));
        Totais = $"{itens.Count} ordem(ns) · {itens.Count(c => c.Situacao == "AGENDADA")} agendada(s) · {itens.Count(c => c.Situacao == "EM_COLETA")} em coleta · {itens.Count(c => c.Situacao == "COLETADA")} coletada(s)";
        Remetente ??= ParticipantesOpcoes.FirstOrDefault();
        Destinatario ??= ParticipantesOpcoes.FirstOrDefault();
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Motorista ??= MotoristasOpcoes.FirstOrDefault();
        Rota ??= RotasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Dia, Hora, Endereco, Mercadoria, Volumes, Peso, Valor, Observacao, Situacao) = (DateTime.Today, "08:00", "", "", "", "", "", "", Situacoes[0]);
        (Remetente, Destinatario, Veiculo, Motorista, Rota) = (ParticipantesOpcoes.FirstOrDefault(), ParticipantesOpcoes.FirstOrDefault(), VeiculosOpcoes.FirstOrDefault(),
                                                              MotoristasOpcoes.FirstOrDefault(), RotasOpcoes.FirstOrDefault());
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (!TimeOnly.TryParse(Hora, out var hora)) { Erros.Add("Informe a hora no formato 08:30."); return; }
        var c = new Coleta(Selecionada?.C.Id ?? 0, Selecionada?.C.Numero ?? 0, Ext.Dia(Dia).ToDateTime(hora), Remetente?.Id, Destinatario?.Id, Endereco, Mercadoria,
                           Ext.Int(Volumes) ?? 0, Formato.LerValor(Peso) ?? 0, Formato.LerValor(Valor) ?? 0, Veiculo?.Id, Motorista?.Id, Rota?.Id, Situacao?.Valor ?? "AGENDADA", Observacao);
        var r = await app.Frota().GravarColetaAsync(empresa, c, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.C.Numero == r.Valor);
        Mensagem = $"Ordem de coleta nº {r.Valor} gravada.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private void Imprimir()
    {
        Erros.Clear();
        Mensagem = null;
        if (Selecionada?.C is not { } c) { Erros.Add("Selecione a ordem de coleta."); return; }
        try
        {
            var arquivo = ArquivosGerados.Salvar($"ordem_de_coleta_{c.Numero}.pdf", ServicoFrota.OrdemColetaPdf(c, Contexto.DescricaoEmpresa, Contexto.Sessao?.Nome ?? ""));
            Mensagem = $"Ordem de coleta salva em PDF: {arquivo}";
        }
        catch (Exception ex)
        {
            Erros.Add($"Não foi possível gerar o PDF: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.C is not { } c) { Erros.Add("Selecione a ordem de coleta."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir ordem de coleta", $"Excluir a ordem de coleta nº {c.Numero}?")) return;
        var r = await app.Frota().ExcluirColetaAsync(empresa, c.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Ordem de coleta excluída.";
    }, Permissao.FrotaOperacao);
}
