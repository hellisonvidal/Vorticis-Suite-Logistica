using Vorticis.Domain.Sped;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Sped;

/// <summary>
/// Validação prévia da EFD ICMS/IPI e da EFD-Contribuições: o que o PVA rejeitaria (pendências bloqueantes) e o que merece conferência (avisos).
/// Pura: recebe os dados já carregados.
/// </summary>
public static class ValidadorEfd
{
    private static void Add(List<PendenciaEcd> l, bool bloqueante, string codigo, string mensagem) =>
        l.Add(new PendenciaEcd(bloqueante ? GravidadePendencia.Bloqueante : GravidadePendencia.Aviso, codigo, mensagem));

    /// <summary>Junta exemplos numa mensagem só: "a, b, c (e mais 4)".</summary>
    private static string Exemplos(IReadOnlyList<string> itens, int max = 5) =>
        itens.Count <= max ? string.Join(", ", itens) : $"{string.Join(", ", itens.Take(max))} (e mais {itens.Count - max})";

    private static string Nome(DocumentoEfd d) => $"{(d.Nfse ? "NFS-e" : d.Modelo == "65" ? "NFC-e" : "NF-e")} {d.Numero}/{d.Serie} ({(d.Entrada ? "entrada" : "saída")} de {d.EntradaSaida:dd/MM/yyyy})";

    // ------------------------------------------------------------------------------------------------ EFD ICMS/IPI

    public static IReadOnlyList<PendenciaEcd> ValidarIcms(DadosEfdIcms d, ApuracaoEfdIcms ap)
    {
        var l = new List<PendenciaEcd>();
        var e = d.Empresa;
        var o = d.Opcoes;
        ValidarEmpresa(l, e, d.Contador);
        if (CamposEfd.Digitos(e.InscricaoEstadual).Length == 0)
            Add(l, true, "SEM_IE", "A empresa não tem inscrição estadual (IE) cadastrada. A EFD ICMS/IPI é entregue por contribuinte do ICMS inscrito: preencha a IE em Cadastros › Empresas.");
        if (CamposEfd.Digitos(e.Cep).Length != 8 || string.IsNullOrWhiteSpace(e.Logradouro))
            Add(l, true, "SEM_ENDERECO", "Endereço da empresa incompleto (CEP e logradouro são obrigatórios no registro 0005). Complete em Cadastros › Empresas.");
        if (o.Perfil is not ('A' or 'B' or 'C')) Add(l, true, "PERFIL_INVALIDO", "Perfil da EFD inválido: use A, B ou C.");
        if (o.IndicadorAtividade is not ('0' or '1')) Add(l, true, "ATIVIDADE_INVALIDA", "Indicador de atividade inválido: 0 (industrial ou equiparado) ou 1 (outros).");
        if (e.Regime is "SIMPLES_NACIONAL" or "MEI")
            Add(l, false, "SIMPLES", "Empresa do Simples Nacional: o ICMS é recolhido no DAS e, em geral, a EFD ICMS/IPI só é exigida se a UF determinar. Confirme a obrigatoriedade.");

        var docs = CalculoEfdIcms.Escrituraveis(d.Documentos).ToList();
        var nfse = d.Documentos.Count(x => x.Nfse);
        if (nfse > 0) Add(l, false, "NFSE_FORA", $"{nfse} NFS-e do período não entram na EFD ICMS/IPI (serviços são do ISS; constam da EFD-Contribuições).");
        if (docs.Count == 0) Add(l, false, "SEM_MOVIMENTO", "Nenhum documento fiscal de mercadoria no período: o bloco C sai sem dados e a apuração do ICMS, zerada.");

        ValidarDocumentos(l, docs, e);

        if (ap.TotalObrigacoes > 0 && string.IsNullOrWhiteSpace(o.CodigoReceitaIcms))
            Add(l, true, "SEM_COD_REC_ICMS", $"Há ICMS a recolher ({ap.TotalObrigacoes:N2}): informe o código de receita da UF (E116.COD_REC).");
        if (ap.St.Any(s => s.Recolher > 0) && string.IsNullOrWhiteSpace(o.CodigoReceitaIcmsSt))
            Add(l, true, "SEM_COD_REC_ST", "Há ICMS-ST retido a recolher: informe o código de receita do ICMS-ST (E250.COD_REC).");
        if (ap.St.Count > 0)
            Add(l, false, "ST_AVISO", $"ICMS-ST retido nas saídas apurado por UF de destino ({string.Join(", ", ap.St.Select(s => s.Uf))}): confira cada E210/E250 com a GIA-ST/GNRE.");
        if (o.IndicadorAtividade != '0' && docs.Where(x => x.Autorizada).SelectMany(x => x.Itens).Any(i => i.Ipi > 0))
            Add(l, false, "IPI_SEM_INDUSTRIAL", "Há IPI nas notas, mas a atividade informada não é industrial/equiparado: o bloco E500 (apuração do IPI) não foi gerado.");
        if (docs.Where(x => x.Autorizada).Any(x => x.Itens.Any(i => string.IsNullOrWhiteSpace(CamposEfd.Digitos(i.Ncm)))))
            Add(l, false, "ITEM_SEM_NCM", "Há itens sem NCM: o cadastro de produtos (0200) sairá sem NCM e o PVA pode exigir.");
        if (ap.SaldoCredorAnterior < 0) Add(l, true, "SALDO_NEGATIVO", "O saldo credor do período anterior não pode ser negativo.");

        // outros lançamentos (Lançamentos › SPED Fiscal)
        var deducoesInformadas = (d.Ajustes ?? []).Where(a => a.Tipo == TipoAjusteIcms.Deducoes).Sum(a => a.Valor);
        if (deducoesInformadas > ap.Deducoes)
            Add(l, false, "DEDUCAO_MAIOR", $"As deduções informadas ({deducoesInformadas:N2}) passam do saldo devedor apurado: só {ap.Deducoes:N2} foram usados no E110.");
        if ((d.Ajustes ?? []).Any(a => a.Codigo[..2] != e.Uf))
            Add(l, true, "AJUSTE_UF", "Há ajuste da apuração com código de outra UF: os códigos do E111 começam pela UF da empresa.");
        if (d.Inventario is { } inv)
        {
            if (inv.Itens.Count == 0) Add(l, true, "INVENTARIO_VAZIO", $"O inventário de {inv.Data:dd/MM/yyyy} não tem itens.");
            if (string.IsNullOrWhiteSpace(inv.ContaContabil)) Add(l, true, "INVENTARIO_SEM_CONTA", "Informe a conta contábil do estoque no inventário (H010.COD_CTA).");
            if (o.InventarioZerado) Add(l, false, "INVENTARIO_CADASTRADO", "Há inventário cadastrado para esta EFD: ele é usado no bloco H no lugar do inventário zerado.");
        }
        return l;
    }

    // ------------------------------------------------------------------------------------------------ EFD-Contribuições

    public static IReadOnlyList<PendenciaEcd> ValidarContribuicoes(DadosEfdContribuicoes d)
    {
        var l = new List<PendenciaEcd>();
        var e = d.Empresa;
        if (e.Regime is "SIMPLES_NACIONAL" or "MEI")
        {
            Add(l, true, "SIMPLES_DISPENSADO", "Empresas do Simples Nacional e MEI não entregam a EFD-Contribuições (PIS e COFINS estão no DAS).");
            return l;
        }
        ValidarEmpresa(l, e, d.Contador);
        if (d.Fim.Day != DateTime.DaysInMonth(d.Fim.Year, d.Fim.Month) || d.Inicio.Day != 1)
            Add(l, true, "PERIODO", "A EFD-Contribuições é mensal: informe o mês inteiro.");
        if (d.Apuracao is not { } ap)
        {
            Add(l, true, "SEM_APURACAO", $"Não há apuração de PIS e COFINS salva para {d.Inicio:MM/yyyy}. Apure e salve em Apuração › PIS e COFINS antes de gerar o bloco M.");
        }
        else
        {
            var docsOk = d.Documentos.Where(x => x.Autorizada).ToList();
            decimal Base(ItemEfd i) => CalculoEfdContribuicoes.Base(i);
            bool Receita(DocumentoEfd doc, ItemEfd i) => Cfop.GeraReceita(doc.Nfse ? Cfop.Servico : i.CfopNumerico);
            var receita = docsOk.Where(x => !x.Entrada).SelectMany(x => x.Itens.Where(i => Receita(x, i))).Sum(Base)
                          - docsOk.Where(x => x.Entrada && !x.Nfse).SelectMany(x => x.Itens).Where(i => Cfop.EhDevolucaoDeVenda(i.CfopNumerico)).Sum(Base);
            if (Math.Abs(receita - ap.ReceitaBase) > 0.05m)
                Add(l, false, "RECEITA_DIVERGE", $"A receita da apuração ({ap.ReceitaBase:N2}) difere da soma das notas de saída do mês ({receita:N2}). O bloco M segue a apuração; revise ou reapure.");
            if (!ap.Cumulativo)
            {
                var cred = docsOk.Where(x => x.Entrada && !x.Nfse).SelectMany(x => x.Itens).Where(i => Cfop.GeraCreditoPisCofins(i.CfopNumerico)).Sum(Base);
                if (Math.Abs(cred - ap.BaseCredito) > 0.05m)
                    Add(l, false, "CREDITO_DIVERGE", $"A base de crédito da apuração ({ap.BaseCredito:N2}) difere da soma das entradas que geram crédito nas notas ({cred:N2}); créditos lançados à mão não têm nota correspondente nos blocos A e C.");
            }
            if (ap.PisDebito - (ap.Cumulativo ? 0 : ap.PisCredito) - ap.PisRetido < 0 || ap.CofinsDebito - (ap.Cumulativo ? 0 : ap.CofinsCredito) - ap.CofinsRetido < 0)
                Add(l, false, "SALDO_CREDOR", "A apuração tem saldo credor de PIS/COFINS: o controle de créditos (registros M110/1100) não é gerado nesta versão; transporte o saldo manualmente no PVA.");
            if (docsOk.Any(x => x.Entrada && !x.Nfse && x.Itens.Any(i => Cfop.EhDevolucaoDeVenda(i.CfopNumerico))))
                Add(l, false, "DEVOLUCAO", "Há devoluções de venda no mês: a apuração reduz a receita, mas o detalhamento (M220/C170 de devolução) não é gerado; confira no PVA.");
        }

        var mercadorias = d.Documentos.Where(x => !x.Nfse).ToList();
        ValidarDocumentos(l, d.Documentos, e, exigeChaveNfse: false);
        if (d.Documentos.Count == 0) Add(l, false, "SEM_MOVIMENTO", "Nenhum documento fiscal no mês: os blocos A e C saem sem dados; o bloco M segue a apuração salva.");
        _ = mercadorias;
        return l;
    }

    // ------------------------------------------------------------------------------------------------ comum

    private static void ValidarEmpresa(List<PendenciaEcd> l, EmpresaEfd e, ContadorEfd? c)
    {
        if (CamposEfd.Digitos(e.Cnpj).Length != 14) Add(l, true, "CNPJ", "CNPJ da empresa inválido.");
        if (e.Uf is not { Length: 2 }) Add(l, true, "UF", "UF da empresa não informada.");
        if (CamposEfd.Digitos(e.CodMunicipio).Length != 7) Add(l, true, "MUNICIPIO", "Código IBGE do município da empresa inválido (7 dígitos).");
        if (c is null) Add(l, true, "SEM_CONTADOR", "A empresa não tem contador responsável vigente (registro 0100). Cadastre em Cadastros › Contadores e vincule à empresa.");
        else
        {
            if (CamposEfd.Digitos(c.Cpf).Length != 11) Add(l, true, "CONTADOR_CPF", $"CPF do contador {c.Nome} inválido.");
            if (string.IsNullOrWhiteSpace(c.Crc)) Add(l, true, "CONTADOR_CRC", $"CRC do contador {c.Nome} não informado.");
        }
    }

    private static void ValidarDocumentos(List<PendenciaEcd> l, IReadOnlyList<DocumentoEfd> docs, EmpresaEfd e, bool exigeChaveNfse = false)
    {
        var chaves = new List<string>(); var cfops = new List<string>(); var semItens = new List<string>(); var valores = new List<string>();
        var semPart = new List<string>(); var partSemDoc = new List<string>(); var partSemCad = new List<string>(); var partSemMun = new List<string>();
        var cabecalho = new List<string>();
        foreach (var doc in docs)
        {
            if (!doc.Nfse && !CamposEfd.ChaveValida(doc.Chave)) chaves.Add(Nome(doc));
            if (!doc.Autorizada) continue;
            if (doc.Itens.Count == 0) { semItens.Add(Nome(doc)); continue; }
            foreach (var i in doc.Itens)
            {
                if (doc.Nfse) break;
                var cfop = i.CfopNumerico;
                if (cfop.Length != 4 || (doc.Entrada ? !CamposEfd.CfopEntrada(cfop) : !CamposEfd.CfopSaida(cfop)))
                { cfops.Add($"{Nome(doc)} item {i.NumeroItem}: CFOP {i.Cfop}"); break; }
            }
            if (!doc.Nfse && Math.Abs(doc.SomaOperacao - doc.Total) > 0.01m)
                valores.Add($"{Nome(doc)}: valor contábil dos itens {doc.SomaOperacao:N2} × total {doc.Total:N2}");
            if (!doc.Nfse && Math.Abs(doc.Itens.Sum(i => i.Icms) - doc.Icms) > 0.01m) cabecalho.Add(Nome(doc));
            if (doc.Participante is not { } p)
            {
                if (doc.Entrada || doc.Modelo == "55" || doc.Nfse) semPart.Add(Nome(doc));
            }
            else
            {
                if (!p.DoCadastro) partSemCad.Add($"{p.Nome} ({p.Documento})");
                if (!p.Estrangeiro && p.Documento is not { Length: 11 or 14 }) partSemDoc.Add(p.Nome);
                if (!p.Estrangeiro && CamposEfd.Digitos(p.CodMunicipio).Length != 7) partSemMun.Add(p.Nome);
            }
        }
        if (chaves.Count > 0) Add(l, true, "CHAVE_INVALIDA", $"Chave de acesso ausente ou com dígito verificador inválido em {chaves.Count} nota(s): {Exemplos(chaves)}.");
        if (semItens.Count > 0) Add(l, true, "SEM_ITENS", $"Notas autorizadas sem itens: {Exemplos(semItens)}.");
        if (cfops.Count > 0) Add(l, true, "CFOP_OPERACAO", $"CFOP incompatível com a operação (entrada usa 1, 2 ou 3; saída, 5, 6 ou 7) em {cfops.Count} nota(s): {Exemplos(cfops)}.");
        if (valores.Count > 0) Add(l, true, "VALOR_DOCUMENTO", $"A soma dos valores dos itens não fecha com o total da nota (o PVA rejeita C190 ≠ C100): {Exemplos(valores, 3)}.");
        if (semPart.Count > 0) Add(l, true, "SEM_PARTICIPANTE", $"Notas sem participante (cadastro de clientes/fornecedores ou dados do emitente/destinatário na nota): {Exemplos(semPart)}.");
        if (partSemDoc.Count > 0) Add(l, true, "PARTICIPANTE_SEM_DOCUMENTO", $"Participantes sem CPF/CNPJ válido: {Exemplos(partSemDoc.Distinct().ToList())}.");
        if (partSemCad.Count > 0)
            Add(l, false, "PARTICIPANTE_SEM_CADASTRO", $"Participantes sem cadastro em Clientes/Fornecedores (0150 montado com os dados da nota): {Exemplos(partSemCad.Distinct().ToList())}.");
        if (partSemMun.Count > 0)
            Add(l, false, "PARTICIPANTE_SEM_MUNICIPIO", $"Participantes sem código de município IBGE (campo exigido no 0150): {Exemplos(partSemMun.Distinct().ToList())}.");
        if (cabecalho.Count > 0)
            Add(l, false, "TOTAIS_NOTA", $"O ICMS do cabeçalho difere da soma dos itens em: {Exemplos(cabecalho)}. O arquivo usa a soma dos itens.");
        _ = e; _ = exigeChaveNfse;
    }
}
