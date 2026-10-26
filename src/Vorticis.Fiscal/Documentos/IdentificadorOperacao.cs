using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;

namespace Vorticis.Fiscal.Documentos;

/// <summary>Entrada ou saída para a empresa e a outra parte da operação (cliente na saída, fornecedor na entrada).</summary>
public sealed record Identificacao(TipoOperacao Operacao, ParteDocumento? Contraparte);

/// <summary>
/// Identifica se o documento é de entrada ou de saída para a empresa, comparando o CNPJ dela (e de seus estabelecimentos)
/// com o do emitente e o do destinatário.
/// <list type="bullet">
/// <item>Emitente é a empresa: saída (NF-e com tpNF = 1 e NFS-e prestada); NF-e de entrada própria (tpNF = 0), como a nota de devolução ou de produtor, é entrada.</item>
/// <item>Destinatário/tomador é a empresa: entrada.</item>
/// <item>Nenhum dos dois: o documento não pertence à empresa e é recusado.</item>
/// </list>
/// </summary>
public static class IdentificadorOperacao
{
    public static Resultado<Identificacao> Identificar(DocumentoLido doc, IEnumerable<string> cnpjsEmpresa)
    {
        var meus = cnpjsEmpresa.Select(Cnpj.SomenteDigitos).Where(c => c.Length > 0).ToHashSet();
        var emitente = Cnpj.SomenteDigitos(doc.Emitente.Documento);
        var destinatario = Cnpj.SomenteDigitos(doc.Destinatario?.Documento);

        if (emitente.Length > 0 && meus.Contains(emitente))
        {
            // Nota de entrada emitida pela própria empresa: a contraparte é o remetente, informado como destinatário no XML.
            return doc.Modelo != ModeloDocumento.NFSe && doc.TipoNf == 0
                ? Resultado<Identificacao>.Ok(new Identificacao(TipoOperacao.Entrada, doc.Destinatario))
                : Resultado<Identificacao>.Ok(new Identificacao(TipoOperacao.Saida, doc.Destinatario));
        }
        if (destinatario.Length > 0 && meus.Contains(destinatario))
            return Resultado<Identificacao>.Ok(new Identificacao(TipoOperacao.Entrada, doc.Emitente));
        return Resultado<Identificacao>.Falha(
            $"O documento não pertence à empresa: emitente {Mascara(emitente)}, destinatário {Mascara(destinatario)}.");
    }

    private static string Mascara(string doc) => doc.Length == 0 ? "(não informado)" : doc.Length == 14 ? Cnpj.Formatar(doc) : doc;
}
