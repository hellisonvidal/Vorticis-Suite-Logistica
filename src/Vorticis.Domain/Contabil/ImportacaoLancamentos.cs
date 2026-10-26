using Vorticis.Domain.Comum;

namespace Vorticis.Domain.Contabil;

/// <summary>Como a planilha descreve os lançamentos.</summary>
public enum ModeloLayout
{
    /// <summary>Cada linha é um lançamento: data, conta débito, conta crédito, valor.</summary>
    UmaLinhaPorLancamento,
    /// <summary>Cada linha é uma partida (conta, D/C, valor); as linhas são agrupadas em lançamentos.</summary>
    UmaLinhaPorPartida,
}

/// <summary>
/// Layout de importação definido pelo usuário: o nome da coluna da planilha que corresponde a cada campo.
/// Campos não mapeados podem ter um valor fixo (conta débito/crédito padrão, histórico padrão).
/// </summary>
public sealed record LayoutImportacao
{
    public string Nome { get; init; } = "";
    public ModeloLayout Modelo { get; init; } = ModeloLayout.UmaLinhaPorLancamento;
    public string? ColunaData { get; init; }
    public string? ColunaValor { get; init; }
    public string? ColunaHistorico { get; init; }
    public string? ColunaDocumento { get; init; }
    public string? ColunaCentroCusto { get; init; }

    // uma linha por lançamento
    public string? ColunaContaDebito { get; init; }
    public string? ColunaContaCredito { get; init; }
    public string? ContaDebitoPadrao { get; init; }
    public string? ContaCreditoPadrao { get; init; }

    // uma linha por partida
    public string? ColunaAgrupador { get; init; }
    public string? ColunaConta { get; init; }
    public string? ColunaDC { get; init; }
    public string? ColunaValorDebito { get; init; }
    public string? ColunaValorCredito { get; init; }

    public string? HistoricoPadrao { get; init; }
    /// <summary>Máscara para converter os códigos de conta da planilha (ex.: 11010001 → 1.1.01.0001); vazio = usar como estão.</summary>
    public string? MascaraContas { get; init; }

    /// <summary>Layout padrão do Vorticis (aba Lançamentos contábeis): uma linha por partida.</summary>
    public static LayoutImportacao Padrao { get; } = new()
    {
        Nome = "Padrão Vorticis", Modelo = ModeloLayout.UmaLinhaPorPartida, ColunaAgrupador = "LANCAMENTO", ColunaData = "DATA",
        ColunaConta = "CONTA", ColunaDC = "DC", ColunaValor = "VALOR", ColunaHistorico = "HISTORICO", ColunaCentroCusto = "CENTRO_CUSTO",
        ColunaDocumento = "DOCUMENTO",
    };

    public static IReadOnlyList<string> ColunasPadrao { get; } = ["LANCAMENTO", "DATA", "CONTA", "DC", "VALOR", "HISTORICO", "CENTRO_CUSTO", "DOCUMENTO"];
}

/// <summary>Lançamento lido da planilha (ainda por códigos; validado depois contra o plano e os períodos).</summary>
public sealed record LancamentoLido(int Linha, DateOnly Data, IReadOnlyList<PartidaProposta> Partidas, string? Chave = null)
{
    public decimal Total => Partidas.Where(p => p.DC == IndicadorDC.D).Sum(p => p.Valor);
}

public sealed record LeituraLancamentos(IReadOnlyList<LancamentoLido> Lancamentos, IReadOnlyList<string> Erros);

/// <summary>Transforma as linhas da planilha em lançamentos conforme o layout.</summary>
public static class ImportadorLancamentos
{
    public static LeituraLancamentos Ler(TabelaImportada t, LayoutImportacao layout)
    {
        var erros = new List<string>();
        int Col(string? nome, string campo, bool obrigatoria)
        {
            if (string.IsNullOrWhiteSpace(nome))
            {
                if (obrigatoria) erros.Add($"Informe a coluna de {campo} no layout.");
                return -1;
            }
            var alvo = TabelaImportada.Normalizar(nome);
            for (var i = 0; i < t.Colunas.Count; i++)
                if (TabelaImportada.Normalizar(t.Colunas[i]) == alvo) return i;
            erros.Add($"A planilha não tem a coluna \"{nome}\" ({campo}).");
            return -1;
        }

        MascaraPlano? mascara = null;
        if (!string.IsNullOrWhiteSpace(layout.MascaraContas))
        {
            var m = MascaraPlano.Ler(layout.MascaraContas);
            if (!m.Sucesso) return new([], m.Erros);
            mascara = m.Valor;
        }
        string? Conta(string bruto, int linha)
        {
            if (bruto.Length == 0) return null;
            var r = mascara is null ? MascaraPlano.SoNormalizar(bruto) : mascara.Converter(bruto);
            if (r.Sucesso) return r.Valor;
            erros.Add($"Linha {linha}: conta {bruto}: {r.Erros[0]}.");
            return null;
        }

        var iData = Col(layout.ColunaData, "data", true);
        var iHist = Col(layout.ColunaHistorico, "histórico", false);
        var iDoc = Col(layout.ColunaDocumento, "documento", false);
        var iCc = Col(layout.ColunaCentroCusto, "centro de custo", false);
        var res = new List<LancamentoLido>();

        if (layout.Modelo == ModeloLayout.UmaLinhaPorLancamento)
        {
            var iDeb = Col(layout.ColunaContaDebito, "conta débito", string.IsNullOrWhiteSpace(layout.ContaDebitoPadrao));
            var iCred = Col(layout.ColunaContaCredito, "conta crédito", string.IsNullOrWhiteSpace(layout.ContaCreditoPadrao));
            var iValor = Col(layout.ColunaValor, "valor", true);
            if (erros.Count > 0) return new([], erros);
            for (var i = 0; i < t.Linhas.Count && erros.Count < 50; i++)
            {
                var linha = t.NumerosLinha[i];
                var valorTexto = t.Valor(i, iValor);
                if (valorTexto.Length == 0 && t.Valor(i, iData).Length == 0) continue;     // linha de total/vazia
                var data = Formato.LerData(t.Valor(i, iData));
                var valor = Formato.LerValor(valorTexto);
                if (data is null) { erros.Add($"Linha {linha}: data inválida \"{t.Valor(i, iData)}\"."); continue; }
                if (valor is null) { erros.Add($"Linha {linha}: valor inválido \"{valorTexto}\"."); continue; }
                if (valor == 0) continue;
                var debBruto = iDeb >= 0 && t.Valor(i, iDeb).Length > 0 ? t.Valor(i, iDeb) : layout.ContaDebitoPadrao?.Trim() ?? "";
                var credBruto = iCred >= 0 && t.Valor(i, iCred).Length > 0 ? t.Valor(i, iCred) : layout.ContaCreditoPadrao?.Trim() ?? "";
                if (debBruto.Length == 0 || credBruto.Length == 0)
                {
                    erros.Add($"Linha {linha}: informe a conta {(debBruto.Length == 0 ? "débito" : "crédito")}.");
                    continue;
                }
                var deb = Conta(debBruto, linha);
                var cred = Conta(credBruto, linha);
                if (deb is null || cred is null) continue;
                if (valor < 0) (deb, cred) = (cred, deb);                   // valor negativo inverte débito e crédito
                var hist = Historico(t, i, iHist, iDoc, layout.HistoricoPadrao);
                var doc = iDoc >= 0 ? Nulo(t.Valor(i, iDoc)) : null;
                var cc = iCc >= 0 ? Nulo(t.Valor(i, iCc)) : null;
                var v = Math.Abs(valor.Value);
                res.Add(new LancamentoLido(linha, data.Value,
                    [new PartidaProposta(deb, IndicadorDC.D, v, hist, cc, null, doc), new PartidaProposta(cred, IndicadorDC.C, v, hist, cc, null, doc)]));
            }
            return new(res, erros);
        }

        // Uma linha por partida
        var iConta = Col(layout.ColunaConta, "conta", true);
        var iGrupo = Col(layout.ColunaAgrupador, "nº do lançamento", false);
        var iDc = Col(layout.ColunaDC, "D/C", false);
        var iVal = Col(layout.ColunaValor, "valor", string.IsNullOrWhiteSpace(layout.ColunaValorDebito) && string.IsNullOrWhiteSpace(layout.ColunaValorCredito));
        var iVDeb = Col(layout.ColunaValorDebito, "valor a débito", false);
        var iVCred = Col(layout.ColunaValorCredito, "valor a crédito", false);
        if (erros.Count > 0) return new([], erros);

        var atual = new List<PartidaProposta>();
        (int Linha, DateOnly Data, string Grupo)? inicio = null;
        void Fechar()
        {
            if (inicio is { } c && atual.Count > 0) res.Add(new LancamentoLido(c.Linha, c.Data, atual.ToList()));
            atual.Clear();
            inicio = null;
        }
        for (var i = 0; i < t.Linhas.Count && erros.Count < 50; i++)
        {
            var linha = t.NumerosLinha[i];
            var contaBruta = t.Valor(i, iConta);
            if (contaBruta.Length == 0 && t.Valor(i, iData).Length == 0) continue;
            var data = Formato.LerData(t.Valor(i, iData));
            if (data is null) { erros.Add($"Linha {linha}: data inválida \"{t.Valor(i, iData)}\"."); continue; }
            var conta = Conta(contaBruta, linha);
            if (conta is null) { if (contaBruta.Length == 0) erros.Add($"Linha {linha}: conta vazia."); continue; }

            IndicadorDC? dc;
            decimal? valor;
            if (iVDeb >= 0 || iVCred >= 0)
            {
                var vd = Formato.LerValor(t.Valor(i, iVDeb)) ?? 0;
                var vc = Formato.LerValor(t.Valor(i, iVCred)) ?? 0;
                (dc, valor) = vd != 0 ? (IndicadorDC.D, vd) : (IndicadorDC.C, vc);
            }
            else
            {
                valor = Formato.LerValor(t.Valor(i, iVal));
                var dcTexto = TabelaImportada.Normalizar(t.Valor(i, iDc));
                // Sem coluna D/C, o sinal do valor decide: positivo débito, negativo crédito.
                dc = iDc < 0 ? IndicadorDC.D : dcTexto.StartsWith('C') ? IndicadorDC.C : dcTexto.StartsWith('D') ? IndicadorDC.D : null;
                if (dc is null) { erros.Add($"Linha {linha}: indicador D/C inválido \"{t.Valor(i, iDc)}\"."); continue; }
            }
            if (valor is null) { erros.Add($"Linha {linha}: valor inválido."); continue; }
            if (valor == 0) continue;
            if (valor < 0) (valor, dc) = (-valor, dc.Value.Inverso());       // negativo inverte o lado

            var grupo = iGrupo >= 0 ? t.Valor(i, iGrupo) : "";
            // Novo lançamento: mudou o nº (quando há coluna) ou, sem coluna, o anterior já fechou débitos = créditos.
            var novo = inicio is null
                       || (iGrupo >= 0 ? grupo != inicio.Value.Grupo
                                       : atual.Count >= 2 && Soma(atual, IndicadorDC.D) == Soma(atual, IndicadorDC.C));
            if (novo)
            {
                Fechar();
                inicio = (linha, data.Value, grupo);
            }
            else if (data.Value != inicio!.Value.Data)
            {
                erros.Add($"Linha {linha}: data diferente das demais partidas do lançamento iniciado na linha {inicio.Value.Linha}.");
                continue;
            }
            atual.Add(new PartidaProposta(conta, dc.Value, valor.Value, Historico(t, i, iHist, iDoc, layout.HistoricoPadrao),
                                          iCc >= 0 ? Nulo(t.Valor(i, iCc)) : null, null, iDoc >= 0 ? Nulo(t.Valor(i, iDoc)) : null));
        }
        Fechar();
        return new(res, erros);
    }

    private static decimal Soma(IEnumerable<PartidaProposta> p, IndicadorDC dc) => p.Where(x => x.DC == dc).Sum(x => x.Valor);

    private static string? Nulo(string s) => s.Length == 0 ? null : s;

    private static string Historico(TabelaImportada t, int i, int iHist, int iDoc, string? padrao)
    {
        var h = iHist >= 0 ? t.Valor(i, iHist) : "";
        if (h.Length == 0) h = padrao?.Trim() ?? "";
        if (h.Length == 0 && iDoc >= 0 && t.Valor(i, iDoc).Length > 0) h = $"Documento {t.Valor(i, iDoc)}";
        return h.Length == 0 ? "Importado de planilha" : h;
    }
}
