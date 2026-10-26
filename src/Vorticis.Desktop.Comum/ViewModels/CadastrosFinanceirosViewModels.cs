using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Financeiro;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record OpcaoContaBancaria(long? Id, string Texto);

public sealed record OpcaoMeio(MeioPagamento Meio, string Texto);

/// <summary>Cadastros › Contas bancárias (Contábil, Fiscal e ERP): banco, agência, conta e a conta contábil do banco.</summary>
public partial class ContasBancariasViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<ContaBancaria> Lista { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private ContaBancaria? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _banco = "";
    [ObservableProperty] private string _agencia = "";
    [ObservableProperty] private string _conta = "";
    [ObservableProperty] private string _contaContabil = "";
    [ObservableProperty] private bool _ativa = true;

    public bool TemSelecionado => Selecionado is not null;
    public override bool PodeManter => true;   // o serviço confere a permissão ao gravar
    protected override string NomeRegistro => "conta bancária";
    protected override string? DescricaoSelecionado => Selecionado?.Resumo;

    public void AtualizarPermissoes() => AtualizarBarra();

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var lista = await app.CadastrosFinanceiros().ContasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        var id = Selecionado?.Id;
        Lista.Clear();
        foreach (var c in lista) Lista.Add(c);
        Selecionado = Lista.FirstOrDefault(c => c.Id == id);
    }

    partial void OnSelecionadoChanged(ContaBancaria? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado() =>
        (Codigo, Descricao, Banco, Agencia, Conta, ContaContabil, Ativa) = Selecionado is { } c
            ? (c.Codigo.ToString(), c.Descricao, c.Banco, c.Agencia, c.Conta, c.ContaContabil ?? "", c.Ativa)
            : ("", "", "", "", "", "", true);

    protected override void PrepararInclusao() =>
        (Codigo, Descricao, Banco, Agencia, Conta, ContaContabil, Ativa) =
            (ServicoCadastrosFinanceiros.ProximoCodigo(Lista.Select(c => c.Codigo)).ToString(), "", "", "", "", "", true);

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (!int.TryParse(Codigo.Trim(), out var cod)) { Erros.Add("Informe o código (número)."); return; }
            var c = new ContaBancaria(inclusao ? 0 : Selecionado?.Id ?? 0, cod, Descricao, Banco, Agencia, Conta, ContaContabil, Ativa);
            var r = await app.CadastrosFinanceiros().GravarContaAsync(empresa, c, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(x => x.Id == r.Valor);
            Mensagem = inclusao ? "Conta bancária incluída." : "Conta bancária alterada.";
        });
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.CadastrosFinanceiros().ExcluirContaAsync(empresa, Selecionado.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Conta bancária excluída.";
        });
        return ok;
    }
}

/// <summary>Linha da lista de formas de pagamento (com a condição e a conta bancária por extenso).</summary>
public sealed record FormaPagamentoLinha(FormaPagamento Forma, string Conta)
{
    public int Codigo => Forma.Codigo;
    public string Descricao => Forma.Descricao;
    public string Meio => Forma.MeioTexto;
    public string Condicao => Forma.CondicaoTexto;
    public int Parcelas => Forma.Dias.Count;
    public string Intervalo => Forma.Intervalo is { } i ? $"{i} dias" : Forma.Dias.Count < 2 ? "—" : "variável";
    public string Prazos => Forma.Prazos;
    public string Situacao => Forma.Ativa ? "Ativa" : "Inativa";
}

/// <summary>
/// Cadastros › Formas de pagamento (Fiscal e ERP): meio e prazos das parcelas ("0" à vista; "30/60/90" três parcelas).
/// Na digitação da nota fiscal, escolher a forma gera as duplicatas.
/// </summary>
public partial class FormasPagamentoViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<FormaPagamentoLinha> Lista { get; } = [];
    public ObservableCollection<OpcaoContaBancaria> Contas { get; } = [new(null, "(nenhuma)")];
    public IReadOnlyList<OpcaoMeio> Meios { get; } = FormaPagamento.Meios.Select(m => new OpcaoMeio(m.Meio, m.Texto)).ToList();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private FormaPagamentoLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private OpcaoMeio? _meio;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Exemplo))] private string _prazos = "0";
    [ObservableProperty] private decimal? _parcelas = 1;
    [ObservableProperty] private decimal? _primeiroVencimento = 0;
    [ObservableProperty] private decimal? _intervalo = 30;
    private bool _sincronizando;

    // Parcelas, primeiro vencimento e intervalo montam os prazos ("30/60/90"); digitar os prazos à mão atualiza os três campos.
    partial void OnParcelasChanged(decimal? value) => MontarPrazos();
    partial void OnPrimeiroVencimentoChanged(decimal? value) => MontarPrazos();
    partial void OnIntervaloChanged(decimal? value) => MontarPrazos();

    private void MontarPrazos()
    {
        if (_sincronizando) return;
        _sincronizando = true;
        Prazos = FormaPagamento.MontarPrazos((int)(Parcelas ?? 1), (int)(PrimeiroVencimento ?? 0), (int)(Intervalo ?? 0));
        _sincronizando = false;
    }

    partial void OnPrazosChanged(string value)
    {
        if (_sincronizando || FormaPagamento.NormalizarPrazos(value) is null) return;
        var f = new FormaPagamento(0, 1, "x", MeioPagamento.Outros, value, null);
        _sincronizando = true;
        (Parcelas, PrimeiroVencimento) = (f.Dias.Count, f.Dias[0]);
        if (f.Intervalo is { } i) Intervalo = i;
        _sincronizando = false;
    }
    [ObservableProperty] private OpcaoContaBancaria? _conta;
    [ObservableProperty] private bool _ativa = true;

    public bool TemSelecionado => Selecionado is not null;
    public override bool PodeManter => true;   // o serviço confere a permissão ao gravar
    protected override string NomeRegistro => "forma de pagamento";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Descricao}" : null;

    /// <summary>Simulação com R$ 1.000,00 emitidos hoje, para conferir os prazos digitados.</summary>
    public string Exemplo
    {
        get
        {
            var f = new FormaPagamento(0, 1, "x", MeioPagamento.Outros, Prazos, null);
            if (f.Validar().Count > 0) return "Prazos inválidos: use os dias separados por barra (ex.: 0, 30 ou 30/60/90).";
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            return "Exemplo (R$ 1.000,00 hoje): " + string.Join("; ", f.GerarParcelas(1000m, hoje).Select(p => $"{p.Numero} em {p.Vencimento:dd/MM/yyyy} = {p.Valor:N2}"));
        }
    }

    public void AtualizarPermissoes() => AtualizarBarra();

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var svc = app.CadastrosFinanceiros();
        var contas = await svc.ContasAsync(empresa, sessao.Id);
        var formas = await svc.FormasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        Contas.Clear();
        Contas.Add(new(null, "(nenhuma)"));
        foreach (var c in contas.Where(c => c.Ativa)) Contas.Add(new(c.Id, $"{c.Codigo} – {c.Descricao}"));
        var porId = contas.ToDictionary(c => c.Id);
        var id = Selecionado?.Forma.Id;
        Lista.Clear();
        foreach (var f in formas)
            Lista.Add(new FormaPagamentoLinha(f, f.ContaBancariaId is { } cb && porId.TryGetValue(cb, out var c) ? c.Descricao : ""));
        Selecionado = Lista.FirstOrDefault(l => l.Forma.Id == id);
    }

    partial void OnSelecionadoChanged(FormaPagamentoLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        if (Selecionado?.Forma is { } f)
        {
            (Codigo, Descricao, Prazos, Ativa) = (f.Codigo.ToString(), f.Descricao, f.Prazos, f.Ativa);
            Meio = Meios.FirstOrDefault(m => m.Meio == f.Meio);
            Conta = Contas.FirstOrDefault(c => c.Id == f.ContaBancariaId) ?? Contas[0];
        }
        else
        {
            (Codigo, Descricao, Prazos, Ativa) = ("", "", "0", true);
            (Meio, Conta) = (null, Contas[0]);
        }
    }

    protected override void PrepararInclusao()
    {
        (Codigo, Descricao, Prazos, Ativa) = (ServicoCadastrosFinanceiros.ProximoCodigo(Lista.Select(l => l.Codigo)).ToString(), "", "0", true);
        (Meio, Conta) = (Meios.First(m => m.Meio == MeioPagamento.Boleto), Contas[0]);
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (!int.TryParse(Codigo.Trim(), out var cod)) { Erros.Add("Informe o código (número)."); return; }
            var f = new FormaPagamento(inclusao ? 0 : Selecionado?.Forma.Id ?? 0, cod, Descricao, Meio?.Meio ?? MeioPagamento.Outros, Prazos, Conta?.Id, Ativa);
            var r = await app.CadastrosFinanceiros().GravarFormaAsync(empresa, f, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(x => x.Forma.Id == r.Valor);
            Mensagem = inclusao ? "Forma de pagamento incluída." : "Forma de pagamento alterada.";
        });
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.CadastrosFinanceiros().ExcluirFormaAsync(empresa, Selecionado.Forma.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Forma de pagamento excluída.";
        });
        return ok;
    }
}
