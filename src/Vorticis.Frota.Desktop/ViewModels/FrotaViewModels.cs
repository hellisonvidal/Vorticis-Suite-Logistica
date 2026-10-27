using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Desktop.Comum;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Frota.Desktop.ViewModels;

public static class FrotaTelas
{
    public const string Painel = "frota.painel";
    public const string Veiculos = "frota.veiculos";
    public const string Motoristas = "frota.motoristas";
    public const string Planos = "frota.planos";
    public const string Documentos = "frota.documentos";
    public const string Multas = "frota.multas";
    public const string Financiamentos = "frota.financiamentos";
    public const string Consorcios = "frota.consorcios";
    public const string BensContratos = "frota.contratos.bens";
    public const string Abastecimentos = "frota.abastecimentos";
    public const string Ordens = "frota.ordens";
    public const string Viagens = "frota.viagens";
    public const string RelCustos = "frota.rel.custos";
    public const string RelConsumo = "frota.rel.consumo";
    public const string RelViagens = "frota.rel.viagens";
    public const string RelVencimentos = "frota.rel.vencimentos";
    public const string Itens = "frota.pecas";
    public const string Autorizacao = "frota.autorizacao";
    public const string Notas = "frota.notas";
    public const string RelAcompanhamento = "frota.rel.ordens";
    public const string RelAprovacoes = "frota.rel.aprovacoes";
    public const string RelNotas = "frota.rel.notas";
    public const string Frotas = "frota.frotas";
    public const string Rotas = "frota.rotas";
    public const string Diarias = "frota.diarias";
    public const string Coletas = "frota.coletas";
    public const string RelCustosFrota = "frota.rel.frotas";
    public const string RelDiariasColaborador = "frota.rel.diarias";
    public const string RelDiariasFrota = "frota.rel.diariasfrota";
    public const string RelConsumoVeiculo = "frota.rel.consumoveiculo";
    public const string RelConsumoMotorista = "frota.rel.consumomotorista";
    public const string RelPreventivas = "frota.rel.preventivas";
    public const string RelHistoricoManutencao = "frota.rel.historicoman";
    public const string RelViagensMotorista = "frota.rel.viagensmotorista";
    public const string RelViagensCliente = "frota.rel.viagenscliente";
    public const string RelViagensRota = "frota.rel.viagensrota";
    public const string RelColetasCliente = "frota.rel.coletascliente";
    public const string RelColetasRota = "frota.rel.coletasrota";
    public const string RelColetasMotorista = "frota.rel.coletasmotorista";
    public const string RelDiariasAcerto = "frota.rel.diariasacerto";
    public const string RelResumo = "frota.rel.resumo";
    public const string Pedagio = "frota.pedagio";
    public const string LayoutsPedagio = "frota.pedagio.layouts";
    // TMS (gestão do transporte)
    public const string TabelasFrete = "tms.tabelas";
    public const string Cotacoes = "tms.cotacoes";
    public const string Ctes = "tms.ctes";
    public const string Manifestos = "tms.manifestos";
    public const string Entregas = "tms.entregas";
    public const string Faturas = "tms.faturas";
    public const string RelFretesCliente = "tms.rel.fretescliente";
    public const string RelFretesTrecho = "tms.rel.fretestrecho";
    public const string RelConhecimentos = "tms.rel.ctes";
    public const string RelPrazoEntrega = "tms.rel.prazo";
    public const string RelFaturas = "tms.rel.faturas";
}

internal static class Ext
{
    public static ServicoFrota Frota(this VorticisApp app) => ServicosFrota.De(app);
    public static DateOnly Dia(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : DateOnly.FromDateTime(DateTime.Today);
    public static DateTime Dt(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
    public static DateTime? DtOpc(DateOnly? d) => d is { } v ? Dt(v) : null;
    public static DateOnly? DiaOpc(DateTime? d) => d is { } v ? DateOnly.FromDateTime(v) : null;
    public static int? Int(string? s) => int.TryParse((s ?? "").Trim(), out var i) ? i : null;
    public static string Num(decimal? v) => v is { } x ? Formato.Num(x) : "";
    public static string Km(decimal? v) => v is { } x ? x.ToString("N0", Formato.PtBr) : "";
    public static string Data(DateOnly? d) => d is { } x ? x.ToString("dd/MM/yyyy") : "";
    public static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);
}

public sealed record Opcao(long? Id, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>Código gravado no banco com o rótulo mostrado na tela (tipo de veículo, combustível, tipo de documento...).</summary>
public sealed record OpcaoCodigo(string Valor)
{
    public override string ToString() => RegrasFrota.Rotulo(Valor);
    public static IReadOnlyList<OpcaoCodigo> De(IEnumerable<string> valores) => [.. valores.Select(v => new OpcaoCodigo(v))];
}

/// <summary>Base das telas da frota: combos de veículos e motoristas e permissão de edição.</summary>
public abstract partial class FrotaBase(IContexto contexto, Permissao edicao) : ModuloViewModel(contexto)
{
    public ObservableCollection<Opcao> VeiculosOpcoes { get; } = [];
    public ObservableCollection<Opcao> MotoristasOpcoes { get; } = [];
    protected List<Veiculo> VeiculosCadastrados { get; private set; } = [];
    public bool PodeEditar => Contexto.Pode(edicao);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeEditar));

    protected async Task CarregarCombosAsync(VorticisApp app, long empresa, long usuario, string semVeiculo = "(escolha)", bool soQueAbastecem = false)
    {
        VeiculosCadastrados = await app.Frota().VeiculosAsync(empresa, usuario);
        var motoristas = await app.Frota().MotoristasAsync(empresa, usuario);
        VeiculosOpcoes.Clear();
        VeiculosOpcoes.Add(new Opcao(null, semVeiculo));
        foreach (var v in VeiculosCadastrados.Where(v => v.Ativo && (!soQueAbastecem || v.Abastece))) VeiculosOpcoes.Add(new Opcao(v.Id, v.Descricao));
        MotoristasOpcoes.Clear();
        MotoristasOpcoes.Add(new Opcao(null, "(nenhum)"));
        foreach (var m in motoristas.Where(m => m.Ativo)) MotoristasOpcoes.Add(new Opcao(m.Id, m.Nome));
    }

    /// <summary>Opção do combo para o id gravado (inclui o inativo já usado no registro, para não perder a informação ao alterar).</summary>
    protected static Opcao? Escolher(ObservableCollection<Opcao> opcoes, long? id, Func<string>? textoInativo = null)
    {
        if (opcoes.FirstOrDefault(o => o.Id == id) is { } o) return o;
        if (id is null || textoInativo is null) return opcoes.FirstOrDefault();
        var novo = new Opcao(id, textoInativo());
        opcoes.Add(novo);
        return novo;
    }
}

// ---------------------------------------------------------------------------------------------- painel

public sealed record AvisoLinha(AvisoFrota A)
{
    public string Situacao => A.Gravidade switch { GravidadeAviso.Vencido => "VENCIDO", GravidadeAviso.AVencer => "A vencer", _ => "Atenção" };
    public string Assunto => A.Assunto;
    public string Descricao => A.Descricao;
    public string Data => Ext.Data(A.Data);
}

/// <summary>Relatórios › Painel da frota: vencimentos de documentos e CNHs, preventivas devidas e consumo fora do esperado.</summary>
public partial class PainelFrotaViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<AvisoLinha> Avisos { get; } = [];
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _numeros = "";

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var avisos = await app.Frota().AvisosAsync(empresa, Ext.Hoje, sessao.Id);
        var veiculos = await app.Frota().VeiculosAsync(empresa, sessao.Id);
        var motoristas = await app.Frota().MotoristasAsync(empresa, sessao.Id);
        var viagens = await app.Frota().ViagensAsync(empresa, Ext.Hoje.AddYears(-1), Ext.Hoje, sessao.Id);
        if (!EhAtual(g)) return;
        Avisos.Clear();
        foreach (var a in avisos) Avisos.Add(new AvisoLinha(a));
        var vencidos = avisos.Count(a => a.Gravidade == GravidadeAviso.Vencido);
        Resumo = avisos.Count == 0 ? "Nenhuma pendência: documentos, CNHs e manutenções em dia."
            : $"{vencidos} vencido(s) · {avisos.Count(a => a.Gravidade == GravidadeAviso.AVencer)} a vencer · {avisos.Count(a => a.Gravidade == GravidadeAviso.Atencao)} alerta(s) de consumo";
        Numeros = $"{veiculos.Count(v => v.Ativo)} veículo(s) ativo(s) · {motoristas.Count(m => m.Ativo)} motorista(s) · {viagens.Count(v => v.Situacao == "EM_ANDAMENTO")} viagem(ns) em andamento";
    }

    [RelayCommand]
    private Task AtualizarAsync() => CarregarAsync();
}

// ---------------------------------------------------------------------------------------------- veículos

public sealed record VeiculoLinha(Veiculo V)
{
    public int Codigo => V.Codigo;
    public string Placa => RegrasFrota.FormatarPlaca(V.Placa);
    public string Tipo => RegrasFrota.Rotulo(V.Tipo);
    public string MarcaModelo => $"{V.Marca} {V.Modelo}".Trim();
    public string Ano => V.AnoFabricacao is { } f ? $"{f}/{V.AnoModelo ?? f}" : "";
    public string Combustivel => RegrasFrota.Rotulo(V.Combustivel);
    public string Km => Ext.Km(V.KmAtual);
    public string Situacao => V.Ativo ? "Ativo" : "Inativo";
}

/// <summary>Cadastros › Veículos (cavalos, caminhões, reboques e semirreboques, utilitários...).</summary>
public partial class VeiculosViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaCadastros)
{
    public ObservableCollection<VeiculoLinha> Lista { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Tipos { get; } = OpcaoCodigo.De(RegrasFrota.TiposVeiculo);
    public static IReadOnlyList<OpcaoCodigo> Combustiveis { get; } = OpcaoCodigo.De(RegrasFrota.CombustiveisVeiculo);
    [ObservableProperty] private VeiculoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _placa = "";
    [ObservableProperty] private string _renavam = "";
    [ObservableProperty] private OpcaoCodigo? _tipo = Tipos[0];
    [ObservableProperty] private string _marca = "";
    [ObservableProperty] private string _modelo = "";
    [ObservableProperty] private string _anoFabricacao = "";
    [ObservableProperty] private string _anoModelo = "";
    [ObservableProperty] private OpcaoCodigo? _combustivel = Combustiveis[0];
    [ObservableProperty] private string _capacidade = "";
    [ObservableProperty] private string _kmAtual = "";
    [ObservableProperty] private string _mediaEsperada = "";
    [ObservableProperty] private bool _ativo = true;
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _totais = "";
    public ObservableCollection<Opcao> FrotasOpcoes { get; } = [];
    [ObservableProperty] private Opcao? _frota;

    /// <summary>Reboque e semirreboque não têm motor: o combustível passa a "Não abastece".</summary>
    partial void OnTipoChanged(OpcaoCodigo? value)
    {
        if (value?.Valor is "REBOQUE" or "SEMIRREBOQUE") Combustivel = Combustiveis.First(c => c.Valor == "NENHUM");
    }

    partial void OnSelecionadoChanged(VeiculoLinha? value)
    {
        if (value?.V is not { } v) return;
        (Codigo, Placa, Renavam, Marca, Modelo) = (v.Codigo.ToString(), RegrasFrota.FormatarPlaca(v.Placa), v.Renavam ?? "", v.Marca ?? "", v.Modelo ?? "");
        Tipo = Tipos.FirstOrDefault(t => t.Valor == v.Tipo);
        Combustivel = Combustiveis.FirstOrDefault(c => c.Valor == v.Combustivel);
        (AnoFabricacao, AnoModelo, Capacidade, KmAtual, MediaEsperada) = (v.AnoFabricacao?.ToString() ?? "", v.AnoModelo?.ToString() ?? "", Ext.Num(v.CapacidadeKg),
                                                                            Ext.Num(v.KmAtual), Ext.Num(v.MediaEsperada));
        (Ativo, Observacao) = (v.Ativo, v.Observacao ?? "");
        Frota = FrotasOpcoes.FirstOrDefault(f => f.Id == v.FrotaId) ?? FrotasOpcoes.FirstOrDefault();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().VeiculosAsync(empresa, sessao.Id);
        var frotas = await app.Frota().FrotasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        FrotasOpcoes.Clear();
        FrotasOpcoes.Add(new Opcao(null, "(nenhuma)"));
        foreach (var f in frotas.Where(f => f.Ativa)) FrotasOpcoes.Add(new Opcao(f.Id, $"{f.Codigo} – {f.Descricao}"));
        Frota ??= FrotasOpcoes.FirstOrDefault();
        Lista.Clear();
        foreach (var v in itens) Lista.Add(new VeiculoLinha(v));
        Totais = $"{itens.Count(v => v.Ativo)} veículo(s) ativo(s), {itens.Count(v => v.Ativo && !v.Abastece)} reboque(s)";
        if (Selecionado is null && string.IsNullOrEmpty(Codigo)) Novo();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        Codigo = ((Lista.Count == 0 ? 0 : Lista.Max(l => l.V.Codigo)) + 1).ToString();
        (Placa, Renavam, Marca, Modelo, AnoFabricacao, AnoModelo, Capacidade, KmAtual, MediaEsperada, Ativo, Observacao) = ("", "", "", "", "", "", "", "0", "", true, "");
        (Tipo, Combustivel) = (Tipos[0], Combustiveis[0]);
        Frota = FrotasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var v = new Veiculo(Selecionado?.V.Id ?? 0, Ext.Int(Codigo) ?? 0, Placa, Renavam, Tipo?.Valor ?? "", Marca, Modelo, Ext.Int(AnoFabricacao), Ext.Int(AnoModelo),
                            Combustivel?.Valor ?? "", Formato.LerValor(Capacidade), Formato.LerValor(KmAtual) ?? 0, Formato.LerValor(MediaEsperada), Ativo, Observacao, Frota?.Id);
        var r = await app.Frota().GravarVeiculoAsync(empresa, v, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.V.Id == r.Valor);
        Mensagem = "Veículo gravado.";
    }, Permissao.FrotaCadastros);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.V is not { } v) { Erros.Add("Selecione o veículo."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir veículo", $"Excluir o veículo {RegrasFrota.FormatarPlaca(v.Placa)}?")) return;
        var r = await app.Frota().ExcluirVeiculoAsync(empresa, v.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionado = null;
        await CarregarAsync();
        Novo();
        Mensagem = "Veículo excluído.";
    }, Permissao.FrotaCadastros);
}

// ---------------------------------------------------------------------------------------------- motoristas

public sealed record MotoristaLinha(Motorista M)
{
    public int Codigo => M.Codigo;
    public string Nome => M.Nome;
    public string Cpf => Vorticis.Domain.Cadastros.Cpf.Formatar(M.Cpf);
    public string Cnh => M.Cnh is null ? "" : $"{M.Cnh} ({M.CnhCategoria})";
    public string Validade => Ext.Data(M.CnhValidade);
    public string SituacaoCnh => M.CnhValidade is { } d ? RegrasFrota.TextoVencimento(d, Ext.Hoje) : "";
    public string Telefone => M.Telefone ?? "";
    public string Situacao => M.Ativo ? "Ativo" : "Inativo";
}

/// <summary>Cadastros › Motoristas (CNH com categoria e validade, que entra no painel de vencimentos).</summary>
public partial class MotoristasViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaCadastros)
{
    public ObservableCollection<MotoristaLinha> Lista { get; } = [];
    public static IReadOnlyList<string> Categorias { get; } = RegrasFrota.CategoriasCnh;
    [ObservableProperty] private MotoristaLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cpf = "";
    [ObservableProperty] private string _cnh = "";
    [ObservableProperty] private string? _categoria = "E";
    [ObservableProperty] private DateTime? _validade;
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private bool _ativo = true;

    partial void OnSelecionadoChanged(MotoristaLinha? value)
    {
        if (value?.M is not { } m) return;
        (Codigo, Nome, Cpf, Cnh, Categoria, Validade, Telefone, Ativo) = (m.Codigo.ToString(), m.Nome, Vorticis.Domain.Cadastros.Cpf.Formatar(m.Cpf), m.Cnh ?? "",
                                                                          m.CnhCategoria, Ext.DtOpc(m.CnhValidade), m.Telefone ?? "", m.Ativo);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var itens = await app.Frota().MotoristasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var m in itens) Lista.Add(new MotoristaLinha(m));
        if (Selecionado is null && string.IsNullOrEmpty(Codigo)) Novo();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        Codigo = ((Lista.Count == 0 ? 0 : Lista.Max(l => l.M.Codigo)) + 1).ToString();
        (Nome, Cpf, Cnh, Categoria, Validade, Telefone, Ativo) = ("", "", "", "E", null, "", true);
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var m = new Motorista(Selecionado?.M.Id ?? 0, Ext.Int(Codigo) ?? 0, Nome, Cpf, Cnh, string.IsNullOrWhiteSpace(Cnh) ? null : Categoria, Ext.DiaOpc(Validade), Telefone, Ativo);
        var r = await app.Frota().GravarMotoristaAsync(empresa, m, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.M.Id == r.Valor);
        Mensagem = "Motorista gravado.";
    }, Permissao.FrotaCadastros);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione o motorista."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir motorista", $"Excluir {m.Nome}?")) return;
        var r = await app.Frota().ExcluirMotoristaAsync(empresa, m.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Selecionado = null;
        await CarregarAsync();
        Novo();
        Mensagem = "Motorista excluído.";
    }, Permissao.FrotaCadastros);
}

// ---------------------------------------------------------------------------------------------- planos de manutenção

public sealed record PlanoLinha(PlanoManutencao P, SituacaoPreventiva S)
{
    public string Placa => RegrasFrota.FormatarPlaca(P.Placa ?? "");
    public string Servico => P.Descricao;
    public string Intervalo => string.Join(" ou ", new[] { P.IntervaloKm is { } k ? $"{Ext.Km(k)} km" : null, P.IntervaloDias is { } d ? $"{d} dias" : null }.Where(x => x is not null));
    public string Ultima => string.Join(" · ", new[] { P.UltimaKm is { } k ? $"{Ext.Km(k)} km" : null, P.UltimaData is { } d ? Ext.Data(d) : null }.Where(x => x is not null));
    public string Proxima => string.Join(" ou ", new[] { S.ProximaKm is { } k ? $"{Ext.Km(k)} km" : null, S.ProximaData is { } d ? Ext.Data(d) : null }.Where(x => x is not null));
    public string Situacao => P.Ativo ? S.Texto : "Inativo";
}

/// <summary>Cadastros › Planos de manutenção: preventivas por km, por dias ou pelo que vencer primeiro.</summary>
public partial class PlanosViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaManutencao)
{
    public ObservableCollection<PlanoLinha> Lista { get; } = [];
    [ObservableProperty] private PlanoLinha? _selecionado;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _intervaloKm = "";
    [ObservableProperty] private string _intervaloDias = "";
    [ObservableProperty] private string _ultimaKm = "";
    [ObservableProperty] private DateTime? _ultimaData;
    [ObservableProperty] private bool _ativo = true;

    partial void OnSelecionadoChanged(PlanoLinha? value)
    {
        if (value?.P is not { } p) return;
        Veiculo = Escolher(VeiculosOpcoes, p.VeiculoId, () => RegrasFrota.FormatarPlaca(p.Placa ?? ""));
        (Descricao, IntervaloKm, IntervaloDias, UltimaKm, UltimaData, Ativo) = (p.Descricao, Ext.Num(p.IntervaloKm), p.IntervaloDias?.ToString() ?? "", Ext.Num(p.UltimaKm),
                                                                                Ext.DtOpc(p.UltimaData), p.Ativo);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id);
        var cfg = await app.Frota().ConfiguracaoAsync(empresa);
        var itens = await app.Frota().PlanosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var p in itens) Lista.Add(new PlanoLinha(p, RegrasFrota.Situacao(p, p.KmAtual, Ext.Hoje, cfg.DiasAviso)));
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        Veiculo = VeiculosOpcoes.FirstOrDefault();
        (Descricao, IntervaloKm, IntervaloDias, UltimaKm, UltimaData, Ativo) = ("", "", "", "", null, true);
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Veiculo?.Id is not { } v) { Erros.Add("Escolha o veículo."); return; }
        var p = new PlanoManutencao(Selecionado?.P.Id ?? 0, v, Descricao, Formato.LerValor(IntervaloKm), Ext.Int(IntervaloDias), Formato.LerValor(UltimaKm),
                                    Ext.DiaOpc(UltimaData), Ativo);
        var r = await app.Frota().GravarPlanoAsync(empresa, p, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.P.Id == r.Valor);
        Mensagem = "Plano gravado.";
    }, Permissao.FrotaManutencao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.P is not { } p) { Erros.Add("Selecione o plano."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir plano", $"Excluir \"{p.Descricao}\" do veículo {RegrasFrota.FormatarPlaca(p.Placa ?? "")}?")) return;
        var r = await app.Frota().ExcluirPlanoAsync(empresa, p.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Plano excluído.";
    }, Permissao.FrotaManutencao);
}

// ---------------------------------------------------------------------------------------------- documentos

public sealed record DocumentoLinha(DocumentoFrota D, int DiasAviso)
{
    public string Tipo => RegrasFrota.Rotulo(D.Tipo);
    public string Dono => D.Dono ?? "";
    public string Numero => D.Numero ?? "";
    public string Vencimento => Ext.Data(D.Vencimento);
    public string Valor => D.Valor is { } v ? Formato.Moeda(v) : "";
    public string Situacao => RegrasFrota.SituacaoVencimento(D.Vencimento, Ext.Hoje, DiasAviso) is null ? "Em dia" : RegrasFrota.TextoVencimento(D.Vencimento, Ext.Hoje);
}

/// <summary>Cadastros › Documentos e vencimentos: licenciamento, seguro, ANTT/RNTRC, tacógrafo, exames... do veículo, do motorista ou da empresa.</summary>
public partial class DocumentosViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaCadastros)
{
    public ObservableCollection<DocumentoLinha> Lista { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Tipos { get; } = OpcaoCodigo.De(RegrasFrota.TiposDocumento);
    [ObservableProperty] private DocumentoLinha? _selecionado;
    [ObservableProperty] private OpcaoCodigo? _tipo = Tipos[0];
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private DateTime? _vencimento = DateTime.Today;
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _observacao = "";

    /// <summary>O documento é de um veículo ou de um motorista: escolher um limpa o outro.</summary>
    partial void OnVeiculoChanged(Opcao? value) { if (value?.Id is not null && Motorista?.Id is not null) Motorista = MotoristasOpcoes.FirstOrDefault(); }
    partial void OnMotoristaChanged(Opcao? value) { if (value?.Id is not null && Veiculo?.Id is not null) Veiculo = VeiculosOpcoes.FirstOrDefault(); }

    partial void OnSelecionadoChanged(DocumentoLinha? value)
    {
        if (value?.D is not { } d) return;
        Tipo = Tipos.FirstOrDefault(t => t.Valor == d.Tipo);
        Veiculo = Escolher(VeiculosOpcoes, d.VeiculoId, () => d.Dono ?? "");
        Motorista = Escolher(MotoristasOpcoes, d.MotoristaId, () => d.Dono ?? "");
        (Numero, Vencimento, Valor, Observacao) = (d.Numero ?? "", Ext.Dt(d.Vencimento), Ext.Num(d.Valor), d.Observacao ?? "");
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, "(nenhum)");
        var cfg = await app.Frota().ConfiguracaoAsync(empresa);
        var itens = await app.Frota().DocumentosAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var d in itens) Lista.Add(new DocumentoLinha(d, cfg.DiasAviso));
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Motorista ??= MotoristasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Tipo, Veiculo, Motorista) = (Tipos[0], VeiculosOpcoes.FirstOrDefault(), MotoristasOpcoes.FirstOrDefault());
        (Numero, Vencimento, Valor, Observacao) = ("", DateTime.Today, "", "");
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var d = new DocumentoFrota(Selecionado?.D.Id ?? 0, Veiculo?.Id, Motorista?.Id, Tipo?.Valor ?? "", Numero, Ext.Dia(Vencimento), Formato.LerValor(Valor), Observacao);
        var r = await app.Frota().GravarDocumentoAsync(empresa, d, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionado = Lista.FirstOrDefault(l => l.D.Id == r.Valor);
        Mensagem = "Documento gravado.";
    }, Permissao.FrotaCadastros);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.D is not { } d) { Erros.Add("Selecione o documento."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir documento", $"Excluir {RegrasFrota.Rotulo(d.Tipo)} – {d.Dono}?")) return;
        var r = await app.Frota().ExcluirDocumentoAsync(empresa, d.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Documento excluído.";
    }, Permissao.FrotaCadastros);
}

// ---------------------------------------------------------------------------------------------- abastecimentos

public sealed record AbastecimentoLinha(Abastecimento A, MediaConsumo? M, bool Alerta)
{
    public string Data => Ext.Data(A.Data);
    public string Placa => RegrasFrota.FormatarPlaca(A.Placa ?? "");
    public string Motorista => A.MotoristaNome ?? "";
    public string Km => Ext.Km(A.Km);
    public string Litros => Formato.Num(A.Litros);
    public string Valor => Formato.Moeda(A.ValorTotal);
    public string PrecoLitro => A.PrecoLitro.ToString("N3", Formato.PtBr);
    public string Combustivel => RegrasFrota.Rotulo(A.Combustivel) + (A.TanqueCheio ? "" : " (parcial)");
    public string Media => M is null ? "" : string.Create(Formato.PtBr, $"{M.Media:N2} km/l") + (Alerta ? " ⚠" : "");
    public string Posto => A.Posto ?? "";
}

/// <summary>Operação › Abastecimentos: média de consumo de tanque cheio a tanque cheio e alerta quando fica abaixo do esperado.</summary>
public partial class AbastecimentosViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    public ObservableCollection<AbastecimentoLinha> Lista { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Combustiveis { get; } = OpcaoCodigo.De(RegrasFrota.CombustiveisAbastecimento);
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private AbastecimentoLinha? _selecionado;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private DateTime? _data = DateTime.Today;
    [ObservableProperty] private string _km = "";
    [ObservableProperty] private string _litros = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private OpcaoCodigo? _combustivel = Combustiveis[0];
    [ObservableProperty] private string _posto = "";
    [ObservableProperty] private bool _tanqueCheio = true;
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _totais = "";

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();

    /// <summary>Sugere o combustível do veículo (flex começa pela gasolina).</summary>
    partial void OnVeiculoChanged(Opcao? value)
    {
        if (Selecionado is not null || VeiculosCadastrados.FirstOrDefault(v => v.Id == value?.Id) is not { } v) return;
        var c = v.Combustivel == "FLEX" ? "GASOLINA" : v.Combustivel;
        Combustivel = Combustiveis.FirstOrDefault(x => x.Valor == c) ?? Combustivel;
        if (string.IsNullOrEmpty(Km)) Km = Ext.Num(v.KmAtual);
    }

    partial void OnSelecionadoChanged(AbastecimentoLinha? value)
    {
        if (value?.A is not { } a) return;
        Veiculo = Escolher(VeiculosOpcoes, a.VeiculoId, () => RegrasFrota.FormatarPlaca(a.Placa ?? ""));
        Motorista = Escolher(MotoristasOpcoes, a.MotoristaId, () => a.MotoristaNome ?? "");
        (Data, Km, Litros, Valor, Posto, TanqueCheio, Observacao) = (Ext.Dt(a.Data), Ext.Num(a.Km), Formato.Num(a.Litros), Formato.Num(a.ValorTotal), a.Posto ?? "",
                                                                     a.TanqueCheio, a.Observacao ?? "");
        Combustivel = Combustiveis.FirstOrDefault(c => c.Valor == a.Combustivel);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id, soQueAbastecem: true);
        var cfg = await app.Frota().ConfiguracaoAsync(empresa);
        // as médias do período precisam do tanque cheio anterior: busca 90 dias antes e mostra só o período
        var ini = Ext.Dia(Inicio);
        var itens = await app.Frota().AbastecimentosAsync(empresa, ini.AddDays(-90), Ext.Dia(Fim), null, sessao.Id);
        if (!EhAtual(g)) return;
        var medias = RegrasFrota.Medias(itens).GroupBy(m => (m.VeiculoId, m.Data)).ToDictionary(x => x.Key, x => x.Last());
        var esperada = VeiculosCadastrados.ToDictionary(v => v.Id, v => v.MediaEsperada);
        Lista.Clear();
        var doPeriodo = itens.Where(a => a.Data >= ini).ToList();
        foreach (var a in doPeriodo.OrderByDescending(a => a.Data).ThenByDescending(a => a.Km))
        {
            var m = a.TanqueCheio && a.Combustivel != "ARLA32" ? medias.GetValueOrDefault((a.VeiculoId, a.Data)) : null;
            Lista.Add(new AbastecimentoLinha(a, m, m is not null && RegrasFrota.ConsumoForaDoEsperado(m.Media, esperada.GetValueOrDefault(a.VeiculoId), cfg.ToleranciaConsumo)));
        }
        var semArla = doPeriodo.Where(a => a.Combustivel != "ARLA32").ToList();
        Totais = $"{doPeriodo.Count} abastecimento(s) · {Formato.Num(semArla.Sum(a => a.Litros))} litros · {Formato.Moeda(doPeriodo.Sum(a => a.ValorTotal))}"
                 + (Lista.Count(l => l.Alerta) is > 0 and var n ? $" · {n} média(s) abaixo do esperado (⚠)" : "");
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Motorista ??= MotoristasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        (Data, Km, Litros, Valor, Posto, TanqueCheio, Observacao) = (DateTime.Today, "", "", "", "", true, "");
        (Veiculo, Motorista) = (VeiculosOpcoes.FirstOrDefault(), MotoristasOpcoes.FirstOrDefault());
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Veiculo?.Id is not { } v) { Erros.Add("Escolha o veículo."); return; }
        var a = new Abastecimento(Selecionado?.A.Id ?? 0, v, Motorista?.Id, Ext.Dia(Data), Formato.LerValor(Km) ?? -1, Formato.LerValor(Litros) ?? 0,
                                  Formato.LerValor(Valor) ?? 0, Combustivel?.Valor ?? "", Posto, TanqueCheio, Observacao);
        var r = await app.Frota().GravarAbastecimentoAsync(empresa, a, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        var gravado = Lista.FirstOrDefault(l => l.A.Id == r.Valor);
        Novo();
        Mensagem = gravado?.Alerta == true ? string.Create(Formato.PtBr, $"Abastecimento gravado. Atenção: média de {gravado.M!.Media:N2} km/l, abaixo do esperado para o veículo.")
            : gravado?.M is { } m ? string.Create(Formato.PtBr, $"Abastecimento gravado. Média desde o último tanque cheio: {m.Media:N2} km/l.") : "Abastecimento gravado.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.A is not { } a) { Erros.Add("Selecione o abastecimento."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir abastecimento", $"Excluir o abastecimento de {a.Data:dd/MM/yyyy} do veículo {RegrasFrota.FormatarPlaca(a.Placa ?? "")}?")) return;
        var r = await app.Frota().ExcluirAbastecimentoAsync(empresa, a.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Novo();
        await CarregarAsync();
        Mensagem = "Abastecimento excluído.";
    }, Permissao.FrotaOperacao);
}

// ---------------------------------------------------------------------------------------------- viagens

public sealed record ViagemLinha(Viagem V)
{
    public int Numero => V.Numero;
    public string Saida => Ext.Data(V.Saida);
    public string Placa => RegrasFrota.FormatarPlaca(V.Placa ?? "");
    public string Motorista => V.MotoristaNome ?? "";
    public string Trajeto => $"{V.Origem} → {V.Destino}";
    public string Cliente => V.Cliente ?? "";
    public string KmRodado => Ext.Km(V.KmRodado);
    public string Frete => Formato.Moeda(V.ValorFrete);
    public string Despesas => Formato.Moeda(V.Despesas);
    public string Situacao => RegrasFrota.Rotulo(V.Situacao);
}

/// <summary>Operação › Viagens: frete, pedágio, diárias e outras despesas; o resultado com o combustível sai em Relatórios › Resultado das viagens.</summary>
public partial class ViagensViewModel(IContexto contexto) : FrotaBase(contexto, Permissao.FrotaOperacao)
{
    public ObservableCollection<ViagemLinha> Lista { get; } = [];
    public static IReadOnlyList<OpcaoCodigo> Situacoes { get; } = OpcaoCodigo.De(["EM_ANDAMENTO", "CONCLUIDA", "CANCELADA"]);
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);
    [ObservableProperty] private ViagemLinha? _selecionada;
    [ObservableProperty] private Opcao? _veiculo;
    [ObservableProperty] private Opcao? _motorista;
    [ObservableProperty] private string _cliente = "";
    [ObservableProperty] private string _origem = "";
    [ObservableProperty] private string _destino = "";
    [ObservableProperty] private DateTime? _saida = DateTime.Today;
    [ObservableProperty] private DateTime? _chegada;
    [ObservableProperty] private string _kmSaida = "";
    [ObservableProperty] private string _kmChegada = "";
    [ObservableProperty] private string _frete = "";
    [ObservableProperty] private string _pedagio = "";
    [ObservableProperty] private string _diarias = "";
    [ObservableProperty] private string _outras = "";
    [ObservableProperty] private OpcaoCodigo? _situacao = Situacoes[0];
    [ObservableProperty] private string _totais = "";

    partial void OnInicioChanged(DateTime? value) => _ = CarregarAsync();
    partial void OnFimChanged(DateTime? value) => _ = CarregarAsync();

    partial void OnVeiculoChanged(Opcao? value)
    {
        if (Selecionada is null && string.IsNullOrEmpty(KmSaida) && VeiculosCadastrados.FirstOrDefault(v => v.Id == value?.Id) is { } v) KmSaida = Ext.Num(v.KmAtual);
    }

    partial void OnSelecionadaChanged(ViagemLinha? value)
    {
        if (value?.V is not { } v) return;
        Veiculo = Escolher(VeiculosOpcoes, v.VeiculoId, () => RegrasFrota.FormatarPlaca(v.Placa ?? ""));
        Motorista = Escolher(MotoristasOpcoes, v.MotoristaId, () => v.MotoristaNome ?? "");
        (Cliente, Origem, Destino, Saida, Chegada, KmSaida, KmChegada) = (v.Cliente ?? "", v.Origem, v.Destino, Ext.Dt(v.Saida), Ext.DtOpc(v.Chegada), Ext.Num(v.KmSaida), Ext.Num(v.KmChegada));
        (Frete, Pedagio, Diarias, Outras) = (Formato.Num(v.ValorFrete), Formato.Num(v.Pedagio), Formato.Num(v.Diarias), Formato.Num(v.OutrasDespesas));
        Situacao = Situacoes.FirstOrDefault(s => s.Valor == v.Situacao);
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        await CarregarCombosAsync(app, empresa, sessao.Id);
        var itens = await app.Frota().ViagensAsync(empresa, Ext.Dia(Inicio), Ext.Dia(Fim), sessao.Id);
        if (!EhAtual(g)) return;
        Lista.Clear();
        foreach (var v in itens) Lista.Add(new ViagemLinha(v));
        var validas = itens.Where(v => v.Situacao != "CANCELADA").ToList();
        Totais = $"{validas.Count} viagem(ns) · frete {Formato.Moeda(validas.Sum(v => v.ValorFrete))} · despesas {Formato.Moeda(validas.Sum(v => v.Despesas))}";
        Veiculo ??= VeiculosOpcoes.FirstOrDefault();
        Motorista ??= MotoristasOpcoes.FirstOrDefault();
    }

    [RelayCommand]
    private void Nova()
    {
        Selecionada = null;
        (Cliente, Origem, Destino, Saida, Chegada, KmSaida, KmChegada) = ("", "", "", DateTime.Today, null, "", "");
        (Frete, Pedagio, Diarias, Outras, Situacao) = ("", "", "", "", Situacoes[0]);
        (Veiculo, Motorista) = (VeiculosOpcoes.FirstOrDefault(), MotoristasOpcoes.FirstOrDefault());
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Veiculo?.Id is not { } veiculo) { Erros.Add("Escolha o veículo."); return; }
        var v = new Viagem(Selecionada?.V.Id ?? 0, Selecionada?.V.Numero ?? 0, veiculo, Motorista?.Id, Cliente, Origem, Destino, Ext.Dia(Saida), Ext.DiaOpc(Chegada),
                           Formato.LerValor(KmSaida) ?? 0, Formato.LerValor(KmChegada), Formato.LerValor(Frete) ?? 0, Formato.LerValor(Pedagio) ?? 0,
                           Formato.LerValor(Diarias) ?? 0, Formato.LerValor(Outras) ?? 0, Situacao?.Valor ?? "EM_ANDAMENTO");
        var r = await app.Frota().GravarViagemAsync(empresa, v, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Selecionada = Lista.FirstOrDefault(l => l.V.Numero == r.Valor);
        Mensagem = $"Viagem nº {r.Valor} gravada.";
    }, Permissao.FrotaOperacao);

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada?.V is not { } v) { Erros.Add("Selecione a viagem."); return; }
        if (!await Contexto.ConfirmarAsync("Excluir viagem", $"Excluir a viagem nº {v.Numero} ({v.Origem} → {v.Destino})?")) return;
        var r = await app.Frota().ExcluirViagemAsync(empresa, v.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Nova();
        await CarregarAsync();
        Mensagem = "Viagem excluída.";
    }, Permissao.FrotaOperacao);
}

// ---------------------------------------------------------------------------------------------- relatórios

public sealed record LinhaRelatorioFrota(string[] V, bool Destaque)
{
    public string V0 => V.ElementAtOrDefault(0) ?? "";
    public string V1 => V.ElementAtOrDefault(1) ?? "";
    public string V2 => V.ElementAtOrDefault(2) ?? "";
    public string V3 => V.ElementAtOrDefault(3) ?? "";
    public string V4 => V.ElementAtOrDefault(4) ?? "";
    public string V5 => V.ElementAtOrDefault(5) ?? "";
    public string V6 => V.ElementAtOrDefault(6) ?? "";
    public string V7 => V.ElementAtOrDefault(7) ?? "";
    public string V8 => V.ElementAtOrDefault(8) ?? "";
    public string V9 => V.ElementAtOrDefault(9) ?? "";
}

/// <summary>Relatórios da frota (custos por veículo, consumo, resultado das viagens, vencimentos): na tela e exportados em PDF, Excel e CSV.</summary>
public sealed partial class RelatorioFrotaViewModel(IContexto contexto, string titulo) : RelatorioViewModel(contexto)
{
    private TabelaRelatorio? _tabela;
    private string _parametros = "";

    public ObservableCollection<LinhaRelatorioFrota> Linhas { get; } = [];
    [ObservableProperty] private IReadOnlyList<ColunaRelatorio> _colunas = [];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1);

    public string Titulo => titulo;
    /// <summary>Os relatórios de vencimentos e de manutenções preventivas são sempre "a partir de hoje": não pedem período.</summary>
    public bool UsaPeriodo => !TabelasFrota.SemPeriodo(titulo);
    /// <summary>Relatórios que podem ser filtrados por veículo e por motorista.</summary>
    public bool UsaFiltros => TabelasFrota.UsaFiltros(titulo);
    public ObservableCollection<Opcao> FiltroVeiculos { get; } = [];
    public ObservableCollection<Opcao> FiltroMotoristas { get; } = [];
    [ObservableProperty] private Opcao? _filtroVeiculo;
    [ObservableProperty] private Opcao? _filtroMotorista;
    protected override string TituloRelatorio => titulo;
    protected override string Parametros => _parametros;
    protected override string NomeArquivo => "frota_" + titulo.ToLowerInvariant().Replace(' ', '_').Replace("í", "i").Replace("ç", "c").Replace("õ", "o").Replace("ú", "u").Replace("ê", "e");
    protected override bool Paisagem => true;
    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => _tabela is null ? [] : [_tabela];

    public async Task CarregarAsync()
    {
        if (UsaFiltros) await CarregarFiltrosAsync();
        if (!UsaPeriodo) await GerarAsync();
    }

    private Task CarregarFiltrosAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (v0, m0) = (FiltroVeiculo?.Id, FiltroMotorista?.Id);
        FiltroVeiculos.Clear(); FiltroVeiculos.Add(new Opcao(null, "(todos)"));
        foreach (var v in (await app.Frota().VeiculosAsync(empresa, usuario)).Where(v => v.Ativo)) FiltroVeiculos.Add(new Opcao(v.Id, v.Descricao));
        FiltroMotoristas.Clear(); FiltroMotoristas.Add(new Opcao(null, "(todos)"));
        foreach (var m in (await app.Frota().MotoristasAsync(empresa, usuario)).Where(m => m.Ativo)) FiltroMotoristas.Add(new Opcao(m.Id, m.Nome));
        FiltroVeiculo = FiltroVeiculos.FirstOrDefault(o => o.Id == v0) ?? FiltroVeiculos[0];
        FiltroMotorista = FiltroMotoristas.FirstOrDefault(o => o.Id == m0) ?? FiltroMotoristas[0];
    }, Permissao.FrotaRelatorios);

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = (Ext.Dia(Inicio), Ext.Dia(Fim));
        var r = await app.Frota().RelatorioAsync(empresa, titulo, ini, fim, usuario, filtro: new FiltroRelatorioFrota(FiltroVeiculo?.Id, FiltroMotorista?.Id));
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var t = r.Valor!;
        var filtros = new List<string>();
        if (UsaFiltros && FiltroVeiculo?.Id is not null) filtros.Add("Veículo: " + FiltroVeiculo.Texto);
        if (UsaFiltros && FiltroMotorista?.Id is not null) filtros.Add("Motorista: " + FiltroMotorista.Texto);
        (_tabela, _parametros) = (t, string.Join(" – ", new[] { UsaPeriodo ? $"Período de {ini:dd/MM/yyyy} a {fim:dd/MM/yyyy}" : $"Posição em {DateTime.Today:dd/MM/yyyy}" }.Concat(filtros)));
        Colunas = t.Colunas;
        Linhas.Clear();
        foreach (var l in t.Linhas)
            Linhas.Add(new LinhaRelatorioFrota([.. l.Valores.Select((v, i) => ExportacaoRelatorio.Texto(v, t.Colunas[i].Tipo))], l.Destaque));
        TemDados = t.Linhas.Count > 0;
        AtualizarPermissoesExportacao();
        Mensagem = t.Linhas.Count == 0 ? "Nada a mostrar no período." : null;
    }, Permissao.FrotaRelatorios);
}

// ---------------------------------------------------------------------------------------------- parâmetros

/// <summary>Cadastros › Parâmetros (Frota): antecedência dos avisos de vencimento e tolerância do alerta de consumo.</summary>
public sealed class ParametrosFrotaViewModel(IContexto contexto) : ParametrosViewModel(contexto)
{
    public override string Titulo => "Parâmetros da Frota";
    public override string Descricao => "Avisos de vencimento e alerta de consumo da transportadora ativa.";
    protected override Permissao PermissaoGravar => Permissao.FrotaCadastros;

    protected override IReadOnlyList<GrupoParametros> MontarGrupos() =>
    [
        new("Avisos", "Valem para o Painel da frota, para o relatório de vencimentos e para as colunas de situação das telas.",
        [
            new CampoParametro("dias", "Avisar vencimentos com antecedência de (dias)", TipoCampoParametro.Texto,
                               "Documentos, CNHs e preventivas por data passam a \"a vencer\" este número de dias antes."),
            new CampoParametro("tolerancia", "Tolerância do consumo (%)", TipoCampoParametro.Texto,
                               "A média abaixo da esperada do veículo menos esta porcentagem gera alerta. Ex.: esperada 2,5 km/l e 15% → alerta abaixo de 2,13 km/l."),
        ], [Atalho("Abrir Painel da frota", FrotaTelas.Painel), Atalho("Abrir Veículos", FrotaTelas.Veiculos)]),
        new("Manutenção", "Autorização das ordens de serviço (Manutenção › Autorização de serviços).",
        [
            new CampoParametro("limite", "Autorizar sozinhas as ordens de até (R$)", TipoCampoParametro.Texto,
                               "Ordens de serviço com valor até este limite são autorizadas automaticamente ao pedir autorização. Com 0, toda ordem precisa ser autorizada."),
        ], [Atalho("Abrir Autorização de serviços", FrotaTelas.Autorizacao)]),
        new("Rotas", "Distância estimada das rotas (Cadastros › Rotas): linha reta entre as cidades vezes o fator rodoviário.",
        [
            new CampoParametro("fator", "Fator rodoviário", TipoCampoParametro.Texto,
                               "Multiplica a distância em linha reta para chegar perto da distância pelas estradas. Padrão 1,25. Ex.: 400 km em linha reta e fator 1,25 → 500 km."),
            new CampoParametro("velocidade", "Velocidade média (km/h)", TipoCampoParametro.Texto, "Usada para estimar o tempo da rota. Padrão 60."),
        ], [Atalho("Abrir Rotas", FrotaTelas.Rotas)]),
        new("Diárias", "Valor unitário de cada tipo de diária (Operação › Diárias). Ao escolher o tipo, o valor é sugerido e pode ser alterado no lançamento.",
        [
            .. RegrasLogistica.TiposDiaria.Select(t => new CampoParametro("diaria_" + t, "Valor da diária – " + RegrasFrota.Rotulo(t), TipoCampoParametro.Texto, null)),
        ], [Atalho("Abrir Diárias", FrotaTelas.Diarias)]),
        new("Contabilização das diárias", "Contas do lançamento gerado quando a diária é paga (Operação › Diárias › Pagar): débito na despesa, crédito na conta a pagar ou no caixa. Informe as duas ou nenhuma.",
        [
            new CampoParametro("conta_despesa", "Conta de despesa das diárias (código)", TipoCampoParametro.Texto,
                               "Conta analítica de resultado. O centro de custo da diária (o da frota) vai junto. Ex.: 4.2.01.010."),
            new CampoParametro("conta_credito", "Conta de crédito (contas a pagar ou caixa)", TipoCampoParametro.Texto,
                               "Conta analítica de onde sai o valor pago. Ex.: 2.1.06.003."),
        ], [Atalho("Abrir Diárias", FrotaTelas.Diarias)]),
    ];

    protected override async Task CarregarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var c = await app.Frota().ConfiguracaoAsync(empresa);
        Campo("dias").Texto = c.DiasAviso.ToString();
        Campo("tolerancia").Texto = Formato.Num(c.ToleranciaConsumo);
        Campo("limite").Texto = Formato.Num(c.LimiteAutorizacao);
        Campo("fator").Texto = c.FatorRodoviario.ToString("0.00", Formato.PtBr);
        Campo("velocidade").Texto = Formato.Num(c.VelocidadeMedia);
        Campo("conta_despesa").Texto = c.ContaDiariaDespesa ?? "";
        Campo("conta_credito").Texto = c.ContaDiariaCredito ?? "";
        foreach (var (tipo, valor) in await app.Frota().ValoresDiariaAsync(empresa)) Campo("diaria_" + tipo).Texto = Formato.Moeda(valor);
    }

    protected override async Task GravarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        if (Ext.Int(Campo("dias").Texto) is not { } dias) { Erros.Add("Informe a antecedência em dias (número inteiro)."); return; }
        var r = await app.Frota().GravarConfiguracaoAsync(empresa, new ConfiguracaoFrota(dias, Formato.LerValor(Campo("tolerancia").Texto) ?? 0,
                                                                                                 Formato.LerValor(Campo("limite").Texto) ?? 0,
                                                                                                 Formato.LerValor(Campo("fator").Texto) ?? 0, Formato.LerValor(Campo("velocidade").Texto) ?? 0,
                                                                                                 Campo("conta_despesa").Texto, Campo("conta_credito").Texto), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var valores = RegrasLogistica.TiposDiaria.ToDictionary(t => t, t => Formato.LerValor(Campo("diaria_" + t).Texto) ?? 0);
        var rv = await app.Frota().GravarValoresDiariaAsync(empresa, valores, usuario);
        if (!rv.Sucesso) MostrarResultado(rv, "");
    }
}
