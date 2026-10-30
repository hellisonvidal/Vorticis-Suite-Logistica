using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Fiscal.Desktop.Servicos;
using Vorticis.Domain.Fiscal.Regras;
using Vorticis.Contabil.Desktop.Servicos;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed partial class RegraFiscalPopupViewModel : ObservableObject
{
    private readonly IRegraFiscalServico _servico;
    private readonly IPlanoContasServico _planoContasServico;
    private readonly RegraFiscalDto? _original;

    public RegraFiscalPopupViewModel(IRegraFiscalServico servico, IPlanoContasServico planoContasServico, RegraFiscalDto? original = null)
    {
        _servico = servico;
        _planoContasServico = planoContasServico;
        _original = original;

        if (original != null)
        {
            Id = original.Id;
            Nome = original.Nome;
            Descricao = original.Descricao;
            TipoOperacao = original.TipoOperacao;
            Cfop = original.Cfop;
            CstIcms = original.CstIcms;
            CstIpi = original.CstIpi;
            CstPis = original.CstPis;
            CstCofins = original.CstCofins;
            AliquotaIcms = original.AliquotaIcms;
            AliquotaIpi = original.AliquotaIpi;
            AliquotaPis = original.AliquotaPis;
            AliquotaCofins = original.AliquotaCofins;
            ReducaoBaseIcms = original.ReducaoBaseIcms;
            ReducaoBaseIcmsSt = original.ReducaoBaseIcmsSt;
            MvaSt = original.MvaSt;
            ContabilizacaoDebitoId = original.ContabilizacaoDebitoId;
            ContabilizacaoCreditoId = original.ContabilizacaoCreditoId;
            ContabilizacaoDebitoStId = original.ContabilizacaoDebitoStId;
            ContabilizacaoCreditoStId = original.ContabilizacaoCreditoStId;
            ContabilizacaoIpiId = original.ContabilizacaoIpiId;
            ContabilizacaoPisId = original.ContabilizacaoPisId;
            ContabilizacaoCofinsId = original.ContabilizacaoCofinsId;
            Ativa = original.Ativa;
        }

        SalvarCommand = new AsyncRelayCommand(SalvarAsync, PodeSalvar);
        AbrirPlanoContasCommand = new AsyncRelayCommand<string?>(AbrirPlanoContasAsync);
    }

    [ObservableProperty] private Guid _id = Guid.Empty;
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private TipoOperacaoFiscal _tipoOperacao = TipoOperacaoFiscal.Saida;
    [ObservableProperty] private string _cfop = "";
    [ObservableProperty] private string _cstIcms = "";
    [ObservableProperty] private string _cstIpi = "";
    [ObservableProperty] private string _cstPis = "";
    [ObservableProperty] private string _cstCofins = "";
    [ObservableProperty] private decimal _aliquotaIcms;
    [ObservableProperty] private decimal _aliquotaIpi;
    [ObservableProperty] private decimal _aliquotaPis;
    [ObservableProperty] private decimal _aliquotaCofins;
    [ObservableProperty] private decimal _reducaoBaseIcms;
    [ObservableProperty] private decimal _reducaoBaseIcmsSt;
    [ObservableProperty] private decimal _mvaSt;

    [ObservableProperty] private Guid? _contabilizacaoDebitoId;
    [ObservableProperty] private Guid? _contabilizacaoCreditoId;
    [ObservableProperty] private Guid? _contabilizacaoDebitoStId;
    [ObservableProperty] private Guid? _contabilizacaoCreditoStId;
    [ObservableProperty] private Guid? _contabilizacaoIpiId;
    [ObservableProperty] private Guid? _contabilizacaoPisId;
    [ObservableProperty] private Guid? _contabilizacaoCofinsId;

    [ObservableProperty] private string _contabilizacaoDebitoDescricao = "";
    [ObservableProperty] private string _contabilizacaoCreditoDescricao = "";
    [ObservableProperty] private string _contabilizacaoDebitoStDescricao = "";
    [ObservableProperty] private string _contabilizacaoCreditoStDescricao = "";
    [ObservableProperty] private string _contabilizacaoIpiDescricao = "";
    [ObservableProperty] private string _contabilizacaoPisDescricao = "";
    [ObservableProperty] private string _contabilizacaoCofinsDescricao = "";

    [ObservableProperty] private bool _ativa = true;
    [ObservableProperty] private bool _carregando;

    public IAsyncRelayCommand SalvarCommand { get; }
    public IAsyncRelayCommand<string?> AbrirPlanoContasCommand { get; }

    public IReadOnlyList<CstItem> CstsIcms => CstCatalog.Icms;
    public IReadOnlyList<CstItem> CstsIpi => CstCatalog.Ipi;
    public IReadOnlyList<CstItem> CstsPis => CstCatalog.Pis;
    public IReadOnlyList<CstItem> CstsCofins => CstCatalog.Cofins;

    private bool PodeSalvar() => !string.IsNullOrWhiteSpace(Nome) && !string.IsNullOrWhiteSpace(Cfop);

    private async Task SalvarAsync()
    {
        Carregando = true;
        try
        {
            var dto = new RegraFiscalDto
            {
                Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
                Nome = Nome,
                Descricao = Descricao,
                TipoOperacao = TipoOperacao,
                Cfop = Cfop,
                CstIcms = CstIcms,
                CstIpi = CstIpi,
                CstPis = CstPis,
                CstCofins = CstCofins,
                AliquotaIcms = AliquotaIcms,
                AliquotaIpi = AliquotaIpi,
                AliquotaPis = AliquotaPis,
                AliquotaCofins = AliquotaCofins,
                ReducaoBaseIcms = ReducaoBaseIcms,
                ReducaoBaseIcmsSt = ReducaoBaseIcmsSt,
                MvaSt = MvaSt,
                ContabilizacaoDebitoId = ContabilizacaoDebitoId,
                ContabilizacaoCreditoId = ContabilizacaoCreditoId,
                ContabilizacaoDebitoStId = ContabilizacaoDebitoStId,
                ContabilizacaoCreditoStId = ContabilizacaoCreditoStId,
                ContabilizacaoIpiId = ContabilizacaoIpiId,
                ContabilizacaoPisId = ContabilizacaoPisId,
                ContabilizacaoCofinsId = ContabilizacaoCofinsId,
                Ativa = Ativa
            };

            if (_original == null)
                await _servico.CriarAsync(dto);
            else
                await _servico.AtualizarAsync(dto);

            DialogHost.Close("RootDialog", true);
        }
        finally { Carregando = false; }
    }

    private async Task AbrirPlanoContasAsync(string? campo)
    {
        var vm = new PlanoContasPopupViewModel(_planoContasServico, null, true);
        var resultado = await DialogHost.Show(vm, "RootDialog");
        if (resultado is PlanoContaDto conta)
        {
            AtribuirConta(campo, conta);
        }
    }

    private void AtribuirConta(string? campo, PlanoContaDto conta)
    {
        switch (campo)
        {
            case "Debito":
                ContabilizacaoDebitoId = conta.Id;
                ContabilizacaoDebitoDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
            case "Credito":
                ContabilizacaoCreditoId = conta.Id;
                ContabilizacaoCreditoDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
            case "DebitoSt":
                ContabilizacaoDebitoStId = conta.Id;
                ContabilizacaoDebitoStDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
            case "CreditoSt":
                ContabilizacaoCreditoStId = conta.Id;
                ContabilizacaoCreditoStDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
            case "Ipi":
                ContabilizacaoIpiId = conta.Id;
                ContabilizacaoIpiDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
            case "Pis":
                ContabilizacaoPisId = conta.Id;
                ContabilizacaoPisDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
            case "Cofins":
                ContabilizacaoCofinsId = conta.Id;
                ContabilizacaoCofinsDescricao = $"{conta.Codigo} - {conta.Nome}";
                break;
        }
    }

    [RelayCommand]
    private void LimparConta(string campo)
    {
        switch (campo)
        {
            case "Debito":
                ContabilizacaoDebitoId = null; ContabilizacaoDebitoDescricao = ""; break;
            case "Credito":
                ContabilizacaoCreditoId = null; ContabilizacaoCreditoDescricao = ""; break;
            case "DebitoSt":
                ContabilizacaoDebitoStId = null; ContabilizacaoDebitoStDescricao = ""; break;
            case "CreditoSt":
                ContabilizacaoCreditoStId = null; ContabilizacaoCreditoStDescricao = ""; break;
            case "Ipi":
                ContabilizacaoIpiId = null; ContabilizacaoIpiDescricao = ""; break;
            case "Pis":
                ContabilizacaoPisId = null; ContabilizacaoPisDescricao = ""; break;
            case "Cofins":
                ContabilizacaoCofinsId = null; ContabilizacaoCofinsDescricao = ""; break;
        }
    }
}