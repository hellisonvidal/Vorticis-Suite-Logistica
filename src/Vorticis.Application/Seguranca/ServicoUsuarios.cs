using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Seguranca;

public sealed record CredenciaisUsuario(long Id, string Login, string Nome, string SenhaHash, bool Ativo, bool Administrador,
                                        bool DeveTrocarSenha, int TentativasFalhas, DateTimeOffset? BloqueadoAte);

/// <param name="PerfilId">Perfil do usuário (vale para todas as empresas; o acesso por empresa admite exceções).</param>
public sealed record UsuarioResumo(long Id, string Login, string Nome, bool Administrador, bool Ativo, DateTimeOffset? UltimoAcesso,
                                   long? PerfilId = null, string? Perfil = null);

public sealed record PerfilDoUsuario(long Id, string Nome, bool Administrador);

/// <summary>Tentativa de login registrada em core.registro_acesso.</summary>
public sealed record RegistroAcesso(DateTimeOffset OcorridoEm, string Login, bool Sucesso, string? Motivo, string? Estacao);

/// <param name="Perfil">Nome do perfil do usuário na empresa; nulo para administrador sem perfil atribuído.</param>
public sealed record EmpresaPermitida(long Id, string Cnpj, string RazaoSocial, string? Perfil)
{
    /// <summary>Código da empresa no sistema (nº do cadastro, 4 dígitos).</summary>
    public string Codigo => Id.ToString("0000");
    public string CnpjFormatado => Domain.Cadastros.Cnpj.Formatar(Cnpj);
}

public sealed record AcessoEmpresa(long EmpresaId, string RazaoSocial, long PerfilId, string Perfil);

public interface IRepositorioUsuarios
{
    Task<bool> ExisteAlgumAsync(CancellationToken ct);
    Task<CredenciaisUsuario?> ObterPorLoginAsync(string login, CancellationToken ct);
    Task<CredenciaisUsuario?> ObterPorIdAsync(long id, CancellationToken ct);
    Task<bool> LoginExisteAsync(string login, CancellationToken ct);

    /// <summary>Insere o usuário. Com <paramref name="somenteSeNaoHouverUsuarios"/>, só insere se a tabela estiver vazia (retorna null caso contrário).</summary>
    Task<long?> CriarAsync(string login, string nome, string senhaHash, bool administrador, long? perfilId, bool deveTrocarSenha,
                           bool somenteSeNaoHouverUsuarios, CancellationToken ct);
    Task<PerfilDoUsuario?> ObterPerfilAsync(long perfilId, CancellationToken ct);
    Task<long?> IdPerfilAdministradorAsync(CancellationToken ct);
    /// <summary>Concede o perfil em todas as empresas cadastradas (substitui acessos existentes) e marca o usuário como "todas as empresas".</summary>
    Task AtribuirPerfilEmTodasEmpresasAsync(long usuarioId, long perfilId, CancellationToken ct);
    /// <summary>
    /// Aplica de uma vez os acessos do usuário por empresa (perfil nulo = sem acesso) e recalcula se ele acessa todas as empresas
    /// (nesse caso, as empresas cadastradas depois também são liberadas a ele).
    /// </summary>
    Task DefinirAcessosAsync(long usuarioId, IReadOnlyList<(long EmpresaId, long? PerfilId)> acessos, CancellationToken ct);
    Task<IReadOnlyList<RegistroAcesso>> ListarRegistroAcessoAsync(int limite, CancellationToken ct);
    /// <summary>Exclui o usuário do banco (o histórico fica sem o vínculo; a auditoria guarda o login).</summary>
    Task ExcluirAsync(long usuarioId, CancellationToken ct);

    Task RegistrarFalhaAsync(long? usuarioId, string login, int tentativas, DateTimeOffset? bloquearAte, string motivo, string? estacao, CancellationToken ct);
    Task RegistrarSucessoAsync(long usuarioId, string login, string? novoHash, string? estacao, CancellationToken ct);
    Task AlterarSenhaAsync(long usuarioId, string senhaHash, bool deveTrocarSenha, CancellationToken ct);
    Task AlterarAsync(long usuarioId, string nome, bool administrador, long perfilId, bool ativo, CancellationToken ct);
    Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken ct);
    /// <param name="perfilId">Perfil a atribuir; nulo revoga o acesso.</param>
    Task DefinirAcessoAsync(long usuarioId, long empresaId, long? perfilId, CancellationToken ct);
    Task<IReadOnlyList<AcessoEmpresa>> ListarAcessosAsync(long usuarioId, CancellationToken ct);
    Task<IReadOnlyList<EmpresaPermitida>> ListarEmpresasPermitidasAsync(long usuarioId, bool administrador, CancellationToken ct);
    /// <summary>Empresas permitidas ao usuário que estão ativas (<paramref name="ativas"/>) ou NÃO ativas no módulo.</summary>
    Task<IReadOnlyList<EmpresaPermitida>> ListarEmpresasPermitidasNoModuloAsync(long usuarioId, bool administrador, Cadastros.ModulosEmpresa modulo, bool ativas, CancellationToken ct);
}

/// <summary>
/// Autenticação e gestão de usuários. Operações administrativas exigem uma sessão de administrador
/// e são verificadas aqui (não apenas na interface).
/// </summary>
public sealed class ServicoUsuarios(IRepositorioUsuarios repositorio, TimeProvider? relogio = null)
{
    public const int TentativasAteBloqueio = 5;
    public static readonly TimeSpan TempoBloqueio = TimeSpan.FromMinutes(15);
    private const string MensagemGenerica = "Usuário ou senha inválidos.";

    /// <summary>
    /// Usuário de implantação: vale somente enquanto o banco não tem NENHUM usuário (instalação nova ou banco limpo), em qualquer
    /// módulo. Ele não existe no banco: é tratado aqui, conferido pelo mesmo hash de senha dos demais usuários. Só permite
    /// cadastrar o primeiro administrador; assim que esse existe, a combinação deixa de funcionar (nada a desativar).
    /// </summary>
    public const string LoginImplantacao = "vorticis";
    public const string SenhaImplantacao = "vorticis";
    private static readonly Lazy<string> HashImplantacao = new(() => Senha.GerarHash(SenhaImplantacao));

    // Hash de referência para gastar o mesmo tempo quando o login não existe (evita descobrir logins pelo tempo de resposta).
    private static readonly Lazy<string> HashFicticio = new(() => Senha.GerarHash(Guid.NewGuid().ToString()));

    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    /// <summary>
    /// A implantação está ativa: o banco ainda não tem nenhum usuário (nem o administrador do cliente). Nesse estado só o usuário
    /// de implantação entra, para cadastrar o primeiro administrador.
    /// </summary>
    public async Task<bool> ImplantacaoAtivaAsync(CancellationToken ct = default) => !await repositorio.ExisteAlgumAsync(ct);

    /// <summary>Perfil Administrador, o único que a implantação pode atribuir (null fora da implantação).</summary>
    public async Task<PerfilDoUsuario?> PerfilDeImplantacaoAsync(SessaoUsuario sessao, CancellationToken ct = default)
        => sessao.Implantacao && await repositorio.IdPerfilAdministradorAsync(ct) is long id ? await repositorio.ObterPerfilAsync(id, ct) : null;

    public async Task<Resultado<SessaoUsuario>> AutenticarAsync(string login, string senha, CancellationToken ct = default)
    {
        login = login.Trim();
        var estacao = Environment.MachineName;
        if (login.Length == 0 || senha.Length == 0) return Resultado<SessaoUsuario>.Falha("Informe usuário e senha.");

        var u = await repositorio.ObterPorLoginAsync(login, ct);
        if (u is null)
        {
            if (await ImplantacaoAtivaAsync(ct))
            {
                // & (sem curto-circuito): o mesmo tempo para login certo ou errado
                if (string.Equals(login, LoginImplantacao, StringComparison.OrdinalIgnoreCase) & Senha.Verificar(senha, HashImplantacao.Value))
                    return Resultado<SessaoUsuario>.Ok(new SessaoUsuario(0, LoginImplantacao, "Usuário de implantação", false, false, Implantacao: true));
                await repositorio.RegistrarFalhaAsync(null, login, 0, null, "IMPLANTACAO_INVALIDA", estacao, ct);
                return Resultado<SessaoUsuario>.Falha(
                    $"Instalação nova, sem usuários: entre com o usuário \"{LoginImplantacao}\" e a senha \"{SenhaImplantacao}\" para cadastrar o administrador.");
            }
            Senha.Verificar(senha, HashFicticio.Value);
            await repositorio.RegistrarFalhaAsync(null, login, 0, null, "USUARIO_INEXISTENTE", estacao, ct);
            return Resultado<SessaoUsuario>.Falha(MensagemGenerica);
        }

        var agora = _relogio.GetUtcNow();
        if (u.BloqueadoAte is { } ate && ate > agora)
        {
            await repositorio.RegistrarFalhaAsync(u.Id, login, u.TentativasFalhas, ate, "BLOQUEADO", estacao, ct);
            return Resultado<SessaoUsuario>.Falha(
                $"Usuário bloqueado por excesso de tentativas. Tente novamente após {ate.ToLocalTime():HH:mm} ou peça ao administrador para redefinir a senha.");
        }

        if (!Senha.Verificar(senha, u.SenhaHash) || !u.Ativo)
        {
            var tentativas = u.Ativo ? u.TentativasFalhas + 1 : u.TentativasFalhas;
            DateTimeOffset? bloquear = tentativas >= TentativasAteBloqueio ? agora + TempoBloqueio : null;
            await repositorio.RegistrarFalhaAsync(u.Id, login, bloquear is null ? tentativas : 0, bloquear,
                                                  u.Ativo ? "SENHA_INVALIDA" : "INATIVO", estacao, ct);
            return Resultado<SessaoUsuario>.Falha(bloquear is null
                ? MensagemGenerica
                : $"Usuário bloqueado por {TempoBloqueio.TotalMinutes:0} minutos após {TentativasAteBloqueio} tentativas inválidas.");
        }

        await repositorio.RegistrarSucessoAsync(u.Id, login, Senha.PrecisaRehash(u.SenhaHash) ? Senha.GerarHash(senha) : null, estacao, ct);
        return Resultado<SessaoUsuario>.Ok(new SessaoUsuario(u.Id, u.Login, u.Nome, u.Administrador, u.DeveTrocarSenha));
    }

    public async Task<Resultado> AlterarPropriaSenhaAsync(SessaoUsuario sessao, string senhaAtual, string novaSenha, string confirmacao,
                                                         CancellationToken ct = default)
    {
        var u = await repositorio.ObterPorIdAsync(sessao.Id, ct);
        if (u is null || !Senha.Verificar(senhaAtual, u.SenhaHash)) return Resultado.Falha("Senha atual incorreta.");
        var erros = Senha.ValidarPolitica(novaSenha, u.Login).ToList();
        if (novaSenha != confirmacao) erros.Add("A confirmação não confere com a nova senha.");
        if (novaSenha == senhaAtual) erros.Add("A nova senha deve ser diferente da atual.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.AlterarSenhaAsync(u.Id, Senha.GerarHash(novaSenha), deveTrocarSenha: false, ct);
        return Resultado.Ok();
    }

    // ---------------------------------------------------------------- administração

    /// <summary>
    /// Cadastra um usuário com o perfil informado. O perfil Administrador torna o usuário administrador do sistema (acessa todas
    /// as empresas); os demais perfis são concedidos nas empresas de <paramref name="empresaIds"/> — nulo = todas, inclusive as
    /// cadastradas depois. Na implantação (sessão do usuário vorticis) só é aceito o perfil Administrador e o cadastro só vale
    /// enquanto o banco não tiver nenhum usuário.
    /// </summary>
    public async Task<Resultado<long>> CriarUsuarioAsync(SessaoUsuario admin, string login, string nome, string senhaInicial,
                                                        long? perfilId, CancellationToken ct = default, IReadOnlyCollection<long>? empresaIds = null)
    {
        if (!admin.Administrador && !admin.Implantacao) return Resultado<long>.Falha("Somente administradores podem cadastrar usuários.");
        var erros = ValidarNovo(login, nome, senhaInicial, senhaInicial);
        var perfil = perfilId is long pid ? await repositorio.ObterPerfilAsync(pid, ct) : null;
        if (perfil is null) erros.Add("Informe o perfil do usuário.");
        if (admin.Implantacao)
        {
            if (perfil is { Administrador: false }) erros.Add("Na implantação só é possível cadastrar um usuário com o perfil Administrador.");
            if (string.Equals(login.Trim(), LoginImplantacao, StringComparison.OrdinalIgnoreCase))
                erros.Add($"O login \"{LoginImplantacao}\" é reservado à implantação; escolha outro login para o administrador.");
        }
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        if (await repositorio.LoginExisteAsync(login.Trim(), ct)) return Resultado<long>.Falha($"O login \"{login.Trim()}\" já está em uso.");
        var id = await repositorio.CriarAsync(login.Trim(), nome.Trim(), Senha.GerarHash(senhaInicial), perfil!.Administrador, perfil.Id,
                                              deveTrocarSenha: !admin.Implantacao, somenteSeNaoHouverUsuarios: admin.Implantacao, ct);
        if (id is null) return Resultado<long>.Falha("A implantação já foi concluída: existe usuário cadastrado. Entre com o login do administrador.");
        if (!perfil.Administrador)
        {
            if (empresaIds is null) await repositorio.AtribuirPerfilEmTodasEmpresasAsync(id.Value, perfil.Id, ct);
            else await repositorio.DefinirAcessosAsync(id.Value, [.. empresaIds.Distinct().Select(e => (e, (long?)perfil.Id))], ct);
        }
        return Resultado<long>.Ok(id.Value);
    }

    public async Task<Resultado> RedefinirSenhaAsync(SessaoUsuario admin, long usuarioId, string senhaTemporaria, CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores podem redefinir senhas.");
        var u = await repositorio.ObterPorIdAsync(usuarioId, ct);
        if (u is null) return Resultado.Falha("Usuário não encontrado.");
        var erros = Senha.ValidarPolitica(senhaTemporaria, u.Login);
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.AlterarSenhaAsync(usuarioId, Senha.GerarHash(senhaTemporaria), deveTrocarSenha: true, ct);   // também desbloqueia
        return Resultado.Ok();
    }

    /// <summary>Altera nome, perfil e situação. O perfil Administrador concede (e os demais retiram) a administração do sistema.</summary>
    public async Task<Resultado> AlterarUsuarioAsync(SessaoUsuario admin, long usuarioId, string nome, long? perfilId, bool ativo,
                                                    CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores podem alterar usuários.");
        if (string.IsNullOrWhiteSpace(nome)) return Resultado.Falha("Informe o nome.");
        var perfil = perfilId is long pid ? await repositorio.ObterPerfilAsync(pid, ct) : null;
        if (perfil is null) return Resultado.Falha("Informe o perfil do usuário.");
        if (usuarioId == admin.Id && (!perfil.Administrador || !ativo))
            return Resultado.Falha("Você não pode remover seu próprio acesso de administrador.");
        try
        {
            await repositorio.AlterarAsync(usuarioId, nome.Trim(), perfil.Administrador, perfil.Id, ativo, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha(ex.Message);
        }
    }

    public async Task<Resultado> DefinirAcessoAsync(SessaoUsuario admin, long usuarioId, long empresaId, long? perfilId,
                                                   CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores podem conceder acesso.");
        try
        {
            await repositorio.DefinirAcessoAsync(usuarioId, empresaId, perfilId, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha(ex.Message);
        }
    }

    /// <summary>
    /// Define de uma vez as empresas que o usuário acessa e o perfil em cada uma (perfil nulo = sem acesso). Empresas não citadas
    /// ficam como estão. O usuário só "acessa todas as empresas" (e recebe as futuras) enquanto tiver acesso a todas.
    /// </summary>
    public async Task<Resultado> DefinirAcessosAsync(SessaoUsuario admin, long usuarioId, IReadOnlyDictionary<long, long?> acessos,
                                                    CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores podem conceder acesso.");
        try
        {
            await repositorio.DefinirAcessosAsync(usuarioId, [.. acessos.Select(a => (a.Key, a.Value))], ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha(ex.Message);
        }
    }

    public async Task<IReadOnlyList<UsuarioResumo>> ListarAsync(SessaoUsuario admin, CancellationToken ct = default)
        => admin.Administrador ? await repositorio.ListarAsync(ct) : [];

    /// <summary>
    /// Exclui o usuário do banco de dados, liberando o login para um novo cadastro. O histórico (lançamentos, acessos)
    /// é mantido sem o vínculo; a trilha de auditoria guarda o login de quem fez cada operação.
    /// </summary>
    public async Task<Resultado> ExcluirUsuarioAsync(SessaoUsuario admin, long usuarioId, CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente administradores podem excluir usuários.");
        if (usuarioId == admin.Id) return Resultado.Falha("Você não pode excluir o seu próprio usuário.");
        try
        {
            await repositorio.ExcluirAsync(usuarioId, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha(ex.Message);
        }
    }

    /// <summary>Últimas tentativas de login (sucesso e falha), mais recentes primeiro.</summary>
    public async Task<IReadOnlyList<RegistroAcesso>> RegistroDeAcessosAsync(SessaoUsuario admin, int limite = 300, CancellationToken ct = default)
        => admin.Administrador ? await repositorio.ListarRegistroAcessoAsync(limite, ct) : [];

    public Task<IReadOnlyList<AcessoEmpresa>> ListarAcessosAsync(long usuarioId, CancellationToken ct = default)
        => repositorio.ListarAcessosAsync(usuarioId, ct);

    /// <summary>Empresas que o usuário pode acessar: todas para o administrador; as liberadas no cadastro para os demais; nenhuma na implantação.</summary>
    public async Task<IReadOnlyList<EmpresaPermitida>> EmpresasPermitidasAsync(SessaoUsuario sessao, CancellationToken ct = default)
        => sessao.Implantacao ? [] : await repositorio.ListarEmpresasPermitidasAsync(sessao.Id, sessao.Administrador, ct);

    /// <summary>Empresas que o usuário pode acessar e que estão ativas no módulo (lista do seletor do topo).</summary>
    public async Task<IReadOnlyList<EmpresaPermitida>> EmpresasDoModuloAsync(SessaoUsuario sessao, Cadastros.ModulosEmpresa modulo, CancellationToken ct = default)
        => sessao.Implantacao ? [] : await repositorio.ListarEmpresasPermitidasNoModuloAsync(sessao.Id, sessao.Administrador, modulo, true, ct);

    /// <summary>Empresas que o usuário pode acessar mas que ainda não estão ativas no módulo (para oferecer a ativação).</summary>
    public async Task<IReadOnlyList<EmpresaPermitida>> EmpresasForaDoModuloAsync(SessaoUsuario sessao, Cadastros.ModulosEmpresa modulo, CancellationToken ct = default)
        => sessao.Implantacao ? [] : await repositorio.ListarEmpresasPermitidasNoModuloAsync(sessao.Id, sessao.Administrador, modulo, false, ct);

    private static List<string> ValidarNovo(string login, string nome, string senha, string confirmacao)
    {
        var erros = new List<string>();
        var l = login.Trim();
        if (l.Length is < 3 or > 60 || !l.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            erros.Add("O login deve ter de 3 a 60 caracteres: letras, números, ponto, hífen ou sublinhado.");
        if (string.IsNullOrWhiteSpace(nome)) erros.Add("Informe o nome.");
        erros.AddRange(Senha.ValidarPolitica(senha, l));
        if (senha != confirmacao) erros.Add("A confirmação não confere com a senha.");
        return erros;
    }
}
