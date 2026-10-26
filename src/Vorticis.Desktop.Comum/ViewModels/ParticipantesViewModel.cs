using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record ParticipanteLinha(Participante P)
{
    public string Codigo => P.Codigo;
    public string Nome => P.Nome;
    public string Documento => P.CpfCnpj switch
    {
        { Length: 11 } d => Domain.Cadastros.Cpf.Formatar(d),
        { Length: 14 } d => Domain.Cadastros.Cnpj.Formatar(d),
        var d => d ?? "",
    };
    public string Cidade => string.Join("/", new[] { P.Municipio, P.Uf }.Where(s => !string.IsNullOrEmpty(s)));
    public string Tipo => P.Cliente && P.Fornecedor ? "Cliente e fornecedor" : P.Cliente ? "Cliente" : "Fornecedor";
    public string Situacao => P.Ativo ? "" : "Inativo";
    /// <summary>Segunda linha da lista: cidade, tipo e situação.</summary>
    public string Detalhe => string.Concat(new[] { Cidade, Tipo, Situacao }.Where(s => s.Length > 0).Select(s => "  ·  " + s));
}

/// <summary>Cadastros › Clientes.</summary>
public sealed class ClientesViewModel(IContexto contexto) : ParticipantesViewModel(contexto, fornecedores: false);

/// <summary>Cadastros › Fornecedores.</summary>
public sealed class FornecedoresViewModel(IContexto contexto) : ParticipantesViewModel(contexto, fornecedores: true);

/// <summary>
/// Cadastro de clientes ou de fornecedores (a mesma pessoa pode ser os dois). Os dados podem vir da base pública
/// do CNPJ, como no cadastro de empresas.
/// </summary>
public abstract partial class ParticipantesViewModel(IContexto contexto, bool fornecedores) : CadastroViewModel(contexto)
{
    private IReadOnlyList<Participante> _todos = [];

    public bool Fornecedores { get; } = fornecedores;
    public ObservableCollection<ParticipanteLinha> Lista { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private ParticipanteLinha? _selecionado;
    [ObservableProperty] private string _filtro = "";

    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _nomeFantasia = "";
    [ObservableProperty] private string _cpfCnpj = "";
    [ObservableProperty] private string _inscricaoEstadual = "";
    [ObservableProperty] private string _uf = "";
    [ObservableProperty] private string _municipioIbge = "";
    [ObservableProperty] private string _municipio = "";
    [ObservableProperty] private string _cep = "";
    [ObservableProperty] private string _logradouro = "";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _complemento = "";
    [ObservableProperty] private string _bairro = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private bool _cliente = true;
    [ObservableProperty] private bool _fornecedor;
    [ObservableProperty] private string _conta = "";
    [ObservableProperty] private bool _ativo = true;
    /// <summary>Alocado como participante dentro da conta contábil (plano com participantes).</summary>
    [ObservableProperty] private bool _controleConta;
    [ObservableProperty] private bool _usaParticipantes;

    public string TituloLista => Fornecedores ? "Fornecedores cadastrados" : "Clientes cadastrados";
    public string TituloFormulario => (Fornecedores ? "Fornecedor" : "Cliente") + (EmInclusao ? " — inclusão" : Modo == ModoCadastro.Alteracao ? " — alteração" : "");
    public string NumeroRegistro => EmInclusao ? "Cód. (automático)" : Selecionado is { } s ? $"Cód. {s.Codigo}" : "";
    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.LancamentoIncluir);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => Fornecedores ? "fornecedor" : "cliente";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Nome}" : null;
    protected override string DetalheExclusao => "\n\nQuem já aparece em lançamentos não é excluído.";

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var lista = await app.Participantes.ListarAsync(empresa, sessao.Id);
        var usa = await app.Participantes.UsaParticipantesAsync(empresa);
        if (!EhAtual(g)) return;
        UsaParticipantes = usa;
        _todos = lista;
        Filtrar();
    }

    partial void OnFiltroChanged(string value) => Filtrar();

    private void Filtrar()
    {
        var id = Selecionado?.P.Id;
        var f = Filtro.Trim();
        var digitos = Domain.Cadastros.Cnpj.SomenteDigitos(f);
        Lista.Clear();
        foreach (var p in _todos.Where(p => (Fornecedores ? p.Fornecedor : p.Cliente)
                                            && (f.Length == 0 || p.Codigo == f || p.Nome.Contains(f, StringComparison.CurrentCultureIgnoreCase)
                                                || (digitos.Length >= 3 && (p.CpfCnpj ?? "").Contains(digitos)))))
            Lista.Add(new ParticipanteLinha(p));
        Selecionado = Lista.FirstOrDefault(l => l.P.Id == id);
    }

    protected override void AoMudarModo()
    {
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(NumeroRegistro));
    }

    partial void OnSelecionadoChanged(ParticipanteLinha? value)
    {
        OnPropertyChanged(nameof(NumeroRegistro));
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        var p = Selecionado?.P;
        (Codigo, Nome, NomeFantasia, CpfCnpj, InscricaoEstadual) = (p?.Codigo ?? "", p?.Nome ?? "", p?.NomeFantasia ?? "", Selecionado?.Documento ?? "", p?.InscricaoEstadual ?? "");
        (Uf, MunicipioIbge, Municipio, Email, Telefone) = (p?.Uf ?? "", p?.CodMunicipioIbge ?? "", p?.Municipio ?? "", p?.Email ?? "", p?.Telefone ?? "");
        (Cep, Logradouro, Numero, Complemento, Bairro) = (Domain.Comum.Formato.Cep(p?.Cep), p?.Logradouro ?? "", p?.Numero ?? "", p?.Complemento ?? "", p?.Bairro ?? "");
        (Cliente, Fornecedor, Conta, Ativo, ControleConta) =
            (p?.Cliente ?? !Fornecedores, p?.Fornecedor ?? Fornecedores, p?.CodigoConta ?? "", p?.Ativo ?? true, p?.ControleConta ?? false);
    }

    protected override void PrepararInclusao()
    {
        Selecionado = null;
        MostrarSelecionado();
        Codigo = ServicoParticipantes.ProximoCodigo(_todos);
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var id = inclusao ? 0 : Selecionado?.P.Id ?? 0;
            if (!inclusao && id == 0) { Erros.Add("Selecione um cadastro."); return; }
            var r = await app.Participantes.GravarAsync(empresa, new Participante(id, Codigo, Nome, NomeFantasia, CpfCnpj, InscricaoEstadual, Uf,
                MunicipioIbge, Municipio, Email, Telefone, Cliente, Fornecedor, Conta, Ativo, false, Cep, Logradouro, Numero, Complemento, Bairro, ControleConta), usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.P.Id == r.Valor);
            Mensagem = inclusao ? $"{Nome.Trim()} incluído (código {Selecionado?.Codigo})." : "Cadastro alterado.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }

    /// <summary>
    /// Buscar dados na Receita (como no cadastro de empresas): pelo CNPJ preenche nome, fantasia, endereço, e-mail e
    /// telefone, e procura a inscrição estadual na UF (sem inscrição encontrada → ISENTO). Grava só ao Salvar.
    /// </summary>
    [RelayCommand]
    private async Task BuscarCnpjAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (!EmEdicao) { Erros.Add("Clique em Incluir ou Alterar antes de buscar os dados."); return; }
        if (Contexto.App is not { } app) return;
        var cnpj = Domain.Cadastros.Cnpj.SomenteDigitos(CpfCnpj);
        if (cnpj.Length != 14) { Erros.Add("A busca na Receita é feita pelo CNPJ (14 dígitos)."); return; }
        try
        {
            Ocupado = true;
            var r = await app.Empresas.ConsultarCnpjAsync(cnpj);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            var d = r.Valor;
            (Nome, NomeFantasia, CpfCnpj) = (d.RazaoSocial, d.NomeFantasia ?? "", Domain.Cadastros.Cnpj.Formatar(d.Cnpj));
            (Uf, MunicipioIbge, Municipio, Email, Telefone) = (d.Uf ?? Uf, d.CodMunicipioIbge ?? MunicipioIbge, d.Municipio ?? Municipio,
                                                                d.Email ?? Email, d.Telefone ?? Telefone);
            (Cep, Logradouro, Numero, Complemento, Bairro) = (Domain.Comum.Formato.Cep(d.Cep ?? Domain.Cadastros.Cnpj.SomenteDigitos(Cep)), d.Logradouro ?? Logradouro,
                                                             d.Numero ?? Numero, d.Complemento ?? Complemento, d.Bairro ?? Bairro);
            var ie = await app.Empresas.ConsultarInscricaoEstadualAsync(cnpj, Uf, MunicipioIbge);
            var aviso = "";
            if (ie.Sucesso)
                InscricaoEstadual = ie.Valor.FirstOrDefault(i => i.Ativa)?.Inscricao ?? ie.Valor.FirstOrDefault()?.Inscricao ?? "ISENTO";
            else aviso = $" Inscrição estadual não consultada: {string.Join(" ", ie.Erros)}";
            Mensagem = $"Dados de {d.RazaoSocial} preenchidos (fonte: {d.Fonte}" +
                       (d.SituacaoCadastral is { } sit ? $", situação {sit}" : "") + "). Confira e clique em Salvar." + aviso;
        }
        catch (Exception ex) { Erros.Add(ex.Message); }
        finally { Ocupado = false; }
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var nome = Selecionado.Nome;
            var r = await app.Participantes.ExcluirAsync(empresa, Selecionado.P.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = $"{nome} excluído.";
        }, Permissao.LancamentoIncluir);
        return ok;
    }
}
