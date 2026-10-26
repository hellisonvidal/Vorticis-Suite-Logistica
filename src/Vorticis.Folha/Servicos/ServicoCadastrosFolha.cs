using Vorticis.Application.Seguranca;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Servicos;

/// <summary>Quem pode ver dados da folha (LGPD: dados de trabalhadores só com alguma permissão da folha).</summary>
internal static class AcessoFolha
{
    private static readonly Permissao[] Todas =
    [
        Permissao.FolhaConsultar, Permissao.FolhaCadastrosManter, Permissao.FolhaParametrosManter, Permissao.FolhaEventosLancar, Permissao.FolhaCalcular,
        Permissao.FolhaFechar, Permissao.FolhaReabrir, Permissao.FolhaContabilizar, Permissao.FolhaESocial, Permissao.FolhaRelatorios,
    ];

    public static bool PodeVer(PermissoesEfetivas p) => p.Administrador || Todas.Any(p.Pode);

    public static async Task<bool> PodeVerAsync(this Autorizador a, long usuarioId, long empresaId, CancellationToken ct)
        => PodeVer(await a.ObterAsync(usuarioId, empresaId, ct));
}

public static class Pis
{
    /// <summary>Valida o PIS/PASEP/NIT (11 dígitos, dígito verificador módulo 11 com pesos 3,2,9,8,7,6,5,4,3,2).</summary>
    public static bool Valido(string? valor)
    {
        var d = Cnpj.SomenteDigitos(valor);
        if (d.Length != 11 || d.All(c => c == d[0])) return false;
        int[] pesos = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var soma = 0;
        for (var i = 0; i < 10; i++) soma += (d[i] - '0') * pesos[i];
        var dv = 11 - soma % 11;
        if (dv >= 10) dv = 0;
        return dv == d[10] - '0';
    }
}

/// <summary>Cadastros da folha: departamentos, cargos, funcionários (com dependentes), rubricas, parâmetros e afastamentos.</summary>
public sealed class ServicoCadastrosFolha(IRepositorioCadastrosFolha repositorio, Autorizador autorizador)
{
    private static readonly string[] Contratos = ["INDETERMINADO", "DETERMINADO", "EXPERIENCIA", "APRENDIZ", "ESTAGIO"];

    private async Task<string?> NegarAsync(long usuarioId, long empresaId, Permissao p, CancellationToken ct) => await autorizador.VerificarAsync(usuarioId, empresaId, p, ct);

    // ------------------------------------------------------------------ departamentos e cargos

    public async Task<IReadOnlyList<Departamento>> ListarDepartamentosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarDepartamentosAsync(empresaId, ct) : [];

    public async Task<Resultado<long>> GravarDepartamentoAsync(long empresaId, Departamento d, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return negado;
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Codigo) || d.Codigo.Trim().Length > 20) erros.Add("Informe o código do departamento (até 20 caracteres).");
        if (string.IsNullOrWhiteSpace(d.Nome)) erros.Add("Informe o nome do departamento.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarDepartamentoAsync(empresaId, d with { Codigo = d.Codigo.Trim(), Nome = d.Nome.Trim() }, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirDepartamentoAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return Resultado.Falha(negado);
        try { await repositorio.ExcluirDepartamentoAsync(empresaId, id, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException) { return Resultado.Falha("O departamento tem funcionários e não pode ser excluído; marque-o como inativo."); }
    }

    public async Task<IReadOnlyList<Cargo>> ListarCargosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarCargosAsync(empresaId, ct) : [];

    public async Task<Resultado<long>> GravarCargoAsync(long empresaId, Cargo c, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return negado;
        var erros = new List<string>();
        var cbo = new string((c.Cbo ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(c.Codigo) || c.Codigo.Trim().Length > 20) erros.Add("Informe o código do cargo (até 20 caracteres).");
        if (string.IsNullOrWhiteSpace(c.Nome)) erros.Add("Informe o nome do cargo.");
        if (cbo.Length != 0 && cbo.Length != 6) erros.Add("O CBO deve ter 6 dígitos.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarCargoAsync(empresaId, c with { Codigo = c.Codigo.Trim(), Nome = c.Nome.Trim(), Cbo = cbo.Length == 0 ? null : cbo }, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirCargoAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return Resultado.Falha(negado);
        try { await repositorio.ExcluirCargoAsync(empresaId, id, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException) { return Resultado.Falha("O cargo tem funcionários e não pode ser excluído; marque-o como inativo."); }
    }

    // ------------------------------------------------------------------ funcionários

    public async Task<IReadOnlyList<Funcionario>> ListarFuncionariosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarFuncionariosAsync(empresaId, ct) : [];

    public async Task<Funcionario?> ObterFuncionarioAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ObterFuncionarioAsync(empresaId, id, ct) : null;

    public static string ProximaMatricula(IEnumerable<Funcionario> existentes)
        => Vorticis.Domain.Comum.Sequencia.MenorLivre(existentes.Select(f => f.Matricula)).ToString("000000");

    /// <summary>Regras de cadastro do funcionário; devolve os erros.</summary>
    public static IReadOnlyList<string> Validar(Funcionario f, DateOnly hoje)
    {
        var erros = new List<string>();
        var cpf = Cnpj.SomenteDigitos(f.Cpf);
        if (string.IsNullOrWhiteSpace(f.Nome)) erros.Add("Informe o nome do funcionário.");
        if (!Cpf.Valido(cpf)) erros.Add("CPF inválido.");
        if (!string.IsNullOrWhiteSpace(f.PisNis) && !Pis.Valido(f.PisNis)) erros.Add("PIS/NIS inválido.");
        if (f.Sexo is not ("M" or "F")) erros.Add("Informe o sexo (M ou F).");
        if (f.Nascimento >= hoje) erros.Add("Data de nascimento inválida.");
        var idadeMinima = f.Categoria == "103" || f.TipoContrato == "APRENDIZ" ? 14 : 16;
        if (f.Admissao < f.Nascimento.AddYears(idadeMinima)) erros.Add($"O funcionário deve ter ao menos {idadeMinima} anos na admissão.");
        if (!Contratos.Contains(f.TipoContrato)) erros.Add("Tipo de contrato inválido.");
        if (f.TipoContrato is "DETERMINADO" or "EXPERIENCIA" && f.TerminoContrato is null) erros.Add("Informe a data de término do contrato a prazo determinado.");
        if (f.TerminoContrato is { } t && t < f.Admissao) erros.Add("O término do contrato não pode ser anterior à admissão.");
        if (f.TipoContrato == "APRENDIZ" != (f.Categoria == "103")) erros.Add("Aprendiz: use o tipo de contrato APRENDIZ com a categoria eSocial 103.");
        if (f.TipoContrato == "ESTAGIO" != (f.Categoria == "901")) erros.Add("Estagiário: use o tipo de contrato ESTAGIO com a categoria eSocial 901.");
        if (f.Salario <= 0) erros.Add(f.TipoSalario == "HORISTA" ? "Informe o valor da hora." : "Informe o salário.");
        if (f.TipoSalario is not ("MENSAL" or "HORISTA")) erros.Add("Tipo de salário inválido.");
        if (f.JornadaMensal <= 0 || f.JornadaSemanal <= 0) erros.Add("Informe a jornada semanal e mensal.");
        if (f.Insalubridade is not (0 or 10 or 20 or 40)) erros.Add("Insalubridade: 0, 10, 20 ou 40%.");
        if (!string.IsNullOrWhiteSpace(f.Cbo) && new string(f.Cbo.Where(char.IsDigit).ToArray()).Length != 6) erros.Add("O CBO deve ter 6 dígitos.");
        if (!string.IsNullOrWhiteSpace(f.Uf) && !Uf.Valida(f.Uf)) erros.Add("UF inválida.");
        if (f.CnpjSindicato is { Length: > 0 } sind && !Cnpj.Valido(sind)) erros.Add("CNPJ do sindicato inválido.");
        if (f.TipoJornada is not (2 or 3 or 4 or 5 or 6 or 7 or 9)) erros.Add("Tipo de jornada inválido (2 a 7 ou 9).");
        if (f.Situacao is not ("ATIVO" or "AFASTADO" or "DESLIGADO")) erros.Add("Situação inválida.");
        if (f.Situacao == "DESLIGADO" && f.Desligamento is null) erros.Add("Informe a data do desligamento.");
        if (f.Desligamento is { } d && d < f.Admissao) erros.Add("O desligamento não pode ser anterior à admissão.");
        foreach (var dep in f.Dependentes)
        {
            if (string.IsNullOrWhiteSpace(dep.Nome)) erros.Add("Dependente sem nome.");
            if (!string.IsNullOrWhiteSpace(dep.Cpf) && !Cpf.Valido(dep.Cpf)) erros.Add($"CPF inválido do dependente {dep.Nome}.");
            if (dep.DepSalarioFamilia && !dep.Incapaz && dep.Nascimento.AddYears(14) < new DateOnly(hoje.Year, hoje.Month, 1))
                erros.Add($"{dep.Nome}: salário-família só para menor de 14 anos ou inválido.");
        }
        return erros;
    }

    public async Task<Resultado<long>> GravarFuncionarioAsync(long empresaId, Funcionario f, long usuarioId, DateOnly? dataAlteracao = null, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return negado;
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var matricula = string.IsNullOrWhiteSpace(f.Matricula) ? ProximaMatricula(await repositorio.ListarFuncionariosAsync(empresaId, ct)) : f.Matricula.Trim();
        var cbo = new string((f.Cbo ?? "").Where(char.IsDigit).ToArray());
        var norm = f with
        {
            Matricula = matricula, Nome = f.Nome.Trim(), Cpf = Cnpj.SomenteDigitos(f.Cpf), PisNis = Texto(Cnpj.SomenteDigitos(f.PisNis)),
            Cbo = Texto(cbo), Uf = Texto(f.Uf?.ToUpperInvariant()), Cep = Texto(Cnpj.SomenteDigitos(f.Cep)), Banco = Texto(f.Banco),
            TipoLogradouro = Texto(f.TipoLogradouro?.ToUpperInvariant()), CnpjSindicato = Texto(Cnpj.SomenteDigitos(f.CnpjSindicato)),
            DescricaoJornada = Texto(f.DescricaoJornada),
            Dependentes = f.Dependentes.Select(d => d with { Nome = d.Nome.Trim(), Cpf = Texto(Cnpj.SomenteDigitos(d.Cpf)) }).ToArray(),
        };
        var erros = Validar(norm, hoje);
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarFuncionarioAsync(empresaId, norm, dataAlteracao ?? hoje, usuarioId, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    private static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<Resultado> ExcluirFuncionarioAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return Resultado.Falha(negado);
        if (await repositorio.FuncionarioTemMovimentoAsync(empresaId, id, ct))
            return Resultado.Falha("O funcionário tem folha, eventos, férias ou rescisão registrados e não pode ser excluído. Registre o desligamento.");
        try { await repositorio.ExcluirFuncionarioAsync(empresaId, id, usuarioId, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    // ------------------------------------------------------------------ rubricas

    public async Task<IReadOnlyList<Rubrica>> ListarRubricasAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (!await autorizador.PodeVerAsync(usuarioId, empresaId, ct)) return [];
        await repositorio.GarantirRubricasPadraoAsync(empresaId, ct);
        return await repositorio.ListarRubricasAsync(empresaId, ct);
    }

    public static IReadOnlyList<string> ValidarRubrica(Rubrica r)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 10) erros.Add("Informe o código da rubrica (até 10 caracteres).");
        if (string.IsNullOrWhiteSpace(r.Descricao)) erros.Add("Informe a descrição da rubrica.");
        if (r.Tipo is not ('P' or 'D' or 'I')) erros.Add("Tipo da rubrica: provento, desconto ou informativa.");
        if (r.Natureza.Length != 4 || !r.Natureza.All(char.IsAsciiDigit)) erros.Add("A natureza (Tabela 03 do eSocial) tem 4 dígitos.");
        if (r.IncInss.Length != 2 || r.IncIrrf.Length != 2 || r.IncFgts.Length != 2) erros.Add("Os códigos de incidência têm 2 dígitos (00 = não incide).");
        return erros;
    }

    public async Task<Resultado<long>> GravarRubricaAsync(long empresaId, Rubrica r, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return negado;
        var erros = ValidarRubrica(r);
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarRubricaAsync(empresaId, r with { Codigo = r.Codigo.Trim(), Descricao = r.Descricao.Trim() }, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirRubricaAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return Resultado.Falha(negado);
        var r = (await repositorio.ListarRubricasAsync(empresaId, ct)).FirstOrDefault(x => x.Id == id);
        if (r is null) return Resultado.Falha("Rubrica não encontrada.");
        if (r.Sistema) return Resultado.Falha("Rubrica padrão do sistema não pode ser excluída (pode ser desativada ou ter as incidências ajustadas).");
        try { await repositorio.ExcluirRubricaAsync(empresaId, id, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException) { return Resultado.Falha("A rubrica tem lançamentos e não pode ser excluída; desative-a."); }
    }

    // ------------------------------------------------------------------ parâmetros e tabelas legais

    public async Task<ParametroEmpresa> ObterParametrosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ObterParametrosAsync(empresaId, ct) : ParametroEmpresa.Padrao;

    public async Task<Resultado> GravarParametrosAsync(long empresaId, ParametroEmpresa p, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaParametrosManter, ct) is { } negado) return Resultado.Falha(negado);
        var erros = new List<string>();
        if (p.RatAliquota is not (0 or 1 or 2 or 3)) erros.Add("O RAT é 1%, 2% ou 3% (grau de risco leve, médio ou grave).");
        if (p.Fap is < 0.5m or > 2m) erros.Add("O FAP vai de 0,5000 a 2,0000.");
        if (p.TerceirosAliquota is < 0 or > 10) erros.Add("Alíquota de terceiros inválida.");
        if (p.SimplesAnexo is not null && p.SimplesAnexo is not ("I" or "II" or "III" or "IV" or "V")) erros.Add("Anexo do Simples Nacional: I a V.");
        if (!string.IsNullOrWhiteSpace(p.ResponsavelCpf) && !Cpf.Valido(p.ResponsavelCpf)) erros.Add("CPF do responsável inválido.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.GravarParametrosAsync(empresaId, p with { ResponsavelCpf = Texto(Cnpj.SomenteDigitos(p.ResponsavelCpf)) }, usuarioId, ct);
        return Resultado.Ok();
    }

    public async Task<ConfiguracaoFolha> ObterConfiguracaoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ObterConfiguracaoAsync(empresaId, ct) : ConfiguracaoFolha.Padrao;

    public async Task<Resultado> GravarConfiguracaoAsync(long empresaId, ConfiguracaoFolha c, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaParametrosManter, ct) is { } negado) return Resultado.Falha(negado);
        var erros = new List<string>();
        if (c.DiaPagamento is < 1 or > 31) erros.Add("O dia de pagamento deve estar entre 1 e 31.");
        if (c.AmbienteEsocial is not (1 or 2)) erros.Add("Ambiente do eSocial inválido.");
        if (c.TransmissorEsocial is not ("EMPREGADOR" or "PROCURADOR" or "CONTADOR")) erros.Add("Transmissor do eSocial inválido.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.GravarConfiguracaoAsync(empresaId, c, usuarioId, ct);
        return Resultado.Ok();
    }

    /// <summary>Tabelas legais cadastradas (INSS, IRRF, salário-família, salário mínimo, FGTS) com as vigências.</summary>
    public async Task<IReadOnlyList<ParametroLegal>> ListarTabelasLegaisAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarParametrosLegaisAsync(ct) : [];

    // ------------------------------------------------------------------ afastamentos

    public async Task<IReadOnlyList<Afastamento>> ListarAfastamentosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => await autorizador.PodeVerAsync(usuarioId, empresaId, ct) ? await repositorio.ListarAfastamentosAsync(empresaId, ct) : [];

    public async Task<Resultado<long>> GravarAfastamentoAsync(long empresaId, Afastamento a, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return negado;
        var erros = new List<string>();
        if (a.FuncionarioId == 0) erros.Add("Escolha o funcionário.");
        if (a.CodigoMotivo.Length != 2 || !a.CodigoMotivo.All(char.IsAsciiDigit)) erros.Add("O motivo do afastamento tem 2 dígitos (Tabela 18 do eSocial).");
        if (a.Fim is { } fim && fim < a.Inicio) erros.Add("O fim do afastamento não pode ser anterior ao início.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarAfastamentoAsync(empresaId, a, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    public async Task<Resultado> ExcluirAfastamentoAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(usuarioId, empresaId, Permissao.FolhaCadastrosManter, ct) is { } negado) return Resultado.Falha(negado);
        await repositorio.ExcluirAfastamentoAsync(empresaId, id, ct);
        return Resultado.Ok();
    }
}
