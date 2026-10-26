using System.Security.Cryptography;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Contabil;
using static Vorticis.Domain.Sped.FormatoSped;

namespace Vorticis.Domain.Sped;

/// <summary>
/// Gera o arquivo da ECD — Livro G (Diário Geral, sem escrituração auxiliar), leiaute 9 (COD_VER_LC 9.00, a partir do
/// ano-calendário 2020; sem alteração de leiaute em 2023, 2024 e 2025) — conforme o Manual de Orientação do Leiaute 9 da ECD
/// (anexo ao ADE Cofis nº 1/2026) e o descritor do leiaute 9 do PGE/PVA do Sped Contábil (estrutura 9004).
/// <para>
/// Registros gerados: 0000, 0001, 0007, 0990; I001, I010, I030, I050, I051 (se há plano referencial), I052, I075 (opcional),
/// I100 (se há centros de custo), I150, I155, I200, I250, I350, I355, I990; J001, J005, J100, J150, J210, J215, J900, J930, J990;
/// 9001, 9900, 9990, 9999. Não gerados: 0020 (escrituração descentralizada), 0035 (SCP), 0150/0180 (participantes com
/// relacionamento societário — clientes e fornecedores comuns não entram), bloco C (preenchido pelo próprio PGE ao recuperar a
/// ECD anterior), I012/I015 (só livros R/A/Z), I020, I053, I157 (mudança de plano), I300/I310 (livro B), I500 a I555 (livro Z),
/// J800/J801 (anexos RTF), J932 (substituta), J935, bloco K (só quando 0000.IND_ESC_CONS = S: o descritor do PGE só aceita
/// K001.IND_DAD = 0, então o bloco não é emitido "sem dados").
/// </para>
/// </summary>
public static class GeradorEcd
{
    public const string VersaoLeiaute = "9.00";

    public static EscrituracaoEcd Gerar(DadosEcd d) => Gerar(d, new CalculoEcd(d));

    public static EscrituracaoEcd Gerar(DadosEcd d, CalculoEcd calc)
    {
        var p = d.Parametros;
        var e = d.Empresa;
        var linhas = new List<(string Reg, string Texto)>();
        void L(string reg, params string?[] campos) => linhas.Add((reg, Linha(reg, campos)));
        const string QtdLin = "\u0001QTD_LIN\u0001";   // substituído no fim, quando se sabe o total de linhas

        var cnpj = new string(e.Cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var nome = Texto(e.Nome);
        var nire = Nire.Normalizar(e.Nire);
        var ie = InscricaoEstadual.Normalizar(e.InscricaoEstadual);
        var ieAlfa = ie is null ? null : new string(ie.Where(char.IsAsciiLetterOrDigit).ToArray());
        var im = e.InscricaoMunicipal is null ? null : new string(e.InscricaoMunicipal.Where(char.IsAsciiLetterOrDigit).ToArray());

        // ---------------------------------------------------------------- Bloco 0
        L("0000", "LECD", Data(p.Inicio), Data(p.Fim), nome, cnpj, e.Uf.ToUpperInvariant(), ieAlfa, e.CodMunicipioIbge, im,
          p.IndicadorSituacaoEspecial?.ToString(), p.IndicadorSituacaoInicio.ToString(), nire is null ? "0" : "1",
          "0", "", e.GrandePorte ? "1" : "0", "0", "", "N", "N", "0", p.MudancaPlanoContas ? "1" : "0", p.CodPlanRef);
        L("0001", "0");
        if (ieAlfa is { Length: > 0 } && ieAlfa != "ISENTO") L("0007", e.Uf.ToUpperInvariant(), ieAlfa);
        else L("0007", "00", "");
        L("0990", (linhas.Count + 1).ToString());

        // ---------------------------------------------------------------- Bloco I
        var inicioI = linhas.Count;
        L("I001", "0");
        L("I010", "G", VersaoLeiaute);
        L("I030", "TERMO DE ABERTURA", p.NumeroOrdem.ToString(), Texto(p.NaturezaLivro, 80), QtdLin, nome, nire, cnpj,
          Data(p.DataArquivamentoAtos), Data(p.DataArquivamentoConversao), Texto(e.Municipio), Data(p.FimExercicioSocial ?? p.Fim));

        foreach (var c in calc.PlanoUsado)
        {
            var sup = c.ContaPaiId is long pai && d.Plano.FirstOrDefault(x => x.Id == pai) is { } s ? s.Codigo : "";
            L("I050", Data(c.DataInclusao), c.CodNat, c.Analitica ? "A" : "S", c.Nivel.ToString(), Texto(c.Codigo), c.Nivel == 1 ? "" : Texto(sup),
              Texto(c.Descricao));
            if (!c.Analitica) continue;
            if (p.CodPlanRef is not null && c.CodNat is "01" or "02" or "03" or "04" && c.CodigoReferencial is not null)
                L("I051", "", c.CodigoReferencial);
            if (calc.Aglutinacao.TryGetValue(c.Id, out var agl)) L("I052", "", Texto(agl));
        }

        if (p.IncluirHistoricosPadronizados)
            foreach (var h in d.Historicos.OrderBy(h => h.Codigo, StringComparer.Ordinal))
                L("I075", Texto(h.Codigo), Texto(h.Descricao));

        var centros = d.CentrosCusto.ToDictionary(c => c.Id);
        foreach (var c in calc.CentrosUsados.Select(id => centros[id]).OrderBy(c => c.Codigo, StringComparer.Ordinal))
            L("I100", Data(c.DataInclusao), Texto(c.Codigo), Texto(c.Descricao));
        string Centro(long id) => id == 0 ? "" : Texto(centros[id].Codigo);

        foreach (var per in calc.Periodos)
        {
            L("I150", Data(per.Inicio), Data(per.Fim));
            foreach (var s in per.Saldos)
            {
                var nat = calc.Conta(s.ContaId).Natureza == IndicadorDC.C ? 'C' : 'D';
                L("I155", Texto(calc.Codigo(s.ContaId)), Centro(s.CentroCustoId), Valor(s.Inicial), Indicador(s.Inicial, nat),
                  Valor(s.Debitos), Valor(s.Creditos), Valor(s.Final), Indicador(s.Final, nat));
            }
        }

        foreach (var l in d.Lancamentos.Where(l => l.Data >= p.Inicio && l.Data <= p.Fim).OrderBy(l => l.Data).ThenBy(l => l.Numero))
        {
            var ind = l.Indicador switch { IndicadorLancamento.Encerramento => "E", IndicadorLancamento.Extemporaneo => "X", _ => "N" };
            L("I200", l.Numero.ToString(), Data(l.Data), Valor(l.Total), ind,
              l.Indicador == IndicadorLancamento.Extemporaneo ? Data(l.DataExtemporanea) : "");
            foreach (var pt in l.Partidas)
                L("I250", Texto(calc.Codigo(pt.ContaId)), Centro(pt.CentroCustoId ?? 0), Valor(pt.Valor), pt.DC.ToString(),
                  Texto(pt.NumeroArquivo), "", Texto(pt.Historico), "");
        }

        foreach (var (data, saldos) in calc.Encerramentos)
        {
            L("I350", Data(data));
            foreach (var s in saldos)
            {
                var nat = calc.Conta(s.ContaId).Natureza == IndicadorDC.C ? 'C' : 'D';
                L("I355", Texto(calc.Codigo(s.ContaId)), Centro(s.CentroCustoId), Valor(s.Saldo), Indicador(s.Saldo, nat));
            }
        }
        L("I990", (linhas.Count - inicioI + 1).ToString());

        // ---------------------------------------------------------------- Bloco J
        var inicioJ = linhas.Count;
        var temDemonstracoes = calc.Balanco.Count > 0 && calc.Dre.Count > 0;
        L("J001", "0");
        if (temDemonstracoes)
        {
            L("J005", Data(p.Inicio), Data(p.Fim), "1", "");
            foreach (var b in calc.Balanco)
            {
                var seZero = b.Grupo == 'A' ? 'D' : 'C';
                L("J100", Texto(b.Codigo), b.Totalizador ? "T" : "D", b.Nivel.ToString(), b.Nivel == 1 ? "" : Texto(b.CodigoSuperior), b.Grupo.ToString(),
                  Texto(b.Descricao), Valor(b.Inicial), Indicador(b.Inicial, seZero), Valor(b.Final), Indicador(b.Final, seZero), "");
            }
            var ordem = 0;
            foreach (var r in calc.Dre)
            {
                var seZero = r.Grupo == 'R' ? 'C' : 'D';
                L("J150", (++ordem).ToString(), Texto(r.Codigo), r.Totalizador ? "T" : "D", r.Nivel.ToString(), r.Nivel == 1 ? "" : Texto(r.CodigoSuperior),
                  Texto(r.Descricao), r.TemInicial ? Valor(r.Inicial) : "", r.TemInicial ? Indicador(r.Inicial, seZero) : "",
                  Valor(r.Final), Indicador(r.Final, seZero), r.Grupo.ToString(), "");
            }
            var tipo = calc.TipoMutacoes == TipoMutacoesEcd.Dlpa ? "0" : "1";
            foreach (var m in calc.MutacoesPl)
            {
                L("J210", tipo, Texto(m.Codigo), Texto(m.Descricao), Valor(m.Inicial), Indicador(m.Inicial, 'C'), Valor(m.Final), Indicador(m.Final, 'C'), "");
                foreach (var f in m.Fatos)
                    L("J215", f.Codigo, Texto(f.Descricao), Valor(f.Valor), f.Valor >= 0 ? "C" : "D");
            }
        }
        L("J900", "TERMO DE ENCERRAMENTO", p.NumeroOrdem.ToString(), Texto(p.NaturezaLivro, 80), nome, QtdLin, Data(p.Inicio), Data(p.Fim));
        foreach (var s in d.Signatarios)
        {
            var doc = new string(s.CpfCnpj.Where(char.IsAsciiLetterOrDigit).ToArray());
            L("J930", Texto(s.Nome), doc, Texto(s.Qualificacao), s.CodigoQualificacao, Texto(s.Crc), Texto(s.Email, 60),
              Texto(s.Telefone is null ? null : new string(s.Telefone.Where(char.IsAsciiDigit).ToArray()), 14),
              s.UfCrc?.ToUpperInvariant(), Texto(s.NumeroCertidaoCrc), Data(s.ValidadeCertidaoCrc), s.ResponsavelLegal ? "S" : "N");
        }
        L("J990", (linhas.Count - inicioJ + 1).ToString());

        // ---------------------------------------------------------------- Bloco 9
        var contagem = linhas.GroupBy(x => x.Reg).ToDictionary(g => g.Key, g => g.Count());
        var regs = contagem.Keys.Concat(["9001", "9900", "9990", "9999"]).Distinct().ToList();
        contagem["9001"] = 1;
        contagem["9900"] = regs.Count;
        contagem["9990"] = 1;
        contagem["9999"] = 1;
        L("9001", "0");
        foreach (var r in regs) L("9900", r, contagem[r].ToString());
        L("9990", (regs.Count + 3).ToString());          // 9001 + 9900s + 9990 + 9999
        L("9999", (linhas.Count + 1).ToString());

        var total = linhas.Count.ToString();
        var textos = linhas.Select(x => x.Texto.Replace(QtdLin, total)).ToList();
        var conteudo = Codificar(textos);
        return new EscrituracaoEcd(textos, contagem, conteudo, Convert.ToHexString(SHA1.HashData(conteudo)));
    }
}
