using CommunityToolkit.Mvvm.ComponentModel;
using Vorticis.Application.Cadastros;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Campos da seção "Procuração eletrônica" do cadastro do contador da empresa (é procurador, vigência, abrangência e observação).</summary>
public partial class ProcuracaoEditor : ObservableObject
{
    [ObservableProperty] private bool _procurador;
    [ObservableProperty] private DateTime? _inicio;
    [ObservableProperty] private DateTime? _fim;
    [ObservableProperty] private bool _ecd = true;
    [ObservableProperty] private bool _ecf = true;
    [ObservableProperty] private bool _efd;
    [ObservableProperty] private bool _esocial = true;
    [ObservableProperty] private string _observacao = "";

    /// <summary>Valores iniciais de um novo cadastro: não é procurador; vigência desde 1º de janeiro do ano e abrangência ECD, ECF e eSocial.</summary>
    public void Limpar()
        => (Procurador, Inicio, Fim, Ecd, Ecf, Efd, Esocial, Observacao) = (false, new DateTime(DateTime.Today.Year, 1, 1), null, true, true, false, true, "");

    public void Carregar(Procuracao? p)
    {
        if (p is not { Procurador: true }) { Limpar(); return; }
        var a = p.Abrangencia;
        (Procurador, Inicio, Fim, Observacao) = (true, p.Inicio?.ToDateTime(TimeOnly.MinValue), p.Fim?.ToDateTime(TimeOnly.MinValue), p.Observacao ?? "");
        (Ecd, Ecf, Efd, Esocial) = (a.Contains(AbrangenciaProcuracao.Ecd), a.Contains(AbrangenciaProcuracao.Ecf), a.Contains(AbrangenciaProcuracao.Efd), a.Contains(AbrangenciaProcuracao.Esocial));
    }

    public Procuracao ParaProcuracao()
    {
        var abrangencia = new List<string>();
        if (Ecd) abrangencia.Add(AbrangenciaProcuracao.Ecd);
        if (Ecf) abrangencia.Add(AbrangenciaProcuracao.Ecf);
        if (Efd) abrangencia.Add(AbrangenciaProcuracao.Efd);
        if (Esocial) abrangencia.Add(AbrangenciaProcuracao.Esocial);
        return new Procuracao(Procurador, Inicio is { } i ? DateOnly.FromDateTime(i) : null, Fim is { } f ? DateOnly.FromDateTime(f) : null, abrangencia, Observacao);
    }
}
