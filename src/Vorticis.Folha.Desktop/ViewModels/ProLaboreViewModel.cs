using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;

namespace Vorticis.Folha.Desktop.ViewModels;

public sealed record SocioProLaboreLinha(ProLaboreSocio S)
{
    public string Nome => S.Nome;
    public string Cpf => S.Cpf is { Length: 11 } c ? $"{c[..3]}.{c[3..6]}.{c[6..9]}-{c[9..]}" : S.Cpf ?? "";
    public string Qualificacao => S.Qualificacao;
    public string Retira => S.Retira ? "Sim" : "Não";
    public string Valor => S.Retira ? Formato.Moeda(S.Valor) : "";
}

public sealed record ProLaboreCalculoLinha(ProLaboreCalculo C)
{
    public string Nome => C.Nome;
    public string Bruto => Formato.Moeda(C.Bruto);
    public string Inss => Formato.Moeda(C.Inss);
    public string Irrf => Formato.Moeda(C.Irrf);
    public string Liquido => Formato.Moeda(C.Liquido);
    public string Patronal => Formato.Moeda(C.InssPatronal);
    public string Memoria => C.Memoria;
}

/// <summary>
/// Folha › Pró-labore: sócios da empresa (Cadastros › Empresas › Sócios), se retiram pró-labore e quanto, e o cálculo na
/// competência escolhida ao lado da empresa. Competência fechada: só consulta.
/// </summary>
public partial class ProLaboreViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<SocioProLaboreLinha> Socios { get; } = [];
    public ObservableCollection<ProLaboreCalculoLinha> Calculos { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private SocioProLaboreLinha? _selecionado;
    [ObservableProperty] private bool _retira;
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private DateTime? _inicio;
    [ObservableProperty] private DateTime? _fim;
    [ObservableProperty] private decimal? _dependentes = 0;
    [ObservableProperty] private string _inssOutrasFontes = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _totais = "";
    [ObservableProperty] private string _situacao = "";
    [ObservableProperty] private bool _aberta = true;

    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.FolhaCadastrosManter);
    public bool PodeCalcular => Contexto.Pode(Permissao.FolhaCalcular) && Aberta;

    partial void OnAbertaChanged(bool value) => OnPropertyChanged(nameof(PodeCalcular));

    public void AtualizarPermissoes() { OnPropertyChanged(nameof(PodeEditar)); OnPropertyChanged(nameof(PodeCalcular)); }

    partial void OnSelecionadoChanged(SocioProLaboreLinha? value)
    {
        var s = value?.S;
        Retira = s?.Retira ?? false;
        Valor = s is { Valor: > 0 } ? Formato.Num(s.Valor) : "";
        Inicio = s?.Inicio is { } i ? FolhaExtensoes.Mes(i) : FolhaExtensoes.Mes(Contexto.PeriodoTrabalho);
        Fim = s?.Fim is { } f ? FolhaExtensoes.Mes(f) : null;
        Dependentes = s?.DependentesIrrf ?? 0;
        InssOutrasFontes = s is { InssOutrasFontes: > 0 } ? Formato.Num(s.InssOutrasFontes) : "";
        Observacao = s?.Observacao ?? "";
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var competencia = Contexto.PeriodoTrabalho;
        var socios = await app.Folha().ProLabore.ListarAsync(empresa, sessao.Id);
        var calculos = await app.Folha().ProLabore.ListarCalculosAsync(empresa, competencia, sessao.Id);
        var status = await app.Folha().Folha.ObterCompetenciaAsync(empresa, competencia, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.S.SocioId;
        Socios.Clear();
        foreach (var s in socios) Socios.Add(new SocioProLaboreLinha(s));
        Selecionado = Socios.FirstOrDefault(l => l.S.SocioId == id) ?? Socios.FirstOrDefault();
        MostrarCalculos(calculos);
        Aberta = !status.Fechada;
        Situacao = status.Fechada
            ? $"Competência {competencia:MM/yyyy} FECHADA: reabra-a em Folha › Cálculo da folha mensal para calcular ou excluir."
            : $"Competência {competencia:MM/yyyy} aberta (troque ao lado da empresa).";
    }

    private void MostrarCalculos(IReadOnlyList<ProLaboreCalculo> calculos)
    {
        Calculos.Clear();
        foreach (var c in calculos) Calculos.Add(new ProLaboreCalculoLinha(c));
        Totais = calculos.Count == 0 ? "Sem cálculo nesta competência."
            : $"{calculos.Count} sócio(s): bruto {Formato.Moeda(calculos.Sum(c => c.Bruto))}, INSS {Formato.Moeda(calculos.Sum(c => c.Inss))}, "
              + $"IRRF {Formato.Moeda(calculos.Sum(c => c.Irrf))}, líquido {Formato.Moeda(calculos.Sum(c => c.Liquido))}, CPP patronal {Formato.Moeda(calculos.Sum(c => c.InssPatronal))}.";
    }

    [RelayCommand]
    private Task GravarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is not { } sel) { Erros.Add("Selecione o sócio na lista."); return; }
        var valor = Formato.LerValor(Valor) ?? 0;
        var outras = Formato.LerValor(InssOutrasFontes) ?? 0;
        var r = await app.Folha().ProLabore.GravarAsync(empresa, sel.S with
        {
            Retira = Retira, Valor = valor, Inicio = Inicio is { } i ? FolhaExtensoes.InicioMes(i) : null, Fim = FolhaExtensoes.Data(Fim) is { } f ? new DateOnly(f.Year, f.Month, 1).AddMonths(1).AddDays(-1) : null,
            DependentesIrrf = (int)(Dependentes ?? 0), InssOutrasFontes = outras, Observacao = Observacao,
        }, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        await CarregarAsync();
        Mensagem = $"Pró-labore de {sel.Nome} gravado.";
    }, Permissao.FolhaCadastrosManter);

    [RelayCommand]
    private Task CalcularAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().ProLabore.CalcularAsync(empresa, Contexto.PeriodoTrabalho, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MostrarCalculos(r.Valor);
        Mensagem = $"Pró-labore de {Contexto.PeriodoTrabalho:MM/yyyy} calculado.";
    }, Permissao.FolhaCalcular);

    [RelayCommand]
    private Task ExcluirCalculoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (!await Contexto.ConfirmarAsync("Excluir cálculo", $"Excluir o cálculo do pró-labore de {Contexto.PeriodoTrabalho:MM/yyyy}?")) return;
        var r = await app.Folha().ProLabore.ExcluirCalculosAsync(empresa, Contexto.PeriodoTrabalho, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MostrarCalculos([]);
        Mensagem = "Cálculo excluído.";
    }, Permissao.FolhaCalcular);
}
