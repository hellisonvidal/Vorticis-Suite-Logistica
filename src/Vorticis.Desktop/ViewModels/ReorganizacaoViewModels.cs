using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

/// <summary>Lançamentos › Contábeis › Reorganizar lançamentos: renumera os lançamentos do exercício sem lacunas (confirmação + senha de administrador).</summary>
public partial class ReorganizarLancamentosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    public bool PodeReorganizar => Contexto.Pode(Permissao.PeriodoFechar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeReorganizar));

    [RelayCommand]
    private Task ReorganizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ano = (int)(Ano ?? DateTime.Today.Year);
        var senha = await Contexto.SolicitarSenhaAsync("Reorganizar lançamentos",
            $"Os lançamentos efetivados de {ano} serão RENUMERADOS em sequência (1, 2, 3...), por data e número atual, preenchendo lacunas. " +
            "O conteúdo dos lançamentos não muda, mas os números impressos em relatórios já emitidos deixam de valer. A operação fica na auditoria.\n\n" +
            "Para confirmar, informe a senha de administrador:", "Reorganizar", "Cancelar");
        if (senha is null) return;
        var r = await app.Reorganizacao.ReorganizarLancamentosAsync(empresa, ano, senha, usuario);
        MostrarResultado(r, r.Sucesso ? (r.Valor.Renumerados == 0 ? $"A numeração de {ano} já estava em ordem ({r.Valor.Total} lançamento(s))."
            : $"{r.Valor.Renumerados} de {r.Valor.Total} lançamento(s) de {ano} renumerado(s).") : "");
    }, Permissao.PeriodoFechar);
}

/// <summary>Lançamentos › Fechamentos › Reorganizar saldos: refaz saldo_mensal a partir das partidas e mostra o antes/depois do que mudou.</summary>
public partial class ReorganizarSaldosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    public ObservableCollection<AlteracaoSaldoView> Alteracoes { get; } = [];
    public bool PodeReorganizar => Contexto.Pode(Permissao.PeriodoFechar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeReorganizar));

    [RelayCommand]
    private Task ReorganizarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ano = (int)(Ano ?? DateTime.Today.Year);
        Alteracoes.Clear();
        if (!await Contexto.ConfirmarAsync("Reorganizar saldos",
                $"Os saldos mensais de {ano} serão recalculados a partir das partidas dos lançamentos. As contas corrigidas serão listadas com os valores antes e depois.",
                "Reorganizar", "Cancelar")) return;
        var r = await app.Reorganizacao.ReorganizarSaldosAsync(empresa, ano, usuario);
        if (r.Sucesso) foreach (var a in r.Valor) Alteracoes.Add(new AlteracaoSaldoView(a));
        MostrarResultado(r, r.Sucesso ? (r.Valor.Count == 0 ? $"Os saldos de {ano} já conferem com as partidas: nada a corrigir."
            : $"{r.Valor.Count} saldo(s) de conta/mês corrigido(s) em {ano}.") : "");
    }, Permissao.PeriodoFechar);
}

public sealed class AlteracaoSaldoView(AlteracaoSaldo a)
{
    public string Conta { get; } = $"{a.CodigoConta} {a.DescricaoConta}";
    public string Competencia { get; } = a.Competencia.ToString("MM/yyyy");
    public string Antes { get; } = $"D {Formato.Moeda(a.DebitoAntes)}  C {Formato.Moeda(a.CreditoAntes)}";
    public string Depois { get; } = $"D {Formato.Moeda(a.DebitoDepois)}  C {Formato.Moeda(a.CreditoDepois)}";
}
