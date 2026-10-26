using Vorticis.Application.Cadastros;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Infra;

namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// Resultado da busca do fornecedor/cliente pelo CPF/CNPJ digitado na nota: <see cref="Cadastrado"/> (já existe na empresa) ou
/// <see cref="Novo"/> (dados para cadastrar ao gravar a nota; <see cref="Participante.Id"/> = 0). Os dois nulos = o texto não é um CPF/CNPJ.
/// </summary>
public sealed record ParticipanteResolvido(Participante? Cadastrado, Participante? Novo, string? Aviso = null, string? Fonte = null);

/// <summary>
/// Busca automática do participante na digitação da nota: primeiro entre os cadastrados da empresa; se não houver, consulta a base
/// pública do CNPJ (a mesma do Cadastro de Empresas) para pré-preencher razão social, endereço, UF, município e inscrição estadual.
/// CPF aceito, sem consulta pública. O cadastro só acontece ao gravar a nota, depois da confirmação do usuário.
/// </summary>
public sealed class ServicoParticipanteNota(VorticisApp app)
{
    /// <summary>O texto digitado parece um CPF/CNPJ (só dígitos e pontuação) com 11 ou 14 dígitos? Devolve os dígitos.</summary>
    public static string? DocumentoDigitado(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Any(c => !char.IsAsciiDigit(c) && ".-/ ".IndexOf(c) < 0)) return null;
        var d = Cnpj.SomenteDigitos(texto);
        return d.Length is 11 or 14 ? d : null;
    }

    public async Task<Resultado<ParticipanteResolvido>> BuscarAsync(long empresaId, string? texto, TipoOperacao operacao, long usuarioId, CancellationToken ct = default)
    {
        if (DocumentoDigitado(texto) is not { } doc) return Resultado<ParticipanteResolvido>.Ok(new ParticipanteResolvido(null, null));
        if (!(doc.Length == 11 ? Cpf.Valido(doc) : Cnpj.Valido(doc)))
            return Resultado<ParticipanteResolvido>.Falha($"{(doc.Length == 11 ? "CPF" : "CNPJ")} inválido: confira os dígitos.");

        var existente = (await app.Participantes.ListarAsync(empresaId, usuarioId, ct)).FirstOrDefault(p => p.CpfCnpj == doc);
        if (existente is not null) return Resultado<ParticipanteResolvido>.Ok(new ParticipanteResolvido(existente, null));

        bool cliente = operacao == TipoOperacao.Saida, fornecedor = !cliente;
        if (doc.Length == 11)
            return Resultado<ParticipanteResolvido>.Ok(new ParticipanteResolvido(null,
                Novo(doc, "", cliente, fornecedor), "CPF não cadastrado: informe o nome (não há consulta pública de CPF)."));

        var r = await app.Empresas.ConsultarCnpjAsync(doc, ct);
        if (!r.Sucesso)
            return Resultado<ParticipanteResolvido>.Ok(new ParticipanteResolvido(null, Novo(doc, "", cliente, fornecedor),
                $"CNPJ não cadastrado e a consulta pública falhou ({string.Join(" ", r.Erros)}). Informe os dados manualmente."));
        var d = r.Valor;
        string? ie = null, aviso = null;
        var consultaIe = await app.Empresas.ConsultarInscricaoEstadualAsync(doc, d.Uf, d.CodMunicipioIbge, ct);
        if (consultaIe.Sucesso) ie = consultaIe.Valor.FirstOrDefault(i => i.Ativa)?.Inscricao ?? consultaIe.Valor.FirstOrDefault()?.Inscricao;
        else aviso = $"Inscrição estadual não consultada: {string.Join(" ", consultaIe.Erros)}";
        if (!d.Ativa && d.SituacaoCadastral is { } sit) aviso = $"Atenção: situação cadastral {sit}. {aviso}".Trim();
        var novo = new Participante(0, "", d.RazaoSocial, d.NomeFantasia, doc, ie, d.Uf, d.CodMunicipioIbge, d.Municipio, d.Email, d.Telefone,
            cliente, fornecedor, null, true, false, d.Cep, d.Logradouro, d.Numero, d.Complemento, d.Bairro);
        return Resultado<ParticipanteResolvido>.Ok(new ParticipanteResolvido(null, novo, aviso, d.Fonte));
    }

    /// <summary>
    /// Completa o cadastro do fornecedor/cliente criado a partir do XML com a consulta pública do CNPJ e da inscrição estadual, como o botão
    /// "Buscar dados na Receita" do cadastro de clientes e fornecedores: preenche só o que está vazio (os dados do XML são mantidos).
    /// Devolve o nome do participante completado ou o motivo de não ter completado.
    /// </summary>
    public async Task<Resultado<string>> CompletarPelaConsultaAsync(long empresaId, string documento, long usuarioId, CancellationToken ct = default)
    {
        var doc = Cnpj.SomenteDigitos(documento);
        if (doc.Length != 14) return Resultado<string>.Falha("CPF: não há consulta pública.");
        var p = (await app.Participantes.ListarAsync(empresaId, usuarioId, ct)).FirstOrDefault(x => x.CpfCnpj == doc);
        if (p is null) return Resultado<string>.Falha("Participante não encontrado.");
        var r = await app.Empresas.ConsultarCnpjAsync(doc, ct);
        if (!r.Sucesso) return Resultado<string>.Falha($"{p.Nome}: consulta do CNPJ falhou ({string.Join(" ", r.Erros)}).");
        var d = r.Valor;
        static string? V(string? atual, string? novo) => string.IsNullOrWhiteSpace(atual) ? (string.IsNullOrWhiteSpace(novo) ? atual : novo.Trim()) : atual;
        var uf = V(p.Uf, d.Uf);
        var ie = p.InscricaoEstadual;
        if (string.IsNullOrWhiteSpace(ie))
        {
            var consultaIe = await app.Empresas.ConsultarInscricaoEstadualAsync(doc, uf, V(p.CodMunicipioIbge, d.CodMunicipioIbge), ct);
            if (consultaIe.Sucesso) ie = consultaIe.Valor.FirstOrDefault(i => i.Ativa)?.Inscricao ?? consultaIe.Valor.FirstOrDefault()?.Inscricao ?? "ISENTO";
        }
        var completo = p with
        {
            Nome = V(p.Nome, d.RazaoSocial)!, NomeFantasia = V(p.NomeFantasia, d.NomeFantasia), InscricaoEstadual = ie, Uf = uf,
            CodMunicipioIbge = V(p.CodMunicipioIbge, d.CodMunicipioIbge), Municipio = V(p.Municipio, d.Municipio), Email = V(p.Email, d.Email),
            Telefone = V(p.Telefone, d.Telefone), Cep = V(p.Cep, d.Cep), Logradouro = V(p.Logradouro, d.Logradouro), Numero = V(p.Numero, d.Numero),
            Complemento = V(p.Complemento, d.Complemento), Bairro = V(p.Bairro, d.Bairro),
        };
        if (completo == p) return Resultado<string>.Ok(p.Nome);
        var g = await app.Participantes.GravarAsync(empresaId, completo, usuarioId, ct);
        return g.Sucesso ? Resultado<string>.Ok(p.Nome) : Resultado<string>.Falha($"{p.Nome}: {string.Join(" ", g.Erros)}");
    }

    private static Participante Novo(string doc, string nome, bool cliente, bool fornecedor) =>
        new(0, "", nome, null, doc, null, null, null, null, null, null, cliente, fornecedor, null, true);

    /// <summary>Cadastra o participante novo (cliente na saída, fornecedor na entrada) e devolve o cadastro com id e código.</summary>
    public async Task<Resultado<Participante>> CadastrarAsync(long empresaId, Participante novo, long usuarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(novo.Nome)) return Resultado<Participante>.Falha("Informe o nome ou a razão social do participante.");
        var g = await app.Participantes.GravarAsync(empresaId, novo with { Id = 0, Codigo = "", Ativo = true }, usuarioId, ct);
        if (!g.Sucesso) return Resultado<Participante>.Falha(g.Erros);
        var p = (await app.Participantes.ListarAsync(empresaId, usuarioId, ct)).FirstOrDefault(x => x.Id == g.Valor);
        return p is null ? Resultado<Participante>.Falha("Participante gravado, mas não encontrado.") : Resultado<Participante>.Ok(p);
    }
}
