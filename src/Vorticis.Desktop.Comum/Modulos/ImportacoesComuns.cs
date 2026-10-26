using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum.Modulos;

/// <summary>Importação de planilha dos cadastros do casco: clientes e fornecedores (participantes).</summary>
public static class ImportacoesComuns
{
    private static readonly CampoImportacao[] CamposParticipante =
    [
        new("codigo", "Código", false, "C001", "cod", "codigo_cliente", "codigo_fornecedor"),
        new("nome", "Nome / razão social", true, "Transportes Exemplo Ltda", "nome", "razao_social", "razao", "cliente", "fornecedor"),
        new("fantasia", "Nome fantasia", false, "Exemplo", "fantasia", "nome_fantasia"),
        new("cpfcnpj", "CPF/CNPJ", false, "11.222.333/0001-81", "cnpj", "cpf", "cnpj_cpf", "cpf_cnpj", "documento"),
        new("ie", "Inscrição estadual", false, "123456789", "ie", "inscricao_estadual", "insc_estadual"),
        new("cep", "CEP", false, "01310-100", "cep"),
        new("logradouro", "Logradouro", false, "Av. Paulista", "endereco", "logradouro", "rua"),
        new("numero", "Número", false, "1000", "numero", "nro", "num"),
        new("complemento", "Complemento", false, "Sala 1", "complemento"),
        new("bairro", "Bairro", false, "Bela Vista", "bairro"),
        new("municipio", "Município", false, "São Paulo", "municipio", "cidade"),
        new("ibge", "Código IBGE do município", false, "3550308", "ibge", "cod_ibge", "codigo_ibge", "cod_municipio"),
        new("uf", "UF", false, "SP", "uf", "estado"),
        new("email", "E-mail", false, "contato@exemplo.com.br", "email", "e_mail"),
        new("telefone", "Telefone", false, "(11) 3333-4444", "telefone", "fone", "celular"),
    ];

    private static string? Digitos(string? s) => s is null ? null : new string([.. s.Where(char.IsAsciiDigit)]) is { Length: > 0 } d ? d : null;

    private static DefinicaoImportacao Participantes(string chave, string titulo, bool cliente) => new(chave, titulo, CamposParticipante, async (ctx, l) =>
    {
        var p = new Participante(0, l.Texto("codigo") ?? "", l.Texto("nome") ?? "", l.Texto("fantasia"), Digitos(l.Texto("cpfcnpj")), l.Texto("ie"),
                                 l.Texto("uf")?.ToUpperInvariant(), Digitos(l.Texto("ibge")), l.Texto("municipio"), l.Texto("email"), l.Texto("telefone"),
                                 cliente, !cliente, null, true, Cep: Digitos(l.Texto("cep")), Logradouro: l.Texto("logradouro"), Numero: l.Texto("numero"),
                                 Complemento: l.Texto("complemento"), Bairro: l.Texto("bairro"));
        return await ctx.App.Participantes.GravarAsync(ctx.Empresa, p, ctx.Usuario);
    });

    public static IReadOnlyList<DefinicaoImportacao> Todas { get; } =
        [Participantes("clientes", "Clientes", true), Participantes("fornecedores", "Fornecedores", false)];
}
