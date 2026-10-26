using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public partial class SaldoLinha : ObservableObject
{
    public required string Codigo { get; init; }
    public required string Descricao { get; init; }
    public required string Grupo { get; init; }
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _dc = "D";

    public decimal ValorDecimal => Numeros.LerValor(Valor) ?? 0;
    public IndicadorDC Indicador => Dc.Trim().ToUpperInvariant() == "C" ? IndicadorDC.C : IndicadorDC.D;
}

/// <summary>
/// Lançamentos › Saldos iniciais: implantação da empresa no sistema a partir do balanço de abertura.
/// Lista as contas patrimoniais analíticas; o lado padrão é a natureza da conta.
/// </summary>
public partial class SaldosIniciaisViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    private static readonly string[] Patrimoniais = ["01", "02", "03"];

    public ObservableCollection<SaldoLinha> Linhas { get; } = [];

    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private bool _somentePreenchidas;
    [ObservableProperty] private string _totalDebito = "0,00";
    [ObservableProperty] private string _totalCredito = "0,00";
    [ObservableProperty] private string _diferenca = "0,00";
    [ObservableProperty] private bool _balanceado;

    private readonly List<SaldoLinha> _todas = [];

    public async Task CarregarAsync()
    {
        var geracao = NovaGeracao();
        IReadOnlyList<ContaDetalhe> plano = [];
        if (Contexto.App is { } app && Contexto.EmpresaId is { } empresa && Contexto.Sessao is { } sessao)
            plano = await app.PlanoContas.ListarAsync(empresa, sessao.Id);
        if (!EhAtual(geracao)) return;
        foreach (var l in _todas) l.PropertyChanged -= AoMudar;
        _todas.Clear();
        Linhas.Clear();
        foreach (var c in plano.Where(c => c.Analitica && c.Ativa && Patrimoniais.Contains(c.CodNat)))
        {
            var l = new SaldoLinha { Codigo = c.Codigo, Descricao = c.Descricao, Grupo = c.CodNat, Dc = c.NaturezaSaldo.ToString() };
            l.PropertyChanged += AoMudar;
            _todas.Add(l);
            if (!SomentePreenchidas) Linhas.Add(l);
        }
        Recalcular();
    }

    partial void OnSomentePreenchidasChanged(bool value)
    {
        Linhas.Clear();
        foreach (var l in _todas.Where(l => !value || l.ValorDecimal != 0)) Linhas.Add(l);
    }

    private void AoMudar(object? s, PropertyChangedEventArgs e) => Recalcular();

    private void Recalcular()
    {
        var d = _todas.Where(l => l.Indicador == IndicadorDC.D).Sum(l => l.ValorDecimal);
        var c = _todas.Where(l => l.Indicador == IndicadorDC.C).Sum(l => l.ValorDecimal);
        (TotalDebito, TotalCredito, Diferenca, Balanceado) = (Numeros.Moeda(d), Numeros.Moeda(c), Numeros.Moeda(d - c), d == c && d > 0);
    }

    [RelayCommand]
    private Task LancarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var invalidas = _todas.Where(l => l.Valor.Trim().Length > 0 && Numeros.LerValor(l.Valor) is null).Select(l => l.Codigo).ToArray();
        if (invalidas.Length > 0) { Erros.Add($"Valor inválido nas contas: {string.Join(", ", invalidas)}."); return; }
        var r = await app.Lancamentos.LancarSaldosIniciaisAsync(empresa, (int)(Ano ?? DateTime.Today.Year),
            _todas.Where(l => l.ValorDecimal != 0).Select(l => new SaldoInicialConta(l.Codigo, l.ValorDecimal, l.Indicador)).ToArray(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"Saldos iniciais lançados (lançamento nº {r.Valor.Numero}). Para corrigir, estorne-o e lance novamente.";
    }, Permissao.SaldosIniciais);
}
