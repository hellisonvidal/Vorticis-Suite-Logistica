using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Fechamento;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed record MesFiscalLinha(MesFiscal M)
{
    public string Mes => M.Competencia.ToString("MM/yyyy");
    public string Situacao => M.Fechado ? "Fechado" : "Aberto";
    public string Notas => M.Notas.ToString();
    public string NaoContabilizadas => M.NaoContabilizadas == 0 ? "" : M.NaoContabilizadas.ToString();
    public string Fechamento => M.Fechado ? $"{M.FechadoEm?.LocalDateTime:dd/MM/yyyy HH:mm}" + (M.FechadoPor is { } u ? $" – {u}" : "") : "";
}

/// <summary>
/// Apuração › Fechamento do período fiscal (regime normal): fecha o mês depois de escriturar e apurar; com ele fechado, as notas do mês não
/// podem ser incluídas, alteradas nem excluídas. Reabrir libera de novo.
/// </summary>
public partial class FechamentoFiscalViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<MesFiscalLinha> Meses { get; } = [];
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year;
    [ObservableProperty] private MesFiscalLinha? _selecionado;
    [ObservableProperty] private string _regime = "";

    public bool PodeManter => Contexto.Pode(Permissao.FiscalApurar);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeManter));

    partial void OnAnoChanged(decimal? value) => _ = CarregarAsync();

    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ano = (int)(Ano ?? DateTime.Today.Year);
        var r = await app.Fiscal().Fechamento.ListarAsync(empresa, ano, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var sel = Selecionado?.M.Competencia;
        Meses.Clear();
        foreach (var m in r.Valor) Meses.Add(new MesFiscalLinha(m));
        Selecionado = Meses.FirstOrDefault(m => m.M.Competencia == sel);
        var regime = await app.Fiscal().Fechamento.RegimeAsync(empresa, new DateOnly(ano, 12, 31));
        Regime = regime is null ? "Regime tributário não informado no cadastro da empresa."
               : ServicoFechamentoFiscal.RegimeNormal(regime) ? $"Regime: {Rotulo(regime)} (regime normal: o fechamento se aplica)."
               : $"Regime: {Rotulo(regime)}. O fechamento do período fiscal é para empresas do regime normal.";
    });

    private static string Rotulo(string regime) => regime switch
    {
        "SIMPLES_NACIONAL" => "Simples Nacional", "MEI" => "MEI", "LUCRO_PRESUMIDO" => "Lucro Presumido", "LUCRO_REAL" => "Lucro Real",
        "LUCRO_ARBITRADO" => "Lucro Arbitrado", _ => "Imune/isenta",
    };

    [RelayCommand]
    private Task FecharAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione o mês na lista."); return; }
        if (m.Fechado) { Erros.Add($"{Selecionado.Mes} já está fechado."); return; }
        if (!await Contexto.ConfirmarAsync("Fechar período fiscal",
                $"Fechar {Selecionado.Mes}? As notas do mês não poderão ser incluídas, alteradas nem excluídas até o mês ser reaberto." +
                (m.NaoContabilizadas > 0 ? $" Atenção: {m.NaoContabilizadas} nota(s) do mês ainda não foram contabilizadas (a contabilização continua permitida)." : ""),
                "Fechar o mês", "Cancelar")) return;
        var r = await app.Fiscal().Fechamento.FecharAsync(empresa, m.Competencia, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"Período fiscal de {m.Competencia:MM/yyyy} fechado.";
    }, Permissao.FiscalApurar);

    [RelayCommand]
    private Task ReabrirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado?.M is not { } m) { Erros.Add("Selecione o mês na lista."); return; }
        if (!await Contexto.ConfirmarAsync("Reabrir período fiscal", $"Reabrir {Selecionado.Mes}? As notas do mês voltam a poder ser alteradas.", "Reabrir", "Cancelar")) return;
        var r = await app.Fiscal().Fechamento.ReabrirAsync(empresa, m.Competencia, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"Período fiscal de {m.Competencia:MM/yyyy} reaberto.";
    }, Permissao.FiscalApurar);
}
