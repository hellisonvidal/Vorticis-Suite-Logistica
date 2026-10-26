using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Produtos;

namespace Vorticis.Fiscal.Documentos;

/// <summary>
/// Nota fiscal digitada (entrada ou saída). Os <see cref="Itens"/> são <see cref="ItemFiscal"/> com os campos de entrada preenchidos
/// (produto, CFOP, quantidade, valor unitário, descontos, tributos); valor do produto e valor contábil são calculados na gravação.
/// <see cref="Totais"/> traz os valores do cabeçalho e é conferido com a soma dos itens.
/// </summary>
public sealed record DocumentoDigitado(
    long Id, TipoOperacao Operacao, ModeloDocumento Modelo, string Serie, string Numero, string? Chave, DateOnly Emissao, DateOnly DataLivro,
    string? Natureza, long ParticipanteId, long? CentroCustoId, TotaisDocumento Totais, IReadOnlyList<ItemFiscal> Itens,
    bool Contabilizado = false, string Origem = "DIGITADO", string? Observacoes = null, string? ContaDebito = null, string? ContaCredito = null,
    IReadOnlyList<Vorticis.Fiscal.Duplicatas.ParcelaDuplicata>? Parcelas = null, long? FormaPagamentoId = null)
{
    // Parcelas (duplicatas): null = não mexe nas duplicatas da nota; lista = parcelas da cobrança (nota nova sem parcelas gera uma à vista, conforme a configuração).
    /// <summary>Nota importada de XML: valores, chave e itens do XML original não se alteram; só a escrituração.</summary>
    public bool Importado => Origem == "XML";
}

/// <summary>
/// Escrituração de um item: CFOP de escrituração (em branco = o do documento), produto vinculado e centro de custo; e, se informados,
/// os tributos do item (CST, base, alíquota e valor de ICMS, ICMS-ST, IPI, PIS e COFINS), que substituem os do XML.
/// </summary>
public sealed record EscrituracaoItem(int Numero, string? Cfop, long? ProdutoId, long? CentroCustoId, TributosItem? Tributos = null);

/// <summary>Tributos de um item, como ficam na escrituração (vindos do XML ou alterados pelo usuário).</summary>
public sealed record TributosItem(string? CstIcms, decimal BcIcms, decimal AliqIcms, decimal Icms, decimal BcIcmsSt, decimal IcmsSt,
                                  string? CstIpi, decimal BcIpi, decimal AliqIpi, decimal Ipi,
                                  string? CstPis, decimal BcPis, decimal AliqPis, decimal Pis,
                                  string? CstCofins, decimal BcCofins, decimal AliqCofins, decimal Cofins)
{
    public static TributosItem De(ItemFiscal i) => new(i.CstIcms, i.BcIcms, i.AliqIcms, i.Icms, i.BcIcmsSt, i.IcmsSt, i.CstIpi, i.BcIpi, i.AliqIpi, i.Ipi,
                                                       i.CstPis, i.BcPis, i.AliqPis, i.Pis, i.CstCofins, i.BcCofins, i.AliqCofins, i.Cofins);
}

/// <summary>
/// Campos de escrituração, editáveis inclusive nas notas importadas: observações, centro de custo, contas de contabilização
/// (sobrepõem as da regra; código da conta analítica) e, por item, CFOP, produto e centro de custo.
/// </summary>
public sealed record EscrituracaoDocumento(long DocumentoId, string? Observacoes, long? CentroCustoId, string? ContaDebito, string? ContaCredito,
                                           IReadOnlyList<EscrituracaoItem> Itens);

/// <summary>Participante como usado na digitação (a outra parte da nota).</summary>
public sealed record ParticipanteNota(long Id, string Nome, string? CpfCnpj, string? Uf);

/// <summary>Opções do Fiscal da empresa. <see cref="UsaCentroCusto"/> só vale com o uso de centros de custo ligado no cadastro da empresa.</summary>
public sealed record ConfiguracaoFiscal(bool UsaCentroCusto, bool EmpresaUsaCentroCusto)
{
    public bool CentroCustoAtivo => UsaCentroCusto && EmpresaUsaCentroCusto;
}

public interface IRepositorioDigitacao
{
    Task<ConfiguracaoFiscal> ObterConfiguracaoAsync(long empresaId, CancellationToken ct);
    Task GravarConfiguracaoAsync(long empresaId, bool usaCentroCusto, CancellationToken ct);
    Task<ParticipanteNota?> ObterParticipanteAsync(long empresaId, long participanteId, CancellationToken ct);
    Task<IReadOnlyDictionary<long, Produto>> ObterProdutosAsync(long empresaId, IEnumerable<long> ids, CancellationToken ct);

    /// <summary>Dos ids informados, os que existem na empresa como centro de custo ativo.</summary>
    Task<IReadOnlySet<long>> CentrosValidosAsync(long empresaId, IEnumerable<long> ids, CancellationToken ct);

    /// <summary>Inclui (Id = 0) ou substitui o documento digitado e seus itens numa transação. Recusa documento XML, contabilizado ou repetido.</summary>
    Task<long> GravarAsync(long empresaId, DocumentoDigitado doc, EmpresaFiscal empresa, ParticipanteNota participante, long usuarioId, CancellationToken ct);

    Task<DocumentoDigitado?> ObterAsync(long empresaId, long documentoId, CancellationToken ct);

    Task<bool> ContaAnaliticaExisteAsync(long empresaId, string codigo, CancellationToken ct);

    /// <summary>Grava somente a escrituração (nunca os dados do XML). Recusa nota contabilizada ou inexistente.</summary>
    Task GravarEscrituracaoAsync(long empresaId, EscrituracaoDocumento escrituracao, long usuarioId, CancellationToken ct);
}

/// <summary>Cálculos da nota digitada, sem banco.</summary>
public static class CalculadoraNota
{
    /// <summary>Valor do produto = quantidade x unitário (2 casas); valor contábil = produto - desconto + frete + seguro + outras + IPI + ICMS-ST.</summary>
    public static ItemFiscal Completar(ItemFiscal i)
    {
        var produto = Dinheiro.Arredondar(i.Quantidade * i.ValorUnitario);
        return i with { ValorProduto = produto, ValorContabil = Dinheiro.Arredondar(produto - i.Desconto + i.Frete + i.Seguro + i.Outras + i.Ipi + i.IcmsSt) };
    }

    /// <summary>Totais do cabeçalho pela soma dos itens (as retenções e o ISS retido vêm do cabeçalho).</summary>
    public static TotaisDocumento Totalizar(IEnumerable<ItemFiscal> itens, TotaisDocumento retencoes)
    {
        var l = itens.Select(Completar).ToList();
        return retencoes with
        {
            Produtos = l.Sum(i => i.ValorProduto), Desconto = l.Sum(i => i.Desconto), Frete = l.Sum(i => i.Frete), Seguro = l.Sum(i => i.Seguro),
            Outras = l.Sum(i => i.Outras), Total = l.Sum(i => i.ValorContabil), BcIcms = l.Sum(i => i.BcIcms), Icms = l.Sum(i => i.Icms),
            BcIcmsSt = l.Sum(i => i.BcIcmsSt), IcmsSt = l.Sum(i => i.IcmsSt), Ipi = l.Sum(i => i.Ipi), Pis = l.Sum(i => i.Pis), Cofins = l.Sum(i => i.Cofins),
            Iss = l.Sum(i => i.Iss),
        };
    }

    /// <summary>Diferenças (texto) entre os totais informados e os calculados pelos itens; vazio quando conferem (tolerância de um centavo).</summary>
    public static IReadOnlyList<string> Conferir(TotaisDocumento informado, TotaisDocumento calculado)
    {
        var erros = new List<string>();
        void C(string nome, decimal a, decimal b)
        {
            if (Math.Abs(a - b) > 0.01m)
                erros.Add($"{nome} do cabeçalho ({Formato.Moeda(a)}) difere da soma dos itens ({Formato.Moeda(b)}).");
        }
        C("Valor dos produtos", informado.Produtos, calculado.Produtos);
        C("Desconto", informado.Desconto, calculado.Desconto);
        C("Frete", informado.Frete, calculado.Frete);
        C("Seguro", informado.Seguro, calculado.Seguro);
        C("Outras despesas", informado.Outras, calculado.Outras);
        C("Valor total da nota", informado.Total, calculado.Total);
        C("Base do ICMS", informado.BcIcms, calculado.BcIcms);
        C("ICMS", informado.Icms, calculado.Icms);
        C("Base do ICMS-ST", informado.BcIcmsSt, calculado.BcIcmsSt);
        C("ICMS-ST", informado.IcmsSt, calculado.IcmsSt);
        C("IPI", informado.Ipi, calculado.Ipi);
        C("PIS", informado.Pis, calculado.Pis);
        C("COFINS", informado.Cofins, calculado.Cofins);
        C("ISS", informado.Iss, calculado.Iss);
        return erros;
    }

    /// <summary>Dígito verificador (módulo 11) da chave de acesso de 44 dígitos da NF-e/NFC-e.</summary>
    public static bool ChaveNfeValida(string chave)
    {
        if (chave.Length != 44 || !chave.All(char.IsAsciiDigit)) return false;
        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        var dv = resto < 2 ? 0 : 11 - resto;
        return dv == chave[43] - '0';
    }
}

/// <summary>
/// Digitação manual de notas fiscais de entrada e de saída, gravadas nas mesmas tabelas da importação de XML (origem DIGITADO).
/// Exige a função de importar/manter documentos. Nota contabilizada não pode ser alterada (nem excluída): desfaça a contabilização antes.
/// </summary>
public sealed class ServicoDigitacaoFiscal(IRepositorioDigitacao repositorio, IRepositorioDocumentos documentos, Autorizador autorizador)
{
    // ------------------------------------------------------------------ configuração

    public async Task<Resultado<ConfiguracaoFiscal>> ObterConfiguracaoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ConfiguracaoFiscal>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<ConfiguracaoFiscal>.Ok(await repositorio.ObterConfiguracaoAsync(empresaId, ct));
    }

    /// <summary>Liga ou desliga "Usar centros de custo no fiscal". Para ligar, a empresa precisa usar centros de custo (cadastro da empresa, aba Contabilidade).</summary>
    public async Task<Resultado> GravarConfiguracaoAsync(long empresaId, bool usaCentroCusto, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return Resultado.Falha(negado);
        var atual = await repositorio.ObterConfiguracaoAsync(empresaId, ct);
        if (usaCentroCusto && !atual.EmpresaUsaCentroCusto)
            return Resultado.Falha("A empresa não usa centros de custo: ative essa opção no cadastro da empresa (aba Contabilidade) antes.");
        await repositorio.GravarConfiguracaoAsync(empresaId, usaCentroCusto, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ digitação

    public async Task<Resultado<DocumentoDigitado>> ObterAsync(long empresaId, long documentoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<DocumentoDigitado>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return await repositorio.ObterAsync(empresaId, documentoId, ct) is { } d ? Resultado<DocumentoDigitado>.Ok(d) : Resultado<DocumentoDigitado>.Falha("Documento não encontrado.");
    }

    /// <summary>Inclui (Id = 0) ou altera a nota digitada, depois de validar cabeçalho, itens, centros de custo e conferir os totais.</summary>
    public async Task<Resultado<long>> GravarAsync(long empresaId, DocumentoDigitado doc, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return negado;
        var empresa = await documentos.ObterEmpresaAsync(empresaId, ct);
        if (empresa is null) return Resultado<long>.Falha("Empresa não encontrada.");
        var config = await repositorio.ObterConfiguracaoAsync(empresaId, ct);

        var erros = new List<string>();
        var numero = (doc.Numero ?? "").Trim();
        var serie = (doc.Serie ?? "").Trim();
        if (numero.Length == 0) erros.Add("Informe o número da nota.");
        else if (numero.Length > 20) erros.Add("O número da nota pode ter até 20 caracteres.");
        if (serie.Length > 10) erros.Add("A série pode ter até 10 caracteres.");
        if (doc.Emissao == default) erros.Add("Informe a data de emissão.");
        if (doc.DataLivro == default) erros.Add($"Informe a data de {(doc.Operacao == TipoOperacao.Entrada ? "entrada" : "saída")}.");
        else if (doc.Emissao != default && doc.DataLivro < doc.Emissao && doc.Operacao == TipoOperacao.Entrada)
            erros.Add("A data de entrada não pode ser anterior à emissão.");

        string? chave = null;
        if (!string.IsNullOrWhiteSpace(doc.Chave))
        {
            chave = new string(doc.Chave.Where(char.IsAsciiDigit).ToArray());
            if (doc.Modelo == ModeloDocumento.NFSe) { if (chave.Length != 50) erros.Add("A chave da NFS-e nacional tem 50 dígitos."); }
            else if (!CalculadoraNota.ChaveNfeValida(chave)) erros.Add("Chave de acesso inválida: são 44 dígitos com dígito verificador correto.");
        }

        var participante = await repositorio.ObterParticipanteAsync(empresaId, doc.ParticipanteId, ct);
        if (participante is null) erros.Add($"Informe o {(doc.Operacao == TipoOperacao.Entrada ? "fornecedor" : "cliente")} (participante cadastrado).");

        if (doc.Itens.Count == 0) erros.Add("Informe ao menos um item.");
        var produtos = await repositorio.ObterProdutosAsync(empresaId, doc.Itens.Where(i => i.ProdutoId is not null).Select(i => i.ProdutoId!.Value).Distinct(), ct);
        var centros = doc.Itens.Select(i => i.CentroCustoId).Append(doc.CentroCustoId).Where(c => c is not null).Select(c => c!.Value).Distinct().ToList();
        if (centros.Count > 0)
        {
            if (!config.CentroCustoAtivo) erros.Add("Centros de custo não estão habilitados no Fiscal desta empresa (Cadastros › Parâmetros).");
            else
            {
                var validos = await repositorio.CentrosValidosAsync(empresaId, centros, ct);
                if (centros.Any(c => !validos.Contains(c))) erros.Add("Centro de custo inexistente ou inativo.");
            }
        }

        var itens = new List<ItemFiscal>();
        var n = 0;
        foreach (var bruto in doc.Itens)
        {
            n++;
            var rot = $"Item {n}";
            Produto? produto = null;
            if (bruto.ProdutoId is { } pid && !produtos.TryGetValue(pid, out produto)) { erros.Add($"{rot}: produto não encontrado."); continue; }
            var cfop = (bruto.Cfop ?? "").Trim().ToUpperInvariant();
            if (doc.Modelo == ModeloDocumento.NFSe) cfop = Cfop.Servico;
            else if (Cfop.Operacao(cfop) is not { } sentido) erros.Add($"{rot}: CFOP inválido (4 dígitos).");
            else if (sentido != doc.Operacao)
                erros.Add($"{rot}: o CFOP {cfop} não é de {doc.Operacao.Rotulo().ToLowerInvariant()} (entradas começam por 1, 2 ou 3; saídas por 5, 6 ou 7).");
            var descricao = string.IsNullOrWhiteSpace(bruto.Descricao) ? produto?.Descricao : bruto.Descricao.Trim();
            if (string.IsNullOrWhiteSpace(descricao)) erros.Add($"{rot}: informe o produto ou a descrição.");
            if (bruto.Quantidade <= 0) erros.Add($"{rot}: a quantidade deve ser maior que zero.");
            if (bruto.ValorUnitario < 0) erros.Add($"{rot}: o valor unitário não pode ser negativo.");
            if (new[] { bruto.Desconto, bruto.Frete, bruto.Seguro, bruto.Outras, bruto.BcIcms, bruto.AliqIcms, bruto.Icms, bruto.BcIcmsSt, bruto.IcmsSt, bruto.BcIpi, bruto.AliqIpi,
                        bruto.Ipi, bruto.BcPis, bruto.AliqPis, bruto.Pis, bruto.BcCofins, bruto.AliqCofins, bruto.Cofins, bruto.BcIss, bruto.AliqIss, bruto.Iss }.Any(v => v < 0))
                erros.Add($"{rot}: valores, bases e alíquotas não podem ser negativos.");
            if (descricao is { Length: > 500 }) erros.Add($"{rot}: a descrição pode ter até 500 caracteres.");
            var item = CalculadoraNota.Completar(bruto with
            {
                Numero = n, Cfop = cfop, Descricao = descricao,
                CodProduto = produto is null ? bruto.CodProduto : produto.Codigo.ToString(),
                Ncm = Digitos(bruto.Ncm) ?? produto?.Ncm, Cest = Digitos(bruto.Cest) ?? produto?.Cest, Gtin = produto?.Gtin ?? ValidadorGtin.Normalizar(bruto.Gtin),
                Unidade = string.IsNullOrWhiteSpace(bruto.Unidade) ? produto?.Unidade : bruto.Unidade.Trim().ToUpperInvariant(),
                Origem = string.IsNullOrWhiteSpace(bruto.Origem) ? produto?.Origem : bruto.Origem,
                CentroCustoId = bruto.CentroCustoId,
            });
            if (item.Ncm is { } ncm && ncm.Length != 8) erros.Add($"{rot}: NCM deve ter 8 dígitos.");
            if (item.ValorContabil < 0) erros.Add($"{rot}: o valor contábil do item ficou negativo.");
            itens.Add(item);
        }
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var calculado = CalculadoraNota.Totalizar(itens, doc.Totais);
        var conferencia = CalculadoraNota.Conferir(doc.Totais, calculado);
        if (conferencia.Count > 0) return Resultado<long>.Falha(conferencia);

        if (doc.Parcelas is { Count: > 0 } parcelas)
        {
            var errosParcelas = new List<string>();
            foreach (var p in parcelas)
            {
                if (p.Valor <= 0) errosParcelas.Add($"Parcela {p.Numero}: o valor deve ser maior que zero.");
                if (p.Vencimento == default) errosParcelas.Add($"Parcela {p.Numero}: informe o vencimento.");
                else if (p.Vencimento < doc.Emissao) errosParcelas.Add($"Parcela {p.Numero}: o vencimento não pode ser anterior à emissão.");
            }
            if (parcelas.Select(p => p.Numero.Trim()).Distinct().Count() != parcelas.Count) errosParcelas.Add("Há parcelas com o mesmo número.");
            var soma = parcelas.Sum(p => p.Valor);
            if (Math.Abs(soma - calculado.Total) > 0.01m)
                errosParcelas.Add($"A soma das parcelas ({Formato.Moeda(soma)}) difere do valor da nota ({Formato.Moeda(calculado.Total)}).");
            if (errosParcelas.Count > 0) return Resultado<long>.Falha(errosParcelas);
        }

        var final = doc with
        {
            Numero = numero, Serie = serie, Chave = chave, Natureza = string.IsNullOrWhiteSpace(doc.Natureza) ? null : doc.Natureza.Trim(),
            Totais = calculado, Itens = itens,
        };
        try { return Resultado<long>.Ok(await repositorio.GravarAsync(empresaId, final, empresa, participante!, usuarioId, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    /// <summary>
    /// Altera a escrituração de uma nota (digitada ou importada de XML) sem tocar nos dados do XML. Nota contabilizada não pode ser alterada.
    /// </summary>
    public async Task<Resultado> GravarEscrituracaoAsync(long empresaId, EscrituracaoDocumento esc, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return Resultado.Falha(negado);
        var doc = await repositorio.ObterAsync(empresaId, esc.DocumentoId, ct);
        if (doc is null) return Resultado.Falha("Documento não encontrado.");
        if (doc.Contabilizado) return Resultado.Falha("A nota está contabilizada: desfaça a contabilização para alterar a escrituração.");
        var config = await repositorio.ObterConfiguracaoAsync(empresaId, ct);
        var erros = new List<string>();
        var obs = string.IsNullOrWhiteSpace(esc.Observacoes) ? null : esc.Observacoes.Trim();
        if (obs is { Length: > 2000 }) erros.Add("As observações podem ter até 2000 caracteres.");
        async Task<string?> Conta(string? codigo, string rotulo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return null;
            codigo = codigo.Trim();
            if (!await repositorio.ContaAnaliticaExisteAsync(empresaId, codigo, ct)) erros.Add($"{rotulo}: conta \"{codigo}\" não encontrada entre as analíticas ativas.");
            return codigo;
        }
        var contaD = await Conta(esc.ContaDebito, "Conta de débito");
        var contaC = await Conta(esc.ContaCredito, "Conta de crédito");
        var centros = esc.Itens.Select(i => i.CentroCustoId).Append(esc.CentroCustoId).Where(c => c is not null).Select(c => c!.Value).Distinct().ToList();
        if (centros.Count > 0)
        {
            if (!config.CentroCustoAtivo) erros.Add("Centros de custo não estão habilitados no Fiscal desta empresa (Cadastros › Parâmetros).");
            else if ((await repositorio.CentrosValidosAsync(empresaId, centros, ct)) is var validos && centros.Any(c => !validos.Contains(c)))
                erros.Add("Centro de custo inexistente ou inativo.");
        }
        var produtos = await repositorio.ObterProdutosAsync(empresaId, esc.Itens.Where(i => i.ProdutoId is not null).Select(i => i.ProdutoId!.Value).Distinct(), ct);
        var itens = new List<EscrituracaoItem>();
        foreach (var i in esc.Itens)
        {
            var original = doc.Itens.FirstOrDefault(x => x.Numero == i.Numero);
            if (original is null) { erros.Add($"Item {i.Numero} não existe na nota."); continue; }
            if (i.ProdutoId is { } pid && !produtos.ContainsKey(pid)) erros.Add($"Item {i.Numero}: produto não encontrado.");
            var cfop = string.IsNullOrWhiteSpace(i.Cfop) ? original.CfopOriginal ?? original.Cfop : i.Cfop.Trim().ToUpperInvariant();
            if (doc.Modelo != ModeloDocumento.NFSe)
            {
                if (Cfop.Operacao(cfop) is not { } sentido) erros.Add($"Item {i.Numero}: CFOP inválido (4 dígitos).");
                else if (sentido != doc.Operacao && cfop != (original.CfopOriginal ?? original.Cfop))
                    erros.Add($"Item {i.Numero}: o CFOP {cfop} não é de {doc.Operacao.Rotulo().ToLowerInvariant()} (entradas começam por 1, 2 ou 3; saídas por 5, 6 ou 7).");
            }
            itens.Add(i with { Cfop = cfop });
        }
        if (erros.Count > 0) return Resultado.Falha(erros);
        try
        {
            await repositorio.GravarEscrituracaoAsync(empresaId, new EscrituracaoDocumento(esc.DocumentoId, obs, esc.CentroCustoId, contaD, contaC, itens), usuarioId, ct);
            return Resultado.Ok();
        }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    private static string? Digitos(string? t) => string.IsNullOrWhiteSpace(t) ? null : new string(t.Where(char.IsAsciiDigit).ToArray()) is { Length: > 0 } d ? d : null;
}
