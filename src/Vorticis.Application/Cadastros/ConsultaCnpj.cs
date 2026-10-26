using Vorticis.Domain.Comum;

namespace Vorticis.Application.Cadastros;

/// <summary>Dados cadastrais de um CNPJ obtidos de fonte externa.</summary>
public sealed record DadosCnpj(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    string? NaturezaJuridica,      // código CONCLA, 4 dígitos
    string? CnaePrincipal,         // 7 dígitos
    string? Uf,
    string? CodMunicipioIbge,      // 7 dígitos
    string? Municipio,
    DateOnly? DataInicioAtividades,
    string? SituacaoCadastral,     // ATIVA, BAIXADA, INAPTA, SUSPENSA, NULA
    bool? OptanteSimples,
    bool? OptanteMei,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Email,
    string? Telefone,
    string Fonte,
    DateTimeOffset ConsultadoEm,
    IReadOnlyList<SocioCnpj>? Socios = null)
{
    public IReadOnlyList<SocioCnpj> QuadroSocietario => Socios ?? [];

    /// <summary>Regime sugerido a partir das opções públicas. Presumido/Real não constam da base pública.</summary>
    public RegimeTributario? RegimeSugerido =>
        OptanteMei == true ? RegimeTributario.MEI : OptanteSimples == true ? RegimeTributario.SimplesNacional : null;

    public bool Ativa => string.Equals(SituacaoCadastral, "ATIVA", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Sócio/administrador do QSA na base pública. O CPF vem mascarado (ex.: ***123456**) — completar manualmente.</summary>
public sealed record SocioCnpj(string Nome, string? DocumentoMascarado, string? Qualificacao, int? QualificacaoCodigo,
                               DateOnly? DataEntrada, char TipoPessoa);

/// <summary>
/// Fonte de dados cadastrais de CNPJ. Implementações: base pública (dados abertos da RFB via BrasilAPI/Minha Receita);
/// futuramente API oficial paga (Serpro "Consulta CNPJ").
/// </summary>
public interface IConsultaCnpj
{
    Task<Resultado<DadosCnpj>> ConsultarAsync(string cnpj, CancellationToken ct);
}

/// <summary>Inscrição estadual encontrada em fonte pública (cadastros estaduais / SINTEGRA).</summary>
public sealed record InscricaoEstadualEncontrada(string Uf, string Inscricao, bool Ativa, string Fonte, DateTimeOffset ConsultadoEm);

/// <summary>Consulta as inscrições estaduais de um CNPJ (todas as UFs em que estiver inscrito).</summary>
public interface IConsultaInscricaoEstadual
{
    Task<Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>> ConsultarAsync(string cnpj, CancellationToken ct);
}
