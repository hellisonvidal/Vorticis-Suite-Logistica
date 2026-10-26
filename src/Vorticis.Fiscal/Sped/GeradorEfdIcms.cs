using Vorticis.Domain.Sped;

namespace Vorticis.Fiscal.Sped;

/// <summary>
/// Gerador da EFD ICMS/IPI (SPED Fiscal), Guia Prático 3.2.x, leiaute 020 (2026): bloco 0 (0000, 0001, 0005, 0100, 0150, 0190, 0200),
/// C (C100, C170, C190 e C500/C590 das contas de energia, gás e água), D (D500/D590 de comunicação), G e K sem dados,
/// E (E110, E111 dos ajustes, E116, E210/E250 do ICMS-ST e E500/E510/E520 do IPI), H (inventário H005/H010, ou zerado opcional),
/// 1 (1010) e 9. Ver docs/08-efd.md.
/// </summary>
public static class GeradorEfdIcms
{
    public static string VersaoDoLeiaute(DateOnly fim, string? informada) =>
        !string.IsNullOrWhiteSpace(informada) ? informada.Trim() : fim.Year >= 2026 ? "020" : fim.Year == 2025 ? "019" : "018";

    public static ArquivoGeradoEfd Gerar(DadosEfdIcms d, ApuracaoEfdIcms? apuracao = null)
    {
        apuracao ??= CalculoEfdIcms.Apurar(d);
        var o = d.Opcoes;
        var e = d.Empresa;
        var docs = CalculoEfdIcms.Escrituraveis(d.Documentos).OrderBy(x => x.EntradaSaida).ThenBy(x => x.Entrada ? 0 : 1).ThenBy(x => x.Modelo).ThenBy(x => x.Serie)
            .ThenBy(x => long.TryParse(x.Numero, out var n) ? n : 0).ThenBy(x => x.Id).ToList();
        var ativos = docs.Where(x => x.Autorizada).ToList();
        var w = new EscritorEfd();

        // ------------------------------------------------------------------ bloco 0
        w.Abrir('0', true);
        w.Reg("0000", VersaoDoLeiaute(d.Fim, o.CodigoVersao), o.Retificadora ? "1" : "0", CamposEfd.D(d.Inicio), CamposEfd.D(d.Fim), CamposEfd.T(e.Nome, 100),
              e.Cnpj, "", e.Uf, CamposEfd.Digitos(e.InscricaoEstadual), e.CodMunicipio, CamposEfd.Digitos(e.InscricaoMunicipal), "", o.Perfil.ToString(), o.IndicadorAtividade.ToString());
        w.Reg("0005", CamposEfd.T(e.Fantasia ?? e.Nome, 60), CamposEfd.Digitos(e.Cep), CamposEfd.T(e.Logradouro, 60), CamposEfd.T(e.Numero ?? "S/N", 10),
              CamposEfd.T(e.Complemento, 60), CamposEfd.T(e.Bairro, 60), CamposEfd.Fone(e.Telefone), "", CamposEfd.T(e.Email, 255));
        if (d.Contador is { } c)
            w.Reg("0100", CamposEfd.T(c.Nome, 100), CamposEfd.Digitos(c.Cpf), CamposEfd.T(c.Crc, 15), CamposEfd.Digitos(c.CnpjEscritorio), "", "", "", "", "",
                  CamposEfd.Fone(c.Telefone), "", CamposEfd.T(c.Email, 255), e.CodMunicipio);
        var consumo = (d.Consumo ?? []).OrderBy(c => c.Entrada).ThenBy(c => c.Modelo).ThenBy(c => c.Numero).ToList();
        EscreverCadastros(w, ativos.Select(x => x.Participante).Concat(consumo.Select(c => c.Participante)),
                          ativos.SelectMany(x => x.Itens).Concat((d.Inventario?.Itens ?? []).Select(ItemDoInventario)));
        w.Fechar();

        // ------------------------------------------------------------------ bloco B (apenas DF) — ISS do Distrito Federal, sem dados
        if (e.Uf == "DF") { w.Abrir('B', false); w.Fechar(); }

        // ------------------------------------------------------------------ bloco C
        var energia = consumo.Where(c => !c.Comunicacao).ToList();
        var comunicacao = consumo.Where(c => c.Comunicacao).ToList();
        w.Abrir('C', docs.Count > 0 || energia.Count > 0);
        foreach (var doc in docs)
        {
            EscreverC100(w, d.Empresa.Cnpj, doc);
            if (!doc.Autorizada) continue;
            if (o.Perfil == 'A' || doc.Entrada)
                foreach (var i in doc.Itens) EscreverC170(w, i);
            foreach (var a in CalculoEfdIcms.Analitico(doc))
                w.Reg("C190", a.Cst, a.Cfop, CamposEfd.Aliq(a.Aliquota), CamposEfd.V(a.Operacao), CamposEfd.V(a.BaseIcms), CamposEfd.V(a.Icms), CamposEfd.V(a.BaseSt),
                      CamposEfd.V(a.IcmsSt), CamposEfd.V(a.Reducao), CamposEfd.V(a.Ipi), "");
        }
        foreach (var cc in energia)
        {
            // C500 (40 campos): entrada (0), emissão de terceiros (1), documento regular (00)
            w.Reg("C500", "0", "1", cc.Participante.Codigo, cc.Modelo, "00", CamposEfd.T(cc.Serie, 4), "", cc.ClasseConsumo, CamposEfd.Digitos(cc.Numero),
                  CamposEfd.D(cc.Emissao), CamposEfd.D(cc.Entrada), CamposEfd.V(cc.Valor), "0,00", CamposEfd.V(cc.Valor), "0,00", "0,00", "0,00",
                  CamposEfd.V(cc.BaseIcms), CamposEfd.V(cc.Icms), "0,00", "0,00", "", CamposEfd.V(cc.Pis), CamposEfd.V(cc.Cofins), "", "",
                  "", "", "", "", "", "", "", "", "", "", "", "", "");
            w.Reg("C590", cc.CstIcms, cc.Cfop, CamposEfd.Aliq(cc.AliqIcms), CamposEfd.V(cc.Valor), CamposEfd.V(cc.BaseIcms), CamposEfd.V(cc.Icms), "0,00", "0,00",
                  CamposEfd.V(Math.Max(0, cc.Valor - cc.BaseIcms)), "");
        }
        w.Fechar();

        // ------------------------------------------------------------------ bloco D: notas de comunicação e telecomunicação recebidas
        w.Abrir('D', comunicacao.Count > 0);
        foreach (var cc in comunicacao)
        {
            w.Reg("D500", "0", "1", cc.Participante.Codigo, cc.Modelo, "00", CamposEfd.T(cc.Serie, 4), "", CamposEfd.Digitos(cc.Numero), CamposEfd.D(cc.Emissao),
                  CamposEfd.D(cc.Entrada), CamposEfd.V(cc.Valor), "0,00", CamposEfd.V(cc.Valor), "0,00", "0,00", "0,00", CamposEfd.V(cc.BaseIcms), CamposEfd.V(cc.Icms),
                  "", CamposEfd.V(cc.Pis), CamposEfd.V(cc.Cofins), "", "");
            w.Reg("D590", cc.CstIcms, cc.Cfop, CamposEfd.Aliq(cc.AliqIcms), CamposEfd.V(cc.Valor), CamposEfd.V(cc.BaseIcms), CamposEfd.V(cc.Icms), "0,00", "0,00",
                  CamposEfd.V(Math.Max(0, cc.Valor - cc.BaseIcms)), "");
        }
        w.Fechar();

        // ------------------------------------------------------------------ bloco E
        w.Abrir('E', true);
        w.Reg("E100", CamposEfd.D(d.Inicio), CamposEfd.D(d.Fim));
        w.Reg("E110", CamposEfd.V(apuracao.Debitos), "0,00", CamposEfd.V(apuracao.AjustesDebitos), CamposEfd.V(apuracao.EstornosCreditos), CamposEfd.V(apuracao.Creditos),
              "0,00", CamposEfd.V(apuracao.AjustesCreditos), CamposEfd.V(apuracao.EstornosDebitos), CamposEfd.V(apuracao.SaldoCredorAnterior),
              CamposEfd.V(Math.Max(0, apuracao.SaldoApurado)), CamposEfd.V(apuracao.Deducoes), CamposEfd.V(apuracao.Recolher),
              CamposEfd.V(apuracao.SaldoCredorTransportar), CamposEfd.V(apuracao.DebitosEspeciais));
        foreach (var a in (d.Ajustes ?? []).OrderBy(a => a.Codigo, StringComparer.Ordinal).ThenBy(a => a.Id))
            w.Reg("E111", a.Codigo, CamposEfd.T(a.Descricao, 255), CamposEfd.V(a.Valor));
        if (apuracao.TotalObrigacoes > 0)
            w.Reg("E116", "000", CamposEfd.V(apuracao.TotalObrigacoes), CamposEfd.D(CamposEfd.Vencimento(d.Fim, o.DiaVencimento)), CamposEfd.T(o.CodigoReceitaIcms, 20), "", "", "", "",
                  $"{d.Fim.Month:00}{d.Fim.Year}");
        foreach (var st in apuracao.St)
        {
            w.Reg("E200", st.Uf, CamposEfd.D(d.Inicio), CamposEfd.D(d.Fim));
            w.Reg("E210", "1", "0,00", "0,00", "0,00", "0,00", "0,00", CamposEfd.V(st.Retencao), "0,00", "0,00", CamposEfd.V(st.Retencao), "0,00", CamposEfd.V(st.Recolher),
                  CamposEfd.V(st.SaldoCredorTransportar), "0,00");
            if (st.Recolher > 0)
                w.Reg("E250", "002", CamposEfd.V(st.Recolher), CamposEfd.D(CamposEfd.Vencimento(d.Fim, o.DiaVencimento)), CamposEfd.T(o.CodigoReceitaIcmsSt, 20), "", "", "", "",
                      $"{d.Fim.Month:00}{d.Fim.Year}");
        }
        if (apuracao.Ipi is { } ipi)
        {
            w.Reg("E500", "0", CamposEfd.D(d.Inicio), CamposEfd.D(d.Fim));
            foreach (var l in ipi.Linhas) w.Reg("E510", l.Cfop, l.Cst, CamposEfd.V(l.Contabil), CamposEfd.V(l.Base), CamposEfd.V(l.Ipi));
            w.Reg("E520", "0,00", CamposEfd.V(ipi.Debitos), CamposEfd.V(ipi.Creditos), "0,00", "0,00", CamposEfd.V(ipi.SaldoCredor), CamposEfd.V(ipi.SaldoDevedor));
        }
        w.Fechar();

        w.Abrir('G', false); w.Fechar();

        // ------------------------------------------------------------------ bloco H: inventário cadastrado (H005/H010) ou zerado opcional
        if (d.Inventario is { } inv)
        {
            w.Abrir('H', true);
            w.Reg("H005", CamposEfd.D(inv.Data), CamposEfd.V(inv.Total), inv.Motivo);
            foreach (var i in inv.Itens.OrderBy(i => i.ProdutoCodigo))
                w.Reg("H010", i.ProdutoCodigo.ToString(), CamposEfd.Unidade(i.Unidade), CamposEfd.Qtd(i.Quantidade), CamposEfd.ValorUnitario(i.ValorUnitario),
                      CamposEfd.V(i.Valor), "0", "", "", CamposEfd.T(inv.ContaContabil, 60), "");
        }
        else
        {
            w.Abrir('H', o.InventarioZerado);
            if (o.InventarioZerado) w.Reg("H005", CamposEfd.D(d.Fim), "0,00", "01");
        }
        w.Fechar();

        w.Abrir('K', false); w.Fechar();

        // ------------------------------------------------------------------ bloco 1
        w.Abrir('1', true);
        w.Reg("1010", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N", "N");
        w.Fechar();

        return w.Concluir();
    }

    /// <summary>Participantes (0150), unidades (0190) e itens (0200) usados pelos documentos escriturados.</summary>
    internal static void EscreverCadastros(EscritorEfd w, IEnumerable<ParticipanteEfd?> participantes, IEnumerable<ItemEfd> itens, bool comCest = true)
    {
        foreach (var p in participantes.Where(p => p is not null).GroupBy(p => p!.Codigo).Select(g => g.First()!).OrderBy(p => p.Codigo, StringComparer.Ordinal))
            w.Reg("0150", CamposEfd.T(p.Codigo, 60), CamposEfd.T(p.Nome, 100), p.CodPais, p.Cnpj, p.Cpf, p.Estrangeiro ? "" : CamposEfd.Digitos(p.InscricaoEstadual),
                  p.Estrangeiro ? "" : p.CodMunicipio, "", CamposEfd.T(p.Logradouro, 60), CamposEfd.T(p.Numero, 10), CamposEfd.T(p.Complemento, 60), CamposEfd.T(p.Bairro, 60));
        var lista = itens.ToList();
        foreach (var u in lista.Select(i => CamposEfd.Unidade(i.Unidade)).Distinct(StringComparer.Ordinal).OrderBy(u => u, StringComparer.Ordinal))
            w.Reg("0190", u, CamposEfd.T(CamposEfd.Descricao(u), 100));
        foreach (var g in lista.GroupBy(i => i.CodigoItem).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var i = g.First();
            var ncm = CamposEfd.Digitos(i.Ncm);
            var campos = new List<string?>
            {
                CamposEfd.T(g.Key, 60), CamposEfd.T(i.Descricao, 255), CamposEfd.Digitos(i.Gtin), "", CamposEfd.Unidade(i.Unidade), i.TipoItem,
                ncm.Length == 8 ? ncm : "", "", ncm.Length >= 2 ? ncm[..2] : "", "", i.AliquotaProduto is { } a ? CamposEfd.Aliq(a) : "",
            };
            if (comCest) campos.Add(CamposEfd.Digitos(i.Cest));   // CEST existe no 0200 da EFD ICMS/IPI, não na EFD-Contribuições
            w.Reg("0200", [.. campos]);
        }
    }

    /// <summary>Item do inventário como item do 0200 (código sequencial do produto).</summary>
    private static ItemEfd ItemDoInventario(ItemInventarioEfd i) =>
        new(0, null, i.ProdutoCodigo, null, i.Descricao, i.Ncm, null, null, i.TipoItem, null, "", i.Unidade, i.Quantidade, i.Valor, 0, 0, 0, 0, i.Valor,
            null, null, 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);

    internal static string IndEmit(string cnpjEmpresa, DocumentoEfd doc) =>
        doc.EmitenteDocumento is { Length: > 0 } em ? (em == cnpjEmpresa ? "0" : "1") : doc.Entrada ? "1" : "0";

    /// <summary>C100 (igual na EFD ICMS/IPI e na EFD-Contribuições). Na EFD-Contribuições o PIS e a COFINS do cabeçalho vêm da apuração (<paramref name="pis"/>/<paramref name="cofins"/>).</summary>
    internal static void EscreverC100(EscritorEfd w, string cnpjEmpresa, DocumentoEfd doc, decimal? pis = null, decimal? cofins = null)
    {
        var codSit = doc.Situacao switch { "CANCELADA" => "02", "DENEGADA" => "04", _ => "00" };
        var oper = doc.Entrada ? "0" : "1";
        var ser = CamposEfd.T(CamposEfd.Digitos(doc.Serie), 3);
        var num = CamposEfd.Digitos(doc.Numero);
        if (!doc.Autorizada)
        {
            w.Reg("C100", oper, IndEmit(cnpjEmpresa, doc), "", doc.Modelo, codSit, ser, num, doc.Chave, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
            return;
        }
        // totais do documento pela soma dos itens (iguais aos do C190); sem itens, os do cabeçalho
        var temItens = doc.Itens.Count > 0;
        decimal Soma(Func<ItemEfd, decimal> f, decimal cab) => temItens ? doc.Itens.Sum(f) : cab;
        w.Reg("C100", oper, IndEmit(cnpjEmpresa, doc), doc.Participante?.Codigo, doc.Modelo, codSit, ser, num, doc.Chave, CamposEfd.D(doc.Emissao), CamposEfd.D(doc.EntradaSaida),
              CamposEfd.V(doc.Total), "2", CamposEfd.V(doc.Desconto), "0,00", CamposEfd.V(doc.ValorProdutos), "9", CamposEfd.V(doc.Frete), CamposEfd.V(doc.Seguro),
              CamposEfd.V(doc.Outras), CamposEfd.V(Soma(i => i.BcIcms, doc.BcIcms)), CamposEfd.V(Soma(i => i.Icms, doc.Icms)),
              CamposEfd.V(Soma(i => i.BcIcmsSt, doc.BcIcmsSt)), CamposEfd.V(Soma(i => i.IcmsSt, doc.IcmsSt)), CamposEfd.V(Soma(i => i.Ipi, doc.Ipi)),
              CamposEfd.V(pis ?? Soma(i => i.Pis, doc.Pis)), CamposEfd.V(cofins ?? Soma(i => i.Cofins, doc.Cofins)), "0,00", "0,00");
    }

    private static void EscreverC170(EscritorEfd w, ItemEfd i)
    {
        var temIpi = !string.IsNullOrWhiteSpace(i.CstIpi);
        w.Reg("C170", i.NumeroItem.ToString(), CamposEfd.T(i.CodigoItem, 60), "", CamposEfd.Qtd(i.Quantidade), CamposEfd.Unidade(i.Unidade), CamposEfd.V(i.ValorProduto),
              CamposEfd.V(i.Desconto), "0", CamposEfd.CstIcms(i.CstIcms, i.Origem), i.CfopNumerico, "", CamposEfd.V(i.BcIcms), CamposEfd.Aliq(i.AliqIcms), CamposEfd.V(i.Icms),
              CamposEfd.V(i.BcIcmsSt), i.IcmsSt != 0 ? CamposEfd.Aliq(0) : "", CamposEfd.V(i.IcmsSt), temIpi ? "0" : "", temIpi ? CamposEfd.Cst2(i.CstIpi, "99") : "",
              temIpi ? "999" : "", temIpi ? CamposEfd.V(i.BcIpi) : "", temIpi ? CamposEfd.Aliq(i.AliqIpi) : "", temIpi ? CamposEfd.V(i.Ipi) : "",
              CamposEfd.Cst2(i.CstPis, "49"), CamposEfd.V(i.BcPis), CamposEfd.Aliq(i.AliqPis), "", "", CamposEfd.V(i.Pis),
              CamposEfd.Cst2(i.CstCofins, "49"), CamposEfd.V(i.BcCofins), CamposEfd.Aliq(i.AliqCofins), "", "", CamposEfd.V(i.Cofins), "", "");
    }
}
