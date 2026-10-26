using System.Security.Cryptography;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Contabil;

public sealed record TabelaReferencialImportada(string CodPlanRef, int Ano, int Contas, string Arquivo, DateTimeOffset ImportadoEm);

/// <summary>Linha do mapeamento: conta analítica da empresa e o vínculo referencial no ano.</summary>
public sealed record ContaMapeamento(long ContaId, string Codigo, string Descricao, string CodNat, bool TemMovimentoNoAno,
                                     string? CodigoReferencial, string? DescricaoReferencial);

public sealed record PendenciaReferencial(string Conta, string Descricao);

public interface IRepositorioPlanoReferencial
{
    Task<IReadOnlyList<TabelaReferencialImportada>> ListarTabelasAsync(CancellationToken ct);
    Task<IReadOnlyList<ContaReferencial>> ListarContasAsync(string codPlanRef, int ano, CancellationToken ct);

    /// <summary>
    /// Grava a tabela (inclui novas contas, atualiza existentes pelo código). Contas que saíram da tabela são
    /// removidas, exceto as que estão em uso em algum vínculo — essas são devolvidas para o usuário revisar.
    /// </summary>
    Task<IReadOnlyList<string>> GravarTabelaAsync(string codPlanRef, int ano, IReadOnlyList<ContaReferencial> contas, string arquivo, string sha256,
                                                  long usuarioId, CancellationToken ct);

    Task<string?> ObterPlanoDaEmpresaAsync(long empresaId, int ano, CancellationToken ct);
    Task DefinirPlanoDaEmpresaAsync(long empresaId, int ano, string codPlanRef, CancellationToken ct);
    Task<IReadOnlyList<ContaMapeamento>> ListarMapeamentoAsync(long empresaId, int ano, string codPlanRef, CancellationToken ct);

    /// <param name="vinculos">conta → código referencial (null remove o vínculo).</param>
    Task GravarVinculosAsync(long empresaId, int ano, string codPlanRef, IReadOnlyDictionary<long, string?> vinculos, long usuarioId, CancellationToken ct);
}

/// <summary>
/// Plano referencial (ECD I051 / ECF): a tabela oficial é importada pelo administrador a partir do arquivo da RFB;
/// cada empresa escolhe o tipo de plano do exercício e vincula suas contas analíticas a contas referenciais analíticas
/// de mesma natureza. Pendências (contas com movimento sem vínculo) impedem a geração da ECD.
/// </summary>
public sealed class ServicoPlanoReferencial(IRepositorioPlanoReferencial repositorio, Autorizador autorizador)
{
    public Task<IReadOnlyList<TabelaReferencialImportada>> ListarTabelasAsync(CancellationToken ct = default) => repositorio.ListarTabelasAsync(ct);

    public Task<IReadOnlyList<ContaReferencial>> ListarContasAsync(string codPlanRef, int ano, CancellationToken ct = default)
        => repositorio.ListarContasAsync(codPlanRef, ano, ct);

    public async Task<Resultado<string>> ImportarAsync(SessaoUsuario admin, string codPlanRef, int ano, byte[] arquivo, string nomeArquivo,
                                                      CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado<string>.Falha("Somente administradores importam tabelas da RFB.");
        if (TipoPlanoReferencial.Todos.All(t => t.Codigo != codPlanRef)) return Resultado<string>.Falha("Tipo de plano referencial inválido.");
        if (ano is < 2000 or > 2100) return Resultado<string>.Falha("Ano inválido.");
        var lido = LeitorPlanoReferencial.Ler(LeitorPlanoReferencial.Decodificar(arquivo));
        if (!lido.Sucesso) return Resultado<string>.Falha(lido.Erros);
        if (lido.Valor.Count(c => c.Analitica) == 0) return Resultado<string>.Falha("O arquivo não tem contas analíticas.");

        var mantidas = await repositorio.GravarTabelaAsync(codPlanRef, ano, lido.Valor, Path.GetFileName(nomeArquivo),
            Convert.ToHexStringLower(SHA256.HashData(arquivo)), admin.Id, ct);
        var resumo = $"{lido.Valor.Count} contas importadas ({lido.Valor.Count(c => c.Analitica)} analíticas) — plano {codPlanRef}, {ano}.";
        if (mantidas.Count > 0)
            resumo += $" {mantidas.Count} conta(s) não constam do novo arquivo mas estão vinculadas e foram mantidas: {string.Join(", ", mantidas.Take(10))}. Revise esses vínculos.";
        return Resultado<string>.Ok(resumo);
    }

    public async Task<Resultado<(string? Plano, IReadOnlyList<ContaMapeamento> Contas)>> CarregarAsync(long empresaId, int ano, long usuarioId,
                                                                                                      CancellationToken ct = default)
    {
        var p = await autorizador.ObterAsync(usuarioId, empresaId, ct);
        if (!p.TemAcesso) return Resultado<(string?, IReadOnlyList<ContaMapeamento>)>.Falha("Você não tem acesso a esta empresa.");
        var plano = await repositorio.ObterPlanoDaEmpresaAsync(empresaId, ano, ct);
        IReadOnlyList<ContaMapeamento> contas = plano is null ? [] : await repositorio.ListarMapeamentoAsync(empresaId, ano, plano, ct);
        return Resultado<(string?, IReadOnlyList<ContaMapeamento>)>.Ok((plano, contas));
    }

    public async Task<Resultado> DefinirPlanoAsync(long empresaId, int ano, string codPlanRef, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoReferencialMapear, ct) is { } negado) return Resultado.Falha(negado);
        if ((await repositorio.ListarTabelasAsync(ct)).All(t => t.CodPlanRef != codPlanRef || t.Ano != ano))
            return Resultado.Falha($"A tabela referencial {codPlanRef} de {ano} ainda não foi importada (Configurações › Plano referencial da RFB).");
        await repositorio.DefinirPlanoDaEmpresaAsync(empresaId, ano, codPlanRef, ct);
        return Resultado.Ok();
    }

    /// <summary>Grava vínculos validando: referencial existente, analítica e de mesma natureza da conta.</summary>
    public async Task<Resultado> VincularAsync(long empresaId, int ano, IReadOnlyDictionary<long, string?> vinculos, long usuarioId,
                                              CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.PlanoReferencialMapear, ct) is { } negado) return Resultado.Falha(negado);
        var plano = await repositorio.ObterPlanoDaEmpresaAsync(empresaId, ano, ct);
        if (plano is null) return Resultado.Falha("Escolha antes o plano referencial do exercício.");
        var referenciais = (await repositorio.ListarContasAsync(plano, ano, ct)).ToDictionary(r => r.Codigo);
        var contas = (await repositorio.ListarMapeamentoAsync(empresaId, ano, plano, ct)).ToDictionary(c => c.ContaId);
        var erros = new List<string>();
        foreach (var (contaId, codigo) in vinculos)
        {
            if (!contas.TryGetValue(contaId, out var conta)) { erros.Add($"Conta {contaId} não é analítica desta empresa."); continue; }
            if (codigo is null) continue;
            if (!referenciais.TryGetValue(codigo, out var r)) erros.Add($"{conta.Codigo}: referencial {codigo} não existe na tabela {plano}/{ano}.");
            else if (!r.Analitica) erros.Add($"{conta.Codigo}: referencial {codigo} é sintética.");
            else if (r.CodNat != conta.CodNat) erros.Add($"{conta.Codigo}: natureza {conta.CodNat} incompatível com a referencial {codigo} ({r.CodNat}).");
        }
        if (erros.Count > 0) return Resultado.Falha(erros);
        try
        {
            await repositorio.GravarVinculosAsync(empresaId, ano, plano, vinculos, usuarioId, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex)
        {
            return Resultado.Falha(ex.Message);
        }
    }

    /// <summary>Sugestões para as contas ainda sem vínculo (não grava nada).</summary>
    public async Task<IReadOnlyDictionary<long, (ContaReferencial Conta, double Pontuacao)>> SugerirAsync(long empresaId, int ano, long usuarioId,
                                                                                                          CancellationToken ct = default)
    {
        var dados = await CarregarAsync(empresaId, ano, usuarioId, ct);
        if (!dados.Sucesso || dados.Valor.Plano is null) return new Dictionary<long, (ContaReferencial, double)>();
        var referenciais = await repositorio.ListarContasAsync(dados.Valor.Plano, ano, ct);
        var res = new Dictionary<long, (ContaReferencial, double)>();
        foreach (var c in dados.Valor.Contas.Where(c => c.CodigoReferencial is null))
            if (SugestaoReferencial.Sugerir(c.Descricao, c.CodNat, referenciais) is { } s) res[c.ContaId] = s;
        return res;
    }

    /// <summary>Copia os vínculos do ano anterior para as contas ainda sem vínculo, quando o código referencial existe na tabela do ano.</summary>
    public async Task<Resultado<int>> CopiarDoAnoAnteriorAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<int>(usuarioId, empresaId, Permissao.PlanoReferencialMapear, ct) is { } negado) return negado;
        var plano = await repositorio.ObterPlanoDaEmpresaAsync(empresaId, ano, ct);
        var planoAnterior = await repositorio.ObterPlanoDaEmpresaAsync(empresaId, ano - 1, ct);
        if (plano is null) return Resultado<int>.Falha("Escolha antes o plano referencial do exercício.");
        if (planoAnterior is null) return Resultado<int>.Falha($"O exercício {ano - 1} não tem plano referencial definido.");
        var anteriores = (await repositorio.ListarMapeamentoAsync(empresaId, ano - 1, planoAnterior, ct))
                         .Where(c => c.CodigoReferencial is not null).ToDictionary(c => c.ContaId, c => c.CodigoReferencial!);
        var codigosAno = (await repositorio.ListarContasAsync(plano, ano, ct)).Where(r => r.Analitica).Select(r => r.Codigo).ToHashSet();
        var atuais = await repositorio.ListarMapeamentoAsync(empresaId, ano, plano, ct);
        var copiar = atuais.Where(c => c.CodigoReferencial is null && anteriores.TryGetValue(c.ContaId, out var r) && codigosAno.Contains(r))
                           .ToDictionary(c => c.ContaId, c => (string?)anteriores[c.ContaId]);
        if (copiar.Count == 0) return Resultado<int>.Ok(0);
        var r = await VincularAsync(empresaId, ano, copiar, usuarioId, ct);
        return r.Sucesso ? Resultado<int>.Ok(copiar.Count) : Resultado<int>.Falha(r.Erros);
    }

    /// <summary>Contas analíticas com movimento ou saldo no ano sem vínculo — impedem a ECD (registro I051 obrigatório).</summary>
    public async Task<Resultado<IReadOnlyList<PendenciaReferencial>>> PendenciasAsync(long empresaId, int ano, long usuarioId, CancellationToken ct = default)
    {
        var dados = await CarregarAsync(empresaId, ano, usuarioId, ct);
        if (!dados.Sucesso) return Resultado<IReadOnlyList<PendenciaReferencial>>.Falha(dados.Erros);
        if (dados.Valor.Plano is null)
            return Resultado<IReadOnlyList<PendenciaReferencial>>.Ok([new("—", $"Plano referencial do exercício {ano} não definido.")]);
        return Resultado<IReadOnlyList<PendenciaReferencial>>.Ok(dados.Valor.Contas
            .Where(c => c.TemMovimentoNoAno && c.CodigoReferencial is null)
            .Select(c => new PendenciaReferencial(c.Codigo, $"{c.Descricao}: sem vínculo ao plano referencial"))
            .ToArray());
    }
}
