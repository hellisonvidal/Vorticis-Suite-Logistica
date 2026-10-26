using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record DiferencaSaldoLinha(DiferencaSaldo D)
{
    public string Codigo => new string(' ', (D.Linha.Nivel - 1) * 3) + D.Linha.Codigo;
    public string Descricao => D.Linha.Descricao;
    public string SaldoAnterior => LinhaBalancete.Formatar(D.Linha.SaldoAnterior);
    public string SaldoFinal => LinhaBalancete.Formatar(D.Linha.SaldoFinal);
    public string Diferenca => D.Diferenca == 0 ? "" : (D.Diferenca > 0 ? "+" : "−") + Formato.Moeda(Math.Abs(D.Diferenca));
    public string Alerta => D.Linha.Analitica && D.Linha.SaldoInvertido ? "Saldo invertido" : "";
}

/// <summary>Consultas › Diferenças de saldos.</summary>
public partial class DiferencasSaldosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    private AnaliseDiferencas? _analise;

    public ObservableCollection<DiferencaSaldoLinha> Linhas { get; } = [];
    public ObservableCollection<string> Inconsistencias { get; } = [];
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private bool _somenteComDiferenca = true;
    [ObservableProperty] private string _resumo = "";

    partial void OnSomenteComDiferencaChanged(bool value) => Exibir();

    [RelayCommand]
    private Task AnalisarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        var r = await app.DiferencasSaldos.AnalisarAsync(empresa, ini, Data(Fim, ini), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        _analise = r.Valor;
        Exibir();
    }, Permissao.RelatorioBalancete);

    private void Exibir()
    {
        Linhas.Clear();
        Inconsistencias.Clear();
        if (_analise is not { } a) return;
        foreach (var d in a.Contas.Where(d => !SomenteComDiferenca || d.Diferenca != 0 || d.Linha.SaldoInvertido)) Linhas.Add(new DiferencaSaldoLinha(d));
        foreach (var i in a.Inconsistencias) Inconsistencias.Add(i);
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        Resumo = $"Saldos em {ini.AddDays(-1):dd/MM/yyyy} × {Data(Fim, ini):dd/MM/yyyy} · débitos {Formato.Moeda(a.TotalDebitos)} · créditos {Formato.Moeda(a.TotalCreditos)} · " +
                 (a.Inconsistencias.Count == 0 ? "✓ nenhuma inconsistência" : $"{a.Inconsistencias.Count} inconsistência(s)");
    }
}
