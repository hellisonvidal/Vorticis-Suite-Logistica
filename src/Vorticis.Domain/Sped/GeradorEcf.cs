using System.Security.Cryptography;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Contabil;
using static Vorticis.Domain.Sped.FormatoSped;

namespace Vorticis.Domain.Sped;

/// <summary>
/// Gera o arquivo da ECF (Escrituração Contábil Fiscal) — leiaute 12 (COD_VER 0012, ano-calendário 2025 e situações especiais de 2026),
/// conforme o Manual de Orientação do Leiaute 12 da ECF (anexo ao ADE Cofis nº 2/2026, atualização de 20/05/2026) e as Tabelas
/// Dinâmicas de 28/05/2026. Mesmo formato de arquivo da ECD (<see cref="FormatoSped"/>: ISO-8859-1, CR LF, "|" nas pontas).
/// <para>
/// Registros gerados: bloco 0 (0000, 0001, 0010, 0020, 0030, 0930, 0990); C (C001, C040 opcional, C990); E (E001, E990 — sem dados);
/// J (J001, J050, J051, J990); K (K001, K030, K155, K156, K355, K356, K990); Lucro Real: L (L001, L030, L100, L300, L990), M (M001, M010,
/// M030, M300, M305, M310, M350, M355, M360, M410, M500, M990) e N (N001, N030, N500, N630, N650, N670, N990); Lucro Presumido: P (P001,
/// P030, P100, P150, P200, P300, P400, P500, P990); Y (Y001, Y600, Y672, Y990); blocos sem dados (Q, S, T, U, V, W, X) e bloco 9.
/// </para>
/// </summary>
public static class GeradorEcf
{
    public const string VersaoLeiaute = "0012";

    public static EscrituracaoEcf Gerar(DadosEcf d) => Gerar(d, new CalculoEcf(d));

    public static EscrituracaoEcf Gerar(DadosEcf d, CalculoEcf calc)
    {
        var p = d.Parametros;
        var e = d.Empresa;
        var real = p.Forma == FormaTributacaoEcf.Real;
        var linhas = new List<(string Reg, string Texto)>();
        void L(string reg, params string?[] campos) => linhas.Add((reg, Linha(reg, campos)));
        // Abre um bloco (xxx1), roda o conteúdo e fecha (xxx90) com a quantidade de linhas do bloco.
        void Bloco(char letra, bool comDados, Action? conteudo = null)
        {
            var inicio = linhas.Count;
            L($"{letra}001", comDados ? "0" : "1");
            if (comDados) conteudo?.Invoke();
            L($"{letra}990", (linhas.Count - inicio + 1).ToString());
        }
        string Ind(decimal v, char seZero = 'D') => Indicador(v, seZero);

        var cnpj = new string(e.Cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var nome = Texto(e.Nome);
        var porId = d.Plano.ToDictionary(c => c.Id);

        // ---------------------------------------------------------------- Bloco 0
        var fp = new char[4];
        for (var t = 1; t <= 4; t++) fp[t - 1] = calc.Periodos.Any(x => x.Trimestre == t) ? (real ? 'R' : 'P') : '0';
        var inicio0 = linhas.Count;
        L("0000", "LECF", VersaoLeiaute, cnpj, nome, p.IndicadorSituacaoInicio.ToString(), "0", "", "", Data(p.Inicio), Data(p.Fim),
          p.Retificadora ? "S" : "N", p.Retificadora ? p.NumeroRecibo : "", "0", "");
        L("0001", "0");
        L("0010", "", "N", real ? "1" : "5", "T", "01", new string(fp), "", real ? "" : "C", "", "", "", real ? "" : p.IndicadorReceita.ToString());
        L("0020", ["1", "0", .. Enumerable.Repeat("N", 28), ""]);
        L("0030", Texto(e.NaturezaJuridica), new string(e.Cnae.Where(char.IsAsciiDigit).ToArray()), Texto(e.Logradouro, 150), Texto(e.Numero, 6),
          Texto(e.Complemento, 50), Texto(e.Bairro, 50), e.Uf.ToUpperInvariant(), e.CodMunicipioIbge, new string((e.Cep ?? "").Where(char.IsAsciiDigit).ToArray()),
          new string((e.Telefone ?? "").Where(char.IsAsciiDigit).ToArray()), Texto(e.Email, 115));
        foreach (var s in d.Signatarios)
        {
            var doc = new string(s.CpfCnpj.Where(char.IsAsciiLetterOrDigit).ToArray());
            L("0930", Texto(s.Nome), doc, s.CodigoQualificacao, Texto(s.Crc),
              Texto(s.Email, 60), Texto(s.Telefone is null ? null : new string(s.Telefone.Where(char.IsAsciiDigit).ToArray()), 14));
        }
        L("0990", (linhas.Count - inicio0 + 1).ToString());

        // ---------------------------------------------------------------- Bloco C (recuperação da ECD)
        Bloco('C', d.Ecd is not null, () =>
        {
            var x = d.Ecd!;
            L("C040", x.HashSha1, Data(x.Inicio), Data(x.Fim), "", cnpj, x.NumeroOrdem.ToString(), Nire.Normalizar(x.Nire), Texto(x.NaturezaLivro, 80),
              "9.00", "G", "N", "N", "0", "0", p.CodPlanRef);
        });

        // ---------------------------------------------------------------- Bloco E (saldos da ECF anterior: sem dados)
        Bloco('E', false);

        // ---------------------------------------------------------------- Bloco J (plano de contas do contribuinte)
        Bloco('J', calc.PlanoUsado.Count > 0, () =>
        {
            foreach (var c in calc.PlanoUsado)
            {
                var sup = c.ContaPaiId is long pai && porId.TryGetValue(pai, out var s) ? s.Codigo : "";
                L("J050", Data(c.DataInclusao), c.CodNat, c.Analitica ? "A" : "S", c.Nivel.ToString(), Texto(c.Codigo), c.Nivel == 1 ? "" : Texto(sup), Texto(c.Descricao));
                if (c.Analitica && c.CodigoReferencial is not null && c.CodNat is "01" or "02" or "03" or "04") L("J051", "", c.CodigoReferencial);
            }
        });

        // ---------------------------------------------------------------- Bloco K (saldos contábeis por período)
        Bloco('K', calc.Periodos.Count > 0, () =>
        {
            for (var i = 0; i < calc.Periodos.Count; i++)
            {
                var per = calc.Periodos[i];
                L("K030", Data(per.Inicio), Data(per.Fim), per.Codigo);
                foreach (var s in calc.Patrimoniais[i].Where(s => s.Conta.CodigoReferencial is not null))
                {
                    var seZero = s.Conta.Natureza == IndicadorDC.C ? 'C' : 'D';
                    L("K155", Texto(s.Conta.Codigo), "", Valor(s.Inicial), Ind(s.Inicial, seZero), Valor(s.Debitos), Valor(s.Creditos), Valor(s.Final), Ind(s.Final, seZero));
                    L("K156", s.Conta.CodigoReferencial, Valor(s.Inicial), Ind(s.Inicial, seZero), Valor(s.Debitos), Valor(s.Creditos), Valor(s.Final), Ind(s.Final, seZero));
                }
                foreach (var s in calc.Resultados[i].Where(s => s.Conta.CodigoReferencial is not null))
                {
                    L("K355", Texto(s.Conta.Codigo), "", Valor(s.Saldo), Ind(s.Saldo));
                    L("K356", s.Conta.CodigoReferencial, Valor(s.Saldo), Ind(s.Saldo));
                }
            }
        });

        // ---------------------------------------------------------------- Bloco L (Lucro Real: balanço e DRE referenciais) / P (Lucro Presumido)
        void Referenciais(string regBalanco, string regDre, int i)
        {
            foreach (var b in calc.Balanco[i])
            {
                var c = b.Conta;
                var seZero = c.CodNat == "01" ? 'D' : 'C';
                L(regBalanco, c.Codigo, Texto(c.Descricao), c.Analitica ? "A" : "S", c.Nivel.ToString(), c.CodNat, c.CodigoPai ?? "",
                  Valor(b.Inicial), Ind(b.Inicial, seZero), Valor(b.Debitos), Valor(b.Creditos), Valor(b.Final), Ind(b.Final, seZero));
            }
            foreach (var r in calc.Dre[i])
            {
                var c = r.Conta;
                L(regDre, c.Codigo, Texto(c.Descricao), c.Analitica ? "A" : "S", c.Nivel.ToString(), "04", c.CodigoPai ?? "", Valor(r.Final), Ind(r.Final, 'C'));
            }
        }
        void Tabela(string reg, IEnumerable<LinhaTabelaEcf> itens)
        {
            foreach (var t in itens) L(reg, t.Codigo, Texto(t.Descricao), ValorComSinal(t.Valor));
        }
        void ParteA(string reg, string filhoB, string filhoC, IEnumerable<LancamentoParteAEcf> itens)
        {
            foreach (var m in itens)
            {
                L(reg, m.Codigo, Texto(m.Descricao), m.Tipo?.ToString(), m.Relacao?.ToString(), ValorComSinal(m.Valor), Texto(m.Historico, 500));
                foreach (var b in m.ContasB) L(filhoB, b.CodContaB, Valor(b.Valor), b.DC.ToString());
                foreach (var c in m.Contas) L(filhoC, Texto(c.CodConta), "", Valor(c.Valor), c.DC.ToString());
            }
        }

        Bloco('L', real && calc.Periodos.Count > 0, () =>
        {
            for (var i = 0; i < calc.Periodos.Count; i++)
            {
                var per = calc.Periodos[i];
                L("L030", Data(per.Inicio), Data(per.Fim), per.Codigo);
                Referenciais("L100", "L300", i);
            }
        });

        Bloco('M', real && calc.Periodos.Count > 0, () =>
        {
            foreach (var c in calc.ContasParteB)
                L("M010", c.Codigo, Texto(c.Descricao), Data(c.Criacao), c.CodPbRfb, "", c.Tributo.ToString(), Valor(c.SaldoInicial), Ind(c.SaldoInicial), "");
            foreach (var r in calc.Real)
            {
                L("M030", Data(r.Periodo.Inicio), Data(r.Periodo.Fim), r.Periodo.Codigo);
                ParteA("M300", "M305", "M310", r.M300);
                ParteA("M350", "M355", "M360", r.M350);
                foreach (var m in r.M410) L("M410", m.CodContaB, m.Tributo.ToString(), Valor(m.Valor), m.Indicador, "", Texto(m.Historico), "N");
                foreach (var m in r.M500.Where(m => calc.ContasParteB.Any(c => c.Codigo == m.CodContaB)))
                    L("M500", m.CodContaB, m.Tributo.ToString(), Valor(m.Inicial), Ind(m.Inicial), Valor(m.ParteA), Ind(m.ParteA), Valor(m.ParteB), Ind(m.ParteB),
                      Valor(m.Final), Ind(m.Final));
            }
        });

        Bloco('N', real && calc.Periodos.Count > 0, () =>
        {
            foreach (var r in calc.Real)
            {
                L("N030", Data(r.Periodo.Inicio), Data(r.Periodo.Fim), r.Periodo.Codigo);
                Tabela("N500", r.N500);
                Tabela("N630", r.N630);
                Tabela("N650", r.N650);
                Tabela("N670", r.N670);
            }
        });

        Bloco('P', !real && calc.Periodos.Count > 0, () =>
        {
            for (var i = 0; i < calc.Periodos.Count; i++)
            {
                var per = calc.Periodos[i];
                L("P030", Data(per.Inicio), Data(per.Fim), per.Codigo);
                Referenciais("P100", "P150", i);
                var a = calc.Presumido[i];
                Tabela("P200", a.P200);
                Tabela("P300", a.P300);
                Tabela("P400", a.P400);
                Tabela("P500", a.P500);
            }
        });

        foreach (var letra in "QSTUVWX") Bloco(letra, false);

        // ---------------------------------------------------------------- Bloco Y (informações gerais)
        var socios = d.Socios.Where(s => s.Documento is { Length: 11 or 14 } && s.TipoPessoa is 'F' or 'J'
                                         && (s.Saida is null || s.Saida >= p.Inicio) && (s.Entrada is null || s.Entrada <= p.Fim)).ToList();
        Bloco('Y', socios.Count > 0 || !real, () =>
        {
            foreach (var s in socios)
            {
                var pf = s.TipoPessoa == 'F';
                var perc = (s.Participacao ?? 0) == 100 ? "100" : Percentual(s.Participacao ?? 0);
                L("Y600", Data(s.Entrada ?? e.DataInicioAtividades), s.Saida is { } sa && sa >= p.Inicio && sa <= p.Fim ? Data(sa) : "", "105", pf ? "PF" : "PJ",
                  s.Documento, Texto(s.Nome), pf ? "02" : "04", perc, perc, "", "", "0,00", "0,00", "0,00", "0,00", "0,00");
            }
            if (!real)
                L("Y672", "", "", "", "", "", "", "", "", "", "", "", "", "", "", Valor(d.Apuracoes.Where(a => a.Regime == FormaTributacaoEcf.Presumido).Sum(a => a.ReceitaBruta)),
                  Valor(calc.TotalAtivoFinal), "");
        });

        // ---------------------------------------------------------------- Bloco 9
        var contagem = new Dictionary<string, int>();
        foreach (var (reg, _) in linhas) contagem[reg] = contagem.GetValueOrDefault(reg) + 1;
        var regs = contagem.Keys.Concat(["9001", "9900", "9990", "9999"]).Distinct().ToList();
        contagem["9001"] = 1;
        contagem["9900"] = regs.Count;
        contagem["9990"] = 1;
        contagem["9999"] = 1;
        L("9001", "0");
        foreach (var r in regs) L("9900", r, contagem[r].ToString(), "", "");
        L("9990", (regs.Count + 3).ToString());          // 9001 + 9900s + 9990 + 9999
        L("9999", (linhas.Count + 1).ToString());

        var textos = linhas.Select(x => x.Texto).ToList();
        var conteudo = Codificar(textos);
        return new EscrituracaoEcf(textos, contagem, conteudo, Convert.ToHexString(SHA1.HashData(conteudo)));
    }
}
