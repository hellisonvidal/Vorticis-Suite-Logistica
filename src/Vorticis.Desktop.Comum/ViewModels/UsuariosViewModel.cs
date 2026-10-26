using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record UsuarioLinha(UsuarioResumo Usuario)
{
    public string Numero => Usuario.Id.ToString("0000");
    public string Login => Usuario.Login;
    public string Nome => Usuario.Nome;
    public string Perfil => Usuario.Perfil ?? (Usuario.Administrador ? "Administrador" : "(definir perfil)");
    public string Situacao => Usuario.Ativo ? "Ativo" : "Inativo";
    public string UltimoAcesso => Usuario.UltimoAcesso?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "nunca";
}

public sealed record OpcaoPerfil(long? Id, string Nome, bool Administrador = false);

/// <summary>Uma empresa na lista de acesso do usuário: marcada = o usuário acessa a empresa, com o perfil escolhido.</summary>
public partial class AcessoLinha : ObservableObject
{
    public required long EmpresaId { get; init; }
    public required string Empresa { get; init; }
    public required string Codigo { get; init; }
    public required string Cnpj { get; init; }
    public required IReadOnlyList<OpcaoPerfil> Opcoes { get; init; }
    /// <summary>Perfil gravado no banco para o usuário nesta empresa (nulo = sem acesso).</summary>
    public long? Original { get; set; }
    [ObservableProperty] private bool _marcada;
    [ObservableProperty] private OpcaoPerfil _perfil = new(null, "");

    public string Rotulo => $"{Codigo} – {Empresa}";
    /// <summary>Perfil a gravar: nulo se desmarcada.</summary>
    public long? PerfilFinal => Marcada ? Perfil.Id : null;

    /// <summary>Filtro de busca: parte do nome, do código ou dos dígitos do CNPJ.</summary>
    public bool Corresponde(string filtro)
    {
        if (filtro.Length == 0) return true;
        if (Empresa.Contains(filtro, StringComparison.CurrentCultureIgnoreCase) || Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase)) return true;
        var digitos = Domain.Cadastros.Cnpj.SomenteDigitos(filtro);
        return digitos.Length > 0 && Cnpj.Contains(digitos, StringComparison.Ordinal);
    }
}

/// <summary>
/// Configurações › Usuários, aba Usuários: Incluir / Alterar / Excluir, com o perfil do usuário e a lista de empresas que ele
/// acessa (somente administrador). Na implantação (usuário vorticis) só permite incluir o primeiro administrador.
/// </summary>
public partial class UsuariosViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<UsuarioLinha> Lista { get; } = [];

    /// <summary>Todas as empresas cadastradas, com a marca de acesso do usuário em edição.</summary>
    public ObservableCollection<AcessoLinha> Acessos { get; } = [];

    /// <summary>As empresas que passam pelo filtro de busca (é o que a tela lista).</summary>
    public ObservableCollection<AcessoLinha> AcessosFiltrados { get; } = [];

    /// <summary>Perfis que podem ser atribuídos ao usuário (aba Perfis de acesso).</summary>
    public ObservableCollection<OpcaoPerfil> PerfisDisponiveis { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private UsuarioLinha? _selecionado;
    [ObservableProperty] private string _login = "";
    [ObservableProperty] private string _nomeEdicao = "";
    [ObservableProperty] private string _novaSenha = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MostrarAcessos), nameof(DescricaoPerfil))] private OpcaoPerfil? _perfilEdicao;
    [ObservableProperty] private bool _ativoEdicao = true;
    [ObservableProperty] private string _senhaTemporaria = "";
    [ObservableProperty] private string _filtroEmpresas = "";

    private bool _carregandoSelecao;

    public bool TemSelecionado => Selecionado is not null;

    /// <summary>Administradores acessam todas as empresas; para os demais, a lista de empresas liberadas (inclusão e alteração).</summary>
    public bool MostrarAcessos => !EmImplantacao && PerfilEdicao is { Administrador: false } && (EmEdicao || TemSelecionado);

    /// <summary>Sessão do usuário de implantação: só se inclui o primeiro administrador.</summary>
    public bool EmImplantacao => Contexto.Sessao?.Implantacao == true;

    public int QuantidadeMarcadas => Acessos.Count(a => a.Marcada);
    public string ResumoEmpresas => $"{QuantidadeMarcadas} de {Acessos.Count} empresa(s) liberada(s)";

    partial void OnFiltroEmpresasChanged(string value) => AplicarFiltro();

    private void AplicarFiltro()
    {
        var f = FiltroEmpresas.Trim();
        AcessosFiltrados.Clear();
        foreach (var a in Acessos.Where(a => a.Corresponde(f))) AcessosFiltrados.Add(a);
    }

    private void AtualizarResumo()
    {
        OnPropertyChanged(nameof(QuantidadeMarcadas));
        OnPropertyChanged(nameof(ResumoEmpresas));
    }

    /// <summary>Marca todas as empresas visíveis (as que passam pelo filtro).</summary>
    [RelayCommand]
    private void MarcarTodas() => MarcarVisiveis(true);

    /// <summary>Desmarca todas as empresas visíveis (as que passam pelo filtro).</summary>
    [RelayCommand]
    private void DesmarcarTodas() => MarcarVisiveis(false);

    private void MarcarVisiveis(bool marcar)
    {
        if (!EmEdicao) return;
        foreach (var a in AcessosFiltrados) a.Marcada = marcar;
    }

    public string DescricaoPerfil => PerfilEdicao switch
    {
        null => "Escolha o perfil: ele define o que o usuário poderá fazer no sistema.",
        { Administrador: true } => "Administrador: todas as funções, em todas as empresas, inclusive usuários, perfis, empresas e backup.",
        var p => $"Perfil {p.Nome}: vale nas empresas marcadas abaixo. Desmarque as empresas que o usuário não deve acessar.",
    };

    protected override string NomeRegistro => "usuário";
    protected override string? DescricaoSelecionado => Selecionado is { } u ? $"{u.Nome} ({u.Login})" : null;
    public override bool PodeManter => Contexto.Sessao is { } s && (s.Administrador || s.Implantacao);
    protected override string DetalheExclusao =>
        "\n\nO usuário é apagado do banco de dados e o login fica livre para um novo cadastro. Os lançamentos que ele fez " +
        "continuam na escrituração; a trilha de auditoria guarda o login. Para só bloquear o acesso, use Alterar e desmarque Ativo.";

    protected override void AoMudarModo() => OnPropertyChanged(nameof(MostrarAcessos));

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        var id = Selecionado?.Usuario.Id;
        var geracao = NovaGeracao();
        var usuarios = await app.Usuarios.ListarAsync(sessao);
        var perfis = (await app.Perfis.ListarAsync(sessao)).Select(p => new OpcaoPerfil(p.Id, p.Nome, p.Administrador)).ToList();
        // Implantação: o único perfil possível é o Administrador
        if (await app.Usuarios.PerfilDeImplantacaoAsync(sessao) is { } pi) perfis = [new OpcaoPerfil(pi.Id, pi.Nome, true)];
        if (!EhAtual(geracao)) return;
        PerfisDisponiveis.Clear();
        foreach (var p in perfis) PerfisDisponiveis.Add(p);
        Lista.Clear();
        foreach (var u in usuarios) Lista.Add(new UsuarioLinha(u));
        Selecionado = Lista.FirstOrDefault(l => l.Usuario.Id == id);
        AtualizarBarra();
        OnPropertyChanged(nameof(EmImplantacao));
        // Primeira instalação: já abre a inclusão do administrador, com o perfil fixo
        if (sessao.Implantacao && EmConsulta && PodeManter)
        {
            IncluirCommand.Execute(null);
            PerfilEdicao = PerfisDisponiveis.FirstOrDefault();
        }
    }

    partial void OnSelecionadoChanged(UsuarioLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        LimparAcessos();
        _carregandoSelecao = true;
        if (Selecionado is { } value)
        {
            (Login, NomeEdicao, AtivoEdicao, SenhaTemporaria, NovaSenha) = (value.Login, value.Nome, value.Usuario.Ativo, "", "");
            PerfilEdicao = PerfisDisponiveis.FirstOrDefault(p => p.Id == value.Usuario.PerfilId);
            _ = CarregarAcessosAsync(value.Usuario.Id);
        }
        else
        {
            (Login, NomeEdicao, AtivoEdicao, SenhaTemporaria, NovaSenha) = ("", "", true, "", "");
            PerfilEdicao = null;
        }
        _carregandoSelecao = false;
        OnPropertyChanged(nameof(MostrarAcessos));
    }

    private void LimparAcessos()
    {
        foreach (var a in Acessos) a.PropertyChanged -= AoMudarAcesso;
        Acessos.Clear();
        AcessosFiltrados.Clear();
        FiltroEmpresas = "";
        AtualizarResumo();
    }

    protected override void PrepararInclusao()
    {
        LimparAcessos();
        _carregandoSelecao = true;
        (Login, NomeEdicao, NovaSenha, AtivoEdicao, PerfilEdicao) = ("", "", "", true, null);
        _carregandoSelecao = false;
        // novo usuário: todas as empresas já vêm marcadas (o administrador desmarca as que não valem)
        if (!EmImplantacao) _ = CarregarAcessosAsync(null);
    }

    /// <summary>Trocar o perfil do usuário passa o novo perfil às empresas marcadas (cada uma ainda pode receber outro, ao lado).</summary>
    partial void OnPerfilEdicaoChanged(OpcaoPerfil? value)
    {
        if (_carregandoSelecao || value is null || value.Administrador) return;
        foreach (var a in Acessos.Where(a => a.Marcada)) a.Perfil = a.Opcoes.FirstOrDefault(o => o.Id == value.Id) ?? a.Perfil;
    }

    private void AoMudarAcesso(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AcessoLinha.Marcada)) AtualizarResumo();
    }

    /// <summary>
    /// Monta a lista de empresas (todas as cadastradas) com a marca do usuário. Usuário novo (<paramref name="usuarioId"/> nulo):
    /// todas marcadas com o perfil escolhido. O administrador logado vê todas as empresas.
    /// </summary>
    public async Task CarregarAcessosAsync(long? usuarioId)
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        try
        {
            var perfis = (await app.Perfis.ListarAsync(sessao)).Select(p => new OpcaoPerfil(p.Id, p.Nome, p.Administrador))
                                                               .Where(p => !p.Administrador).ToList();
            var atuais = usuarioId is long uid
                ? (await app.Usuarios.ListarAcessosAsync(uid)).ToDictionary(a => a.EmpresaId, a => a.PerfilId)
                : [];
            var todas = await app.Usuarios.EmpresasPermitidasAsync(sessao);   // administrador: todas
            var padrao = PerfilEdicao is { Administrador: false } pe ? perfis.FirstOrDefault(o => o.Id == pe.Id) : null;
            LimparAcessos();
            foreach (var e in todas)
            {
                long? atual = atuais.TryGetValue(e.Id, out var pid) ? pid : null;
                var perfil = perfis.FirstOrDefault(o => o.Id == atual) ?? padrao
                             ?? perfis.FirstOrDefault(o => o.Id == Selecionado?.Usuario.PerfilId) ?? perfis.FirstOrDefault()
                             ?? new OpcaoPerfil(null, "");
                var linha = new AcessoLinha
                {
                    EmpresaId = e.Id, Empresa = e.RazaoSocial, Codigo = e.Codigo, Cnpj = e.Cnpj, Opcoes = perfis, Original = atual,
                    Marcada = usuarioId is null || atual is not null, Perfil = perfil,
                };
                linha.PropertyChanged += AoMudarAcesso;
                Acessos.Add(linha);
            }
            AplicarFiltro();
            AtualizarResumo();
            OnPropertyChanged(nameof(MostrarAcessos));
        }
        catch (Exception ex)
        {
            Erros.Add(ex.Message);
        }
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await Admin(async (app, sessao) =>
        {
            if (inclusao)
            {
                // lista ainda não carregada (ou sem empresas): vale "todas as empresas", como antes da lista existir
                var empresas = PerfilEdicao is { Administrador: false } && Acessos.Count > 0 ? Acessos.Where(a => a.Marcada).Select(a => a.EmpresaId).ToList() : null;
                var r = await app.Usuarios.CriarUsuarioAsync(sessao, Login, NomeEdicao, NovaSenha, PerfilEdicao?.Id, empresaIds: empresas);
                MostrarResultado(r, $"Usuário {Login.Trim()} criado com o perfil {PerfilEdicao?.Nome}. Ele deverá trocar a senha no primeiro acesso.");
                if (!r.Sucesso) return;
                ok = true;
                if (sessao.Implantacao)
                {
                    // o acesso de implantação termina aqui: volta ao login para o administrador entrar
                    Modo = ModoCadastro.Consulta;
                    await Contexto.ConcluirImplantacaoAsync(Login.Trim());
                    return;
                }
                Modo = ModoCadastro.Consulta;
                NovaSenha = "";
                await CarregarAsync();
                Selecionado = Lista.FirstOrDefault(l => l.Usuario.Id == r.Valor);
                return;
            }
            if (Selecionado is null) return;
            var id = Selecionado.Usuario.Id;
            var alt = await app.Usuarios.AlterarUsuarioAsync(sessao, id, NomeEdicao, PerfilEdicao?.Id, AtivoEdicao);
            if (!alt.Sucesso) { MostrarResultado(alt, ""); return; }
            var mudancas = Acessos.Where(a => MostrarAcessos && a.PerfilFinal != a.Original).ToList();
            if (mudancas.Count > 0)
            {
                var acesso = await app.Usuarios.DefinirAcessosAsync(sessao, id, mudancas.ToDictionary(a => a.EmpresaId, a => a.PerfilFinal));
                if (!acesso.Sucesso) { MostrarResultado(acesso, ""); return; }
                foreach (var a in mudancas) a.Original = a.PerfilFinal;
            }
            ok = true;
            Modo = ModoCadastro.Consulta;
            await CarregarAsync();
            Mensagem = "Alterações salvas.";
        });
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await Admin(async (app, sessao) =>
        {
            if (Selecionado is not { } u) return;
            var r = await app.Usuarios.ExcluirUsuarioAsync(sessao, u.Usuario.Id);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = $"Usuário {u.Login} excluído.";
        });
        return ok;
    }

    [RelayCommand]
    private Task RedefinirSenhaAsync() => Admin(async (app, sessao) =>
    {
        if (Selecionado is null) return;
        var r = await app.Usuarios.RedefinirSenhaAsync(sessao, Selecionado.Usuario.Id, SenhaTemporaria);
        MostrarResultado(r, $"Senha de {Selecionado.Login} redefinida e desbloqueada. Ele deverá trocá-la no próximo acesso.");
        if (r.Sucesso) SenhaTemporaria = "";
    });

    private async Task Admin(Func<Infra.VorticisApp, SessaoUsuario, Task> acao)
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || !(sessao.Administrador || sessao.Implantacao))
        {
            Erros.Add("Somente administradores.");
            return;
        }
        try { await acao(app, sessao); }
        catch (Exception ex) { Erros.Add(ex.Message); }
    }
}
