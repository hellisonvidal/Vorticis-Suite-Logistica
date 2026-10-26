using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Sistema;

/// <summary>Titular da licença de uso (a quem o sistema está registrado), exibido em Ajuda › Licença e registro.</summary>
public sealed record RegistroLicenca(string? Titular, string? Documento, string? Email, DateTimeOffset? RegistradoEm)
{
    public bool Registrado => !string.IsNullOrWhiteSpace(Titular);

    public string DocumentoFormatado => Documento switch
    {
        { Length: 11 } d => Cpf.Formatar(d),
        { Length: 14 } d => Cnpj.Formatar(d),
        var d => d ?? "",
    };
}

public interface IRepositorioConfiguracao
{
    Task<IReadOnlyDictionary<string, (string Valor, DateTimeOffset AlteradoEm)>> LerAsync(IEnumerable<string> chaves, CancellationToken ct);
    Task GravarAsync(IReadOnlyDictionary<string, string?> valores, long? usuarioId, CancellationToken ct);
    /// <summary>
    /// Apaga empresas, cadastros e todos os usuários (core.fn_limpar_banco): o banco volta ao estado de instalação nova,
    /// onde só vale o usuário de implantação. Retorna quantas empresas foram excluídas.
    /// </summary>
    Task<int> LimparBancoAsync(long usuarioId, CancellationToken ct);
}

public sealed class ServicoLicenca(IRepositorioConfiguracao repositorio)
{
    private const string ChaveTitular = "licenca_titular", ChaveDocumento = "licenca_documento", ChaveEmail = "licenca_email";

    public async Task<RegistroLicenca> ObterAsync(CancellationToken ct = default)
    {
        var v = await repositorio.LerAsync([ChaveTitular, ChaveDocumento, ChaveEmail], ct);
        string? Ler(string k) => v.TryGetValue(k, out var x) && x.Valor.Length > 0 ? x.Valor : null;
        return new RegistroLicenca(Ler(ChaveTitular), Ler(ChaveDocumento), Ler(ChaveEmail),
                                   v.TryGetValue(ChaveTitular, out var t) ? t.AlteradoEm : null);
    }

    /// <summary>Registra o titular da licença (pessoa ou escritório). Somente o administrador.</summary>
    public async Task<Resultado> RegistrarAsync(SessaoUsuario admin, string titular, string? documento, string? email, CancellationToken ct = default)
    {
        if (!admin.Administrador) return Resultado.Falha("Somente o administrador registra a licença.");
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(titular) || titular.Trim().Length < 3) erros.Add("Informe o nome do titular da licença.");
        var doc = Cnpj.SomenteDigitos(documento);
        if (doc.Length > 0 && !(doc.Length == 11 ? Cpf.Valido(doc) : Cnpj.Valido(doc))) erros.Add("CPF ou CNPJ do titular inválido.");
        if (erros.Count > 0) return Resultado.Falha(erros);
        await repositorio.GravarAsync(new Dictionary<string, string?>
        {
            [ChaveTitular] = titular.Trim(), [ChaveDocumento] = doc, [ChaveEmail] = email?.Trim() ?? "",
        }, admin.Id, ct);
        return Resultado.Ok();
    }
}
