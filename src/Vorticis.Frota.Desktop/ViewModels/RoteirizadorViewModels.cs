using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Frota.Desktop.Views;

namespace Vorticis.Frota.Desktop.ViewModels;

public sealed record ParadaRoteiroLinha(int Ordem, ParadaRota P)
{
    public string Texto => $"{Ordem}. {P.Municipio}/{P.Uf}";
}

/// <summary>Trecho editável: o km pode ser digitado pelo usuário (vale sobre o estimado e o da estrada).</summary>
public sealed partial class TrechoLinha : ObservableObject
{
    public TrechoLinha(TrechoRoteiro t, Action aoMudar)
    {
        T = t;
        _aoMudar = aoMudar;
        _km = Formato.Num(t.Km);
    }

    private readonly Action _aoMudar;
    public TrechoRoteiro T { get; private set; }
    public string Descricao => $"{T.De.Municipio}/{T.De.Uf} → {T.Para.Municipio}/{T.Para.Uf}";
    public string Referencia => $"estimado {Formato.Num(T.KmEstimado)}" + (T.KmEstrada is { } e ? $" · estrada {Formato.Num(e)}" : "");
    [ObservableProperty] private string _km;

    partial void OnKmChanged(string value)
    {
        var lido = Formato.LerValor(value);
        var informado = lido is { } v && v != (T.KmEstrada ?? T.KmEstimado) ? v : (decimal?)null;
        if (informado == T.KmInformado) return;
        T = T with { KmInformado = informado };
        _aoMudar();
    }
}

/// <summary>
/// Operação › Roteirizador: paradas (cidades) do roteiro, ordem mais curta, distâncias estimadas ou pela estrada, km de cada trecho ajustável,
/// custo aproximado (km × custo por km + pedágios), mapa, abertura no Google Maps e gravação como rota.
/// </summary>
public partial class RoteirizadorViewModel(IContexto contexto) : TmsBase(contexto)
{
    private ConfiguracaoFrota _config = new();
    private List<Rota> _rotas = [];
    private IReadOnlyList<(double Lat, double Lon)>? _caminhoEstrada;

    public ObservableCollection<ParadaRoteiroLinha> Paradas { get; } = [];
    public ObservableCollection<TrechoLinha> Trechos { get; } = [];
    public ObservableCollection<Opcao> RotasOpcoes { get; } = [];
    public SeletorMunicipio NovaParada { get; } = new() { Uf = "SP" };

    [ObservableProperty] private ParadaRoteiroLinha? _paradaSelecionada;
    [ObservableProperty] private bool _fixarDestino = true;
    [ObservableProperty] private bool _voltarOrigem;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _rota;
    [ObservableProperty] private string _custoKm = "";
    [ObservableProperty] private string _pedagios = "";
    [ObservableProperty] private string _velocidade = "";
    [ObservableProperty] private string _nomeRota = "";
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _custoTexto = "";
    [ObservableProperty] private IReadOnlyList<PontoMapa> _mapaParadas = [];
    [ObservableProperty] private IReadOnlyList<(double Lat, double Lon)>? _mapaCaminho;

    public string UrlMapa => RegrasRoteirizacao.UrlGoogleMaps(Ordem, VoltarOrigem);
    private IReadOnlyList<ParadaRota> Ordem => [.. Paradas.Select(p => p.P)];

    partial void OnVoltarOrigemChanged(bool value) { if (value) FixarDestino = false; Refazer(); }
    partial void OnFixarDestinoChanged(bool value) { if (value) VoltarOrigem = false; }
    partial void OnCustoKmChanged(string value) => Totalizar();
    partial void OnPedagiosChanged(string value) => Totalizar();
    partial void OnVelocidadeChanged(string value) => Totalizar();

    /// <summary>Escolher uma rota cadastrada carrega as paradas dela (para ajustar, otimizar ou calcular o custo).</summary>
    partial void OnRotaChanged(Opcao? value)
    {
        if (_rotas.FirstOrDefault(r => r.Id == value?.Id) is not { } r) return;
        Definir(r.Paradas);
        if (r.Pedagios > 0) Pedagios = Formato.Num(r.Pedagios);
        NomeRota = r.Descricao;
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, "(nenhum)");
        var cfg = await app.Frota().ConfiguracaoAsync(empresa);
        var rotas = await app.Frota().RotasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        (_config, _rotas) = (cfg, rotas);
        if (Velocidade is "") Velocidade = Formato.Num(cfg.VelocidadeMedia);
        RotasOpcoes.Clear();
        RotasOpcoes.Add(new Opcao(null, "(nova)"));
        foreach (var r in rotas.Where(r => r.Ativa)) RotasOpcoes.Add(new Opcao(r.Id, $"{r.Codigo} – {r.Descricao}"));
        Rota ??= RotasOpcoes[0];
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
    }

    private void Definir(IEnumerable<ParadaRota> paradas)
    {
        Paradas.Clear();
        foreach (var p in paradas) Paradas.Add(new ParadaRoteiroLinha(Paradas.Count + 1, p));
        Refazer();
    }

    /// <summary>Refaz os trechos (km estimado) e o mapa depois de qualquer mudança nas paradas; o km digitado de um trecho igual é mantido.</summary>
    private void Refazer()
    {
        var anteriores = Trechos.ToDictionary(t => (t.T.De.MunicipioIbge, t.T.Para.MunicipioIbge), t => t.T);
        _caminhoEstrada = null;
        Trechos.Clear();
        foreach (var t in RegrasRoteirizacao.Trechos(Ordem, _config.FatorRodoviario, VoltarOrigem))
        {
            var antes = anteriores.GetValueOrDefault((t.De.MunicipioIbge, t.Para.MunicipioIbge));
            Trechos.Add(new TrechoLinha(t with { KmEstrada = antes?.KmEstrada, KmInformado = antes?.KmInformado }, Totalizar));
        }
        AtualizarMapa();
        Totalizar();
        OnPropertyChanged(nameof(UrlMapa));
    }

    private void AtualizarMapa()
    {
        MapaParadas = [.. Paradas.Select(p => BaseMunicipios.Obter(p.P.MunicipioIbge) is { } m ? new PontoMapa(m.Latitude, m.Longitude, $"{p.P.Municipio}/{p.P.Uf}") : null)
                             .Where(p => p is not null).Select(p => p!)];
        MapaCaminho = _caminhoEstrada ?? (VoltarOrigem && MapaParadas.Count > 1 ? [.. MapaParadas.Select(p => (p.Lat, p.Lon)), (MapaParadas[0].Lat, MapaParadas[0].Lon)] : null);
    }

    private void Totalizar()
    {
        var r = RegrasRoteirizacao.Resumir(Trechos.Select(t => t.T), Formato.LerValor(Velocidade) ?? _config.VelocidadeMedia, Formato.LerValor(CustoKm) ?? 0,
                                           Formato.LerValor(Pedagios) ?? 0);
        Resumo = Paradas.Count < 2 ? "Inclua a origem e ao menos um destino."
            : $"{Paradas.Count} parada(s) · {Formato.Num(r.Km)} km · {r.Horas.ToString("N1", Formato.PtBr)} h de direção a {Formato.Num(Formato.LerValor(Velocidade) ?? _config.VelocidadeMedia)} km/h"
              + (VoltarOrigem ? " · com a volta à origem" : "");
        CustoTexto = r.CustoKm == 0 && r.Pedagios == 0 ? "Informe o custo por km (ou escolha o veículo e clique em Sugerir) para ver o custo aproximado."
            : $"Custo aproximado: {Formato.Num(r.Km)} km × {Formato.Moeda(r.CustoKm)} + pedágios {Formato.Moeda(r.Pedagios)} = {Formato.Moeda(r.CustoTotal)}";
    }

    [RelayCommand]
    private void IncluirParada()
    {
        Erros.Clear();
        if (NovaParada.Municipio is null) { Erros.Add("Escolha a UF e o município da parada."); return; }
        var p = NovaParada.Parada;
        if (Paradas.Count > 0 && Paradas[^1].P.MunicipioIbge == p.MunicipioIbge) { Erros.Add("A parada é igual à anterior."); return; }
        // com o destino fixo, a nova parada entra antes do destino final (a partir da terceira)
        var lista = Ordem.ToList();
        if (FixarDestino && lista.Count >= 2) lista.Insert(lista.Count - 1, p); else lista.Add(p);
        Definir(lista);
    }

    private void Mover(int delta)
    {
        if (ParadaSelecionada is not { } s) { Erros.Clear(); Erros.Add("Selecione a parada."); return; }
        var lista = Ordem.ToList();
        var i = s.Ordem - 1;
        var j = i + delta;
        if (j < 0 || j >= lista.Count) return;
        (lista[i], lista[j]) = (lista[j], lista[i]);
        Definir(lista);
        ParadaSelecionada = Paradas[j];
    }

    [RelayCommand] private void Subir() => Mover(-1);
    [RelayCommand] private void Descer() => Mover(1);

    [RelayCommand]
    private void RetirarParada()
    {
        if (ParadaSelecionada is not { } s) { Erros.Clear(); Erros.Add("Selecione a parada."); return; }
        Definir(Ordem.Where((_, k) => k != s.Ordem - 1));
    }

    [RelayCommand]
    private void Limpar()
    {
        (Rota, NomeRota, Pedagios) = (RotasOpcoes.FirstOrDefault(), "", "");
        Definir([]);
    }

    /// <summary>Reordena as paradas pelo caminho mais curto (a origem fica; o destino final também, se marcado).</summary>
    [RelayCommand]
    private void Otimizar()
    {
        Erros.Clear();
        if (Paradas.Count < 3) { Mensagem = "Com até duas paradas não há o que reordenar."; return; }
        var antes = Trechos.Sum(t => t.T.KmEstimado);
        Definir(RegrasRoteirizacao.Otimizar(Ordem, FixarDestino, VoltarOrigem));
        var depois = Trechos.Sum(t => t.T.KmEstimado);
        Mensagem = depois < antes ? $"Ordem otimizada: {Formato.Num(antes - depois)} km a menos (estimado)." : "A ordem atual já é a mais curta.";
    }

    [RelayCommand]
    private Task DistanciasEstradaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ordem = VoltarOrigem && Paradas.Count > 1 ? [.. Ordem, Ordem[0]] : Ordem;
        var r = await app.Frota().BuscarTrajetoAsync(empresa, ordem, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        for (var i = 0; i < Trechos.Count && i < r.Valor.KmTrechos.Count; i++)
        {
            var t = Trechos[i];
            Trechos[i] = new TrechoLinha(t.T with { KmEstrada = r.Valor.KmTrechos[i] }, Totalizar);
        }
        _caminhoEstrada = r.Valor.Pontos.Count > 1 ? r.Valor.Pontos : null;
        AtualizarMapa();
        Totalizar();
        Mensagem = $"Distâncias pela estrada: {Formato.Num(r.Valor.Km)} km, cerca de {r.Valor.Horas.ToString("N1", Formato.PtBr)} h (serviço OSRM / OpenStreetMap).";
    });

    /// <summary>Custo por km do veículo escolhido: combustível e manutenção dos últimos 6 meses pelo km rodado.</summary>
    [RelayCommand]
    private Task SugerirCustoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Veiculo?.Id is not { } v) { Erros.Add("Escolha o veículo."); return; }
        if (await app.Frota().CustoPorKmAsync(empresa, v, Ext.Hoje, usuario) is not { } custo)
        {
            Erros.Add("O veículo não tem abastecimentos suficientes nos últimos 6 meses para calcular o custo por km. Digite o valor.");
            return;
        }
        CustoKm = Formato.Num(custo);
        Mensagem = $"Custo por km do veículo (combustível e manutenção dos últimos 6 meses): {Formato.Moeda(custo)}.";
    });

    /// <summary>Grava o roteiro em Cadastros › Rotas (nova rota ou a escolhida), com as paradas na ordem, o km total e os pedágios.</summary>
    [RelayCommand]
    private Task GravarRotaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Paradas.Count < 2) { Erros.Add("Inclua a origem e ao menos um destino."); return; }
        var existente = _rotas.FirstOrDefault(r => r.Id == Rota?.Id);
        var km = Trechos.Sum(t => t.T.Km);
        var origem = Trechos.Any(t => t.T.KmInformado is not null) ? "MANUAL" : Trechos.All(t => t.T.KmEstrada is not null) ? "RODOVIARIA" : "ESTIMADA";
        var codigo = existente?.Codigo ?? (_rotas.Select(r => r.Codigo).DefaultIfEmpty(0).Max() + 1);
        var paradas = VoltarOrigem ? [.. Ordem, Ordem[0]] : Ordem;
        var rota = new Rota(existente?.Id ?? 0, codigo, string.IsNullOrWhiteSpace(NomeRota) ? $"{Paradas[0].P.Municipio} x {Paradas[^1].P.Municipio}" : NomeRota.Trim(),
                            km, origem, RegrasLogistica.EstimarHoras(km, Formato.LerValor(Velocidade) ?? _config.VelocidadeMedia), Formato.LerValor(Pedagios) ?? 0, true, paradas);
        var r = await app.Frota().GravarRotaAsync(empresa, rota, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Rota = RotasOpcoes.FirstOrDefault(o => o.Id == r.Valor) ?? Rota;
        Mensagem = $"Rota {codigo} – {rota.Descricao} gravada em Cadastros › Rotas ({Formato.Num(km)} km).";
    }, Permissao.FrotaCadastros);
}
