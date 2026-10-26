using Vorticis.Application.Seguranca;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Cadastros;

/// <summary>Contador (pessoa física, em geral com CRC; o procurador que não é contador fica sem CRC). <paramref name="AssinaEcdEcf"/>: é quem assina a ECD e a ECF (J930/0930).</summary>
public sealed record Contador(long Id, string Nome, string Cpf, string Crc, string UfCrc, string? Email, string? Telefone, long? EscritorioId,
                              bool AssinaEcdEcf = false)
{
    /// <summary>CRC como impresso nos termos: "UF-número", sem repetir a UF quando ela já faz parte do número (ex.: 1SP123456/O-7).</summary>
    public string CrcCompleto => string.IsNullOrWhiteSpace(Crc) ? "" : Crc.Contains(UfCrc, StringComparison.OrdinalIgnoreCase) ? Crc : $"{UfCrc}-{Crc}";
}

public sealed record EscritorioContabil(long Id, string Cnpj, string RazaoSocial, string? Crc, string? UfCrc, string? Email, string? Telefone);

public sealed record VinculoContabil(long Id, long? ContadorId, long? EscritorioId, string Nome, string Documento, string? Crc,
                                     DateOnly Inicio, DateOnly? Fim, bool ResponsavelTecnico, bool AssinaEcdEcf = false,
                                     Procuracao? Procuracao = null)
{
    public bool Vigente => Fim is null;
    public bool EhProcurador => Procuracao is { Procurador: true };
    public string Tipo => ContadorId is not null ? "Contador" : "Escritório contábil";
}

public sealed record Socio(long Id, string Nome, char TipoPessoa, string? CpfCnpj, string? DocumentoMascarado, string Qualificacao,
                           int? QualificacaoCodigo, decimal? Participacao, DateOnly? DataEntrada, DateOnly? DataSaida,
                           bool RepresentanteLegal, string? Email, string? Telefone, string Origem = "MANUAL")
{
    public bool Ativo => DataSaida is null;
}

/// <summary>Códigos da abrangência da procuração eletrônica (para quais obrigações o procurador pode assinar/transmitir).</summary>
public static class AbrangenciaProcuracao
{
    public const string Ecd = "ECD", Ecf = "ECF", Efd = "EFD", Esocial = "ESOCIAL";
    public static readonly IReadOnlyList<string> Todas = [Ecd, Ecf, Efd, Esocial];

    public static string Descricao(string codigo) => codigo switch { Esocial => "eSocial", _ => codigo };
}

/// <summary>
/// Procuração eletrônica do contador vinculado à empresa: é procurador da empresa, a vigência e a abrangência (ECD, ECF, EFD, ESOCIAL).
/// A procuração eletrônica em si é cadastrada no e-CAC.
/// </summary>
public sealed record Procuracao(bool Procurador, DateOnly? Inicio, DateOnly? Fim, IReadOnlyList<string> Abrangencia, string? Observacao = null)
{
    public static Procuracao Nenhuma { get; } = new(false, null, null, []);
}

/// <summary>Procurador da empresa: o contador marcado como procurador no vínculo (qualificação 309 – Procurador na ECD/ECF). A procuração eletrônica fica no e-CAC.</summary>
public sealed record Procurador(long Id, string Nome, string Cpf, string? Email, string? Telefone, DateOnly VigenciaInicio, DateOnly? VigenciaFim,
                                IReadOnlyList<string> Abrangencia, string? Observacao = null)
{
    public bool VigenteEm(DateOnly data) => VigenciaInicio <= data && (VigenciaFim is null || VigenciaFim >= data);
    public bool Abrange(string obrigacao) => Abrangencia.Contains(obrigacao, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Síndico do condomínio (módulo Condomínio, aba Síndico do cadastro da empresa). <paramref name="AssinaLivros"/>: assina os livros
/// contábeis e fiscais como responsável pelo condomínio; <paramref name="AssinaturaDigital"/>: assina com certificado digital.
/// </summary>
public sealed record Sindico(string Nome, string? Cpf, string? Email, string? Telefone, DateOnly? MandatoInicio, DateOnly? MandatoFim,
                             bool AssinaLivros, bool AssinaturaDigital);

public interface IRepositorioResponsaveis
{
    Task<IReadOnlyList<Contador>> ListarContadoresAsync(CancellationToken ct);
    Task<IReadOnlyList<EscritorioContabil>> ListarEscritoriosAsync(CancellationToken ct);
    /// <summary>Insere ou atualiza pelo CPF; devolve o id.</summary>
    Task<long> SalvarContadorAsync(Contador c, CancellationToken ct);
    /// <summary>Insere ou atualiza pelo CNPJ; devolve o id.</summary>
    Task<long> SalvarEscritorioAsync(EscritorioContabil e, CancellationToken ct);

    Task<IReadOnlyList<VinculoContabil>> ListarVinculosAsync(long empresaId, CancellationToken ct);
    Task<long> IncluirVinculoAsync(long empresaId, long? contadorId, long? escritorioId, DateOnly inicio, bool responsavel, Procuracao? procuracao, long usuarioId, CancellationToken ct);
    Task DefinirProcuracaoAsync(long empresaId, long vinculoId, Procuracao procuracao, long usuarioId, CancellationToken ct);
    Task EncerrarVinculoAsync(long empresaId, long vinculoId, DateOnly fim, long usuarioId, CancellationToken ct);
    Task DefinirResponsavelAsync(long empresaId, long vinculoId, long usuarioId, CancellationToken ct);
    Task DefinirAssinaEcdEcfAsync(long contadorId, bool assina, long usuarioId, CancellationToken ct);
    Task ExcluirVinculoAsync(long empresaId, long vinculoId, long usuarioId, CancellationToken ct);

    Task<IReadOnlyList<Socio>> ListarSociosAsync(long empresaId, CancellationToken ct);
    Task<long> SalvarSocioAsync(long empresaId, Socio s, long usuarioId, CancellationToken ct);
    Task ExcluirSocioAsync(long empresaId, long socioId, long usuarioId, CancellationToken ct);

    Task<IReadOnlyList<Procurador>> ListarProcuradoresAsync(long empresaId, CancellationToken ct);

    Task<Sindico?> ObterSindicoAsync(long empresaId, CancellationToken ct);
    Task SalvarSindicoAsync(long empresaId, Sindico s, long usuarioId, CancellationToken ct);
    Task ExcluirSindicoAsync(long empresaId, long usuarioId, CancellationToken ct);
}

/// <summary>
/// Contabilidade responsável (contadores e escritórios, com vigência e responsável técnico) e quadro societário.
/// São os signatários da ECD/ECF (registros J930/0930: contador com CRC e representante legal) e dos termos do Diário.
/// </summary>
public sealed class ServicoResponsaveis(IRepositorioResponsaveis repositorio, IRepositorioEmpresa empresas, Autorizador autorizador,
                                       IConsultaCnpj? consultaCnpj = null)
{
    // ------------------------------------------------------------------ leitura

    public async Task<Resultado<IReadOnlyList<VinculoContabil>>> ListarVinculosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<IReadOnlyList<VinculoContabil>>.Ok(await repositorio.ListarVinculosAsync(empresaId, ct))
            : Resultado<IReadOnlyList<VinculoContabil>>.Falha("Você não tem acesso a esta empresa.");

    public async Task<Resultado<IReadOnlyList<Socio>>> ListarSociosAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<IReadOnlyList<Socio>>.Ok(await repositorio.ListarSociosAsync(empresaId, ct))
            : Resultado<IReadOnlyList<Socio>>.Falha("Você não tem acesso a esta empresa.");

    public Task<IReadOnlyList<Contador>> ListarContadoresAsync(CancellationToken ct = default) => repositorio.ListarContadoresAsync(ct);
    public Task<IReadOnlyList<EscritorioContabil>> ListarEscritoriosAsync(CancellationToken ct = default) => repositorio.ListarEscritoriosAsync(ct);

    /// <summary>Contador responsável técnico vigente (assina a ECD e os termos do Diário).</summary>
    public async Task<Contador?> ResponsavelTecnicoAsync(long empresaId, CancellationToken ct = default)
    {
        var v = (await repositorio.ListarVinculosAsync(empresaId, ct)).FirstOrDefault(x => x.Vigente && x.ResponsavelTecnico);
        return v?.ContadorId is long id ? (await repositorio.ListarContadoresAsync(ct)).FirstOrDefault(c => c.Id == id) : null;
    }

    /// <summary>Contador que assina a ECD/ECF: o vigente marcado "assina ECD/ECF" (o responsável técnico primeiro); sem marcação, o responsável técnico.</summary>
    public async Task<Contador?> SignatarioEcdEcfAsync(long empresaId, CancellationToken ct = default)
    {
        var v = (await repositorio.ListarVinculosAsync(empresaId, ct)).Where(x => x.Vigente && x.ContadorId is not null)
                                                                       .OrderByDescending(x => x.AssinaEcdEcf).ThenByDescending(x => x.ResponsavelTecnico)
                                                                       .FirstOrDefault(x => x.AssinaEcdEcf || x.ResponsavelTecnico);
        return v?.ContadorId is long id ? (await repositorio.ListarContadoresAsync(ct)).FirstOrDefault(c => c.Id == id) : null;
    }

    /// <summary>Marca ou desmarca o contador do vínculo como signatário da ECD/ECF.</summary>
    public async Task<Resultado> DefinirAssinaEcdEcfAsync(long empresaId, long vinculoId, bool assina, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        var v = (await repositorio.ListarVinculosAsync(empresaId, ct)).FirstOrDefault(x => x.Id == vinculoId);
        if (v is null) return Resultado.Falha("Vínculo não encontrado.");
        if (v.ContadorId is not long contador) return Resultado.Falha("Só um contador (pessoa física com CRC) assina a ECD/ECF.");
        if (assina && string.IsNullOrWhiteSpace(v.Crc)) return Resultado.Falha("Informe o CRC do contador no cadastro para ele assinar a ECD/ECF.");
        return await GravarSemValor(() => repositorio.DefinirAssinaEcdEcfAsync(contador, assina, usuarioId, ct));
    }

    public async Task<Socio?> RepresentanteLegalAsync(long empresaId, CancellationToken ct = default)
        => (await repositorio.ListarSociosAsync(empresaId, ct)).FirstOrDefault(s => s.Ativo && s.RepresentanteLegal);

    /// <summary>Sócio/responsável que assina os relatórios: o representante legal ativo; sem ele, o primeiro sócio ativo.</summary>
    public async Task<Socio?> SocioResponsavelAsync(long empresaId, CancellationToken ct = default)
    {
        var ativos = (await repositorio.ListarSociosAsync(empresaId, ct)).Where(s => s.Ativo).ToList();
        return ativos.FirstOrDefault(s => s.RepresentanteLegal) ?? ativos.FirstOrDefault();
    }

    // ------------------------------------------------------------------ contabilidade

    public async Task<Resultado<long>> VincularContadorAsync(long empresaId, Contador dados, DateOnly inicio, bool responsavelTecnico, long usuarioId,
                                                            CancellationToken ct = default, Procuracao? procuracao = null)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return negado;
        var cpf = Cnpj.SomenteDigitos(dados.Cpf);
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(dados.Nome)) erros.Add("Informe o nome do contador.");
        if (!Cpf.Valido(cpf)) erros.Add("CPF do contador inválido.");
        // Só o procurador que não assina como contador (nem é responsável técnico) pode ficar sem CRC.
        var semCrc = procuracao is { Procurador: true } && !responsavelTecnico && !dados.AssinaEcdEcf && string.IsNullOrWhiteSpace(dados.Crc);
        if (!semCrc)
        {
            if (string.IsNullOrWhiteSpace(dados.Crc)) erros.Add("Informe o número do CRC.");
            if (!Uf.Valida(dados.UfCrc)) erros.Add("UF do CRC inválida.");
        }
        erros.AddRange(ValidarProcuracao(procuracao));
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var vinculos = await repositorio.ListarVinculosAsync(empresaId, ct);
        var contadorId = await repositorio.SalvarContadorAsync(dados with
        {
            Nome = dados.Nome.Trim(), Cpf = cpf, Crc = semCrc ? "" : dados.Crc.Trim().ToUpperInvariant(), UfCrc = semCrc ? "" : dados.UfCrc.Trim().ToUpperInvariant(),
            Email = Limpar(dados.Email)?.ToLowerInvariant(), Telefone = Limpar(dados.Telefone),
        }, ct);
        if (vinculos.Any(v => v.Vigente && v.ContadorId == contadorId))
            return Resultado<long>.Falha("Este contador já está vinculado à empresa (dados cadastrais atualizados).");
        if (responsavelTecnico && vinculos.FirstOrDefault(v => v.Vigente && v.ResponsavelTecnico) is { } atual)
            return Resultado<long>.Falha($"{atual.Nome} já é o responsável técnico vigente. Encerre o vínculo dele ou vincule sem marcar como responsável.");
        return await Gravar(() => repositorio.IncluirVinculoAsync(empresaId, contadorId, null, inicio, responsavelTecnico, NormalizarProcuracao(procuracao), usuarioId, ct));
    }

    public async Task<Resultado<long>> VincularEscritorioAsync(long empresaId, EscritorioContabil dados, DateOnly inicio, long usuarioId,
                                                              CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return negado;
        var cnpj = new string(dados.Cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var erros = new List<string>();
        if (!Cnpj.Valido(cnpj)) erros.Add("CNPJ do escritório inválido.");
        if (string.IsNullOrWhiteSpace(dados.RazaoSocial)) erros.Add("Informe a razão social do escritório.");
        if (!string.IsNullOrWhiteSpace(dados.UfCrc) && !Uf.Valida(dados.UfCrc)) erros.Add("UF do CRC inválida.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var id = await repositorio.SalvarEscritorioAsync(dados with
        {
            Cnpj = cnpj, RazaoSocial = dados.RazaoSocial.Trim(), Crc = Limpar(dados.Crc)?.ToUpperInvariant(), UfCrc = Limpar(dados.UfCrc)?.ToUpperInvariant(),
            Email = Limpar(dados.Email)?.ToLowerInvariant(), Telefone = Limpar(dados.Telefone),
        }, ct);
        if ((await repositorio.ListarVinculosAsync(empresaId, ct)).Any(v => v.Vigente && v.EscritorioId == id))
            return Resultado<long>.Falha("Este escritório já está vinculado à empresa (dados cadastrais atualizados).");
        return await Gravar(() => repositorio.IncluirVinculoAsync(empresaId, null, id, inicio, false, null, usuarioId, ct));
    }

    public async Task<Resultado> EncerrarVinculoAsync(long empresaId, long vinculoId, DateOnly fim, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        var v = (await repositorio.ListarVinculosAsync(empresaId, ct)).FirstOrDefault(x => x.Id == vinculoId);
        if (v is null) return Resultado.Falha("Vínculo não encontrado.");
        if (!v.Vigente) return Resultado.Falha("O vínculo já está encerrado.");
        if (fim < v.Inicio) return Resultado.Falha($"A data de encerramento não pode ser anterior ao início ({v.Inicio:dd/MM/yyyy}).");
        return await GravarSemValor(() => repositorio.EncerrarVinculoAsync(empresaId, vinculoId, fim, usuarioId, ct));
    }

    public async Task<Resultado> DefinirResponsavelAsync(long empresaId, long vinculoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        var v = (await repositorio.ListarVinculosAsync(empresaId, ct)).FirstOrDefault(x => x.Id == vinculoId);
        if (v is null) return Resultado.Falha("Vínculo não encontrado.");
        if (v.ContadorId is null) return Resultado.Falha("O responsável técnico deve ser um contador (pessoa física com CRC).");
        if (string.IsNullOrWhiteSpace(v.Crc)) return Resultado.Falha("Informe o CRC do contador no cadastro para ele ser o responsável técnico.");
        if (!v.Vigente) return Resultado.Falha("O vínculo está encerrado.");
        return await GravarSemValor(() => repositorio.DefinirResponsavelAsync(empresaId, vinculoId, usuarioId, ct));
    }

    public async Task<Resultado> ExcluirVinculoAsync(long empresaId, long vinculoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        return await GravarSemValor(() => repositorio.ExcluirVinculoAsync(empresaId, vinculoId, usuarioId, ct));
    }

    public async Task<Resultado<DadosCnpj>> ConsultarCnpjAsync(string cnpj, CancellationToken ct = default)
    {
        var c = new string(cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (!Cnpj.Valido(c)) return Resultado<DadosCnpj>.Falha("CNPJ inválido.");
        if (consultaCnpj is null) return Resultado<DadosCnpj>.Falha("Consulta de CNPJ não configurada.");
        return await consultaCnpj.ConsultarAsync(c, ct);
    }

    // ------------------------------------------------------------------ sócios

    public async Task<Resultado<long>> SalvarSocioAsync(long empresaId, Socio s, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return negado;
        var doc = s.TipoPessoa == 'J'
            ? new string((s.CpfCnpj ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray())
            : Cnpj.SomenteDigitos(s.CpfCnpj);
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(s.Nome)) erros.Add("Informe o nome do sócio.");
        if (string.IsNullOrWhiteSpace(s.Qualificacao)) erros.Add("Informe a qualificação (ex.: Sócio-Administrador).");
        if (s.TipoPessoa is not ('F' or 'J' or 'E')) erros.Add("Tipo de pessoa inválido.");
        if (doc.Length > 0 && s.TipoPessoa == 'F' && !Cpf.Valido(doc)) erros.Add("CPF do sócio inválido.");
        if (doc.Length > 0 && s.TipoPessoa == 'J' && !Cnpj.Valido(doc)) erros.Add("CNPJ do sócio inválido.");
        if (s.Participacao is < 0 or > 100) erros.Add("Participação deve estar entre 0 e 100%.");
        if (s.DataEntrada is { } e && s.DataSaida is { } sa && sa < e) erros.Add("A data de saída não pode ser anterior à de entrada.");
        if (s.RepresentanteLegal && s.TipoPessoa == 'F' && doc.Length == 0)
            erros.Add("Informe o CPF do representante legal (exigido na assinatura da ECD e dos termos).");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var outros = (await repositorio.ListarSociosAsync(empresaId, ct)).Where(x => x.Id != s.Id && x.Ativo).ToArray();
        var soma = outros.Sum(x => x.Participacao ?? 0) + (s.DataSaida is null ? s.Participacao ?? 0 : 0);
        if (soma > 100) return Resultado<long>.Falha($"A soma das participações dos sócios ativos ficaria em {soma:0.####}% (máximo 100%).");

        return await Gravar(() => repositorio.SalvarSocioAsync(empresaId, s with
        {
            Nome = s.Nome.Trim(), CpfCnpj = doc.Length == 0 ? null : doc, Qualificacao = s.Qualificacao.Trim(),
            Email = Limpar(s.Email)?.ToLowerInvariant(), Telefone = Limpar(s.Telefone),
        }, usuarioId, ct));
    }

    public async Task<Resultado> ExcluirSocioAsync(long empresaId, long socioId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        return await GravarSemValor(() => repositorio.ExcluirSocioAsync(empresaId, socioId, usuarioId, ct));
    }

    // ------------------------------------------------------------------ síndico (condomínio)

    public async Task<Resultado<Sindico?>> ObterSindicoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<Sindico?>.Ok(await repositorio.ObterSindicoAsync(empresaId, ct))
            : Resultado<Sindico?>.Falha("Você não tem acesso a esta empresa.");

    /// <summary>Síndico que assina os livros (marcado "assina livros contábeis e fiscais"); nulo se não houver.</summary>
    public async Task<Sindico?> SindicoSignatarioAsync(long empresaId, CancellationToken ct = default)
        => await repositorio.ObterSindicoAsync(empresaId, ct) is { AssinaLivros: true } s ? s : null;

    public static IReadOnlyList<string> ValidarSindico(Sindico s)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(s.Nome)) erros.Add("Informe o nome do síndico.");
        var cpf = Digitos(s.Cpf);
        if (cpf.Length > 0 && !Cpf.Valido(cpf)) erros.Add("CPF do síndico inválido.");
        if (s.AssinaLivros && cpf.Length == 0) erros.Add("Informe o CPF do síndico para ele assinar os livros.");
        if (s.AssinaturaDigital && !s.AssinaLivros) erros.Add("Marque \"Assina os livros contábeis e fiscais\" para usar a assinatura digital.");
        if (s.MandatoInicio is { } i && s.MandatoFim is { } f && f < i) erros.Add("O fim do mandato não pode ser anterior ao início.");
        return erros;
    }

    public async Task<Resultado> SalvarSindicoAsync(long empresaId, Sindico s, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        if (ValidarSindico(s) is { Count: > 0 } erros) return Resultado.Falha(erros);
        var cpf = Digitos(s.Cpf);
        return await GravarSemValor(() => repositorio.SalvarSindicoAsync(empresaId, s with
        {
            Nome = s.Nome.Trim(), Cpf = cpf.Length == 0 ? null : cpf, Email = Limpar(s.Email)?.ToLowerInvariant(), Telefone = Limpar(s.Telefone),
        }, usuarioId, ct));
    }

    public async Task<Resultado> ExcluirSindicoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        return await GravarSemValor(() => repositorio.ExcluirSindicoAsync(empresaId, usuarioId, ct));
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    // ------------------------------------------------------------------ procuração eletrônica (contador procurador da empresa)

    public async Task<Resultado<IReadOnlyList<Procurador>>> ListarProcuradoresAsync(long empresaId, long usuarioId, CancellationToken ct = default)
        => (await autorizador.ObterAsync(usuarioId, empresaId, ct)).TemAcesso
            ? Resultado<IReadOnlyList<Procurador>>.Ok(await repositorio.ListarProcuradoresAsync(empresaId, ct))
            : Resultado<IReadOnlyList<Procurador>>.Falha("Você não tem acesso a esta empresa.");

    public Task<IReadOnlyList<VinculoContabil>> ListarVinculosParaTransmissaoAsync(long empresaId, CancellationToken ct = default)
        => repositorio.ListarVinculosAsync(empresaId, ct);

    /// <summary>
    /// Contador procurador com procuração vigente na data e que abrange a obrigação (ECD, ECF, EFD ou ESOCIAL); havendo mais de um,
    /// o de vigência iniciada mais recentemente. Nulo se não houver.
    /// </summary>
    public async Task<Procurador?> ProcuradorVigenteAsync(long empresaId, DateOnly data, string abrangencia, CancellationToken ct = default)
        => (await repositorio.ListarProcuradoresAsync(empresaId, ct)).Where(p => p.VigenteEm(data) && p.Abrange(abrangencia))
                                                                   .OrderByDescending(p => p.VigenciaInicio).FirstOrDefault();

    /// <summary>Procurador mais recente cadastrado (vigente ou não): serve para explicar por que a procuração não vale.</summary>
    public async Task<Procurador?> UltimoProcuradorAsync(long empresaId, CancellationToken ct = default)
        => (await repositorio.ListarProcuradoresAsync(empresaId, ct)).OrderByDescending(p => p.VigenciaInicio).FirstOrDefault();

    /// <summary>Marca/desmarca o contador do vínculo como procurador da empresa e grava a vigência, a abrangência e a observação.</summary>
    public async Task<Resultado> DefinirProcuracaoAsync(long empresaId, long vinculoId, Procuracao procuracao, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return Resultado.Falha(negado);
        var v = (await repositorio.ListarVinculosAsync(empresaId, ct)).FirstOrDefault(x => x.Id == vinculoId);
        if (v is null) return Resultado.Falha("Vínculo não encontrado.");
        if (v.ContadorId is null) return Resultado.Falha("Só uma pessoa física (contador) pode ser procuradora da empresa.");
        var erros = ValidarProcuracao(procuracao).ToList();
        if (erros.Count > 0) return Resultado.Falha(erros);
        return await GravarSemValor(() => repositorio.DefinirProcuracaoAsync(empresaId, vinculoId, NormalizarProcuracao(procuracao)!, usuarioId, ct));
    }

    private static IEnumerable<string> ValidarProcuracao(Procuracao? p)
    {
        if (p is not { Procurador: true }) yield break;
        if (p.Inicio is null) yield return "Informe o início da vigência da procuração.";
        if (p.Inicio is { } i && p.Fim is { } f && f < i) yield return "O fim da vigência da procuração não pode ser anterior ao início.";
        if (p.Abrangencia.Count == 0) yield return "Marque ao menos uma abrangência da procuração (ECD, ECF, EFD ou eSocial).";
        if (p.Abrangencia.Any(a => !AbrangenciaProcuracao.Todas.Contains(a.Trim().ToUpperInvariant()))) yield return "Abrangência da procuração inválida.";
    }

    /// <summary>Sem a marcação de procurador, os demais campos da procuração são descartados.</summary>
    private static Procuracao? NormalizarProcuracao(Procuracao? p)
        => p is not { Procurador: true } ? null
         : p with { Abrangencia = p.Abrangencia.Select(a => a.Trim().ToUpperInvariant()).Distinct().ToList(), Observacao = Limpar(p.Observacao) };

    /// <summary>Busca o QSA da empresa na base pública da RFB e inclui os sócios que ainda não estão cadastrados.</summary>
    public async Task<Resultado<int>> ImportarDaReceitaAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return negado;
        var empresa = await empresas.ObterAsync(empresaId, ct);
        if (empresa is null) return Resultado<int>.Falha("Empresa não encontrada.");
        var dados = await ConsultarCnpjAsync(empresa.Cnpj, ct);
        if (!dados.Sucesso) return Resultado<int>.Falha(dados.Erros);
        return await IncluirDoQsaAsync(empresaId, dados.Valor.QuadroSocietario, dados.Valor.Fonte, usuarioId, ct);
    }

    /// <summary>Inclui sócios vindos de uma consulta já feita (ex.: no cadastro da empresa), sem duplicar pelo nome.</summary>
    public async Task<Resultado<int>> IncluirDoQsaAsync(long empresaId, IReadOnlyList<SocioCnpj> qsa, string fonte, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.EmpresaResponsaveisManter, ct) is { } negado) return negado;
        var existentes = (await repositorio.ListarSociosAsync(empresaId, ct)).Select(s => LeitorPlanoReferencial.Normalizar(s.Nome)).ToHashSet();
        var temRepresentante = (await repositorio.ListarSociosAsync(empresaId, ct)).Any(s => s.Ativo && s.RepresentanteLegal);
        int incluidos = 0;
        foreach (var q in qsa.Where(q => !existentes.Contains(LeitorPlanoReferencial.Normalizar(q.Nome))))
        {
            await repositorio.SalvarSocioAsync(empresaId, new Socio(0, q.Nome.Trim(), q.TipoPessoa, null, q.DocumentoMascarado,
                q.Qualificacao ?? "Sócio", q.QualificacaoCodigo, null, q.DataEntrada, null, false, null, null,
                fonte.Length > 40 ? fonte[..40] : fonte), usuarioId, ct);
            incluidos++;
        }
        return Resultado<int>.Ok(incluidos);
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<Resultado<long>> Gravar(Func<Task<long>> acao)
    {
        try { return Resultado<long>.Ok(await acao()); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    private static async Task<Resultado> GravarSemValor(Func<Task> acao)
    {
        try { await acao(); return Resultado.Ok(); }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }
}
