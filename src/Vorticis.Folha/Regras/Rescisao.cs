using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

public enum MotivoRescisao { SemJustaCausa, PedidoDemissao, JustaCausa, Acordo484A, TerminoContrato }

public enum TipoAviso { Indenizado, Trabalhado, NaoCumprido, Dispensado }

public static class MotivoRescisaoExtensoes
{
    public static string Sql(this MotivoRescisao m) => m switch
    {
        MotivoRescisao.SemJustaCausa => "SEM_JUSTA_CAUSA",
        MotivoRescisao.PedidoDemissao => "PEDIDO_DEMISSAO",
        MotivoRescisao.JustaCausa => "JUSTA_CAUSA",
        MotivoRescisao.Acordo484A => "ACORDO_484A",
        MotivoRescisao.TerminoContrato => "TERMINO_CONTRATO",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    public static MotivoRescisao DeSql(string s) => Enum.GetValues<MotivoRescisao>().Single(m => m.Sql() == s);

    public static string Descricao(this MotivoRescisao m) => m switch
    {
        MotivoRescisao.SemJustaCausa => "Rescisão sem justa causa (iniciativa do empregador)",
        MotivoRescisao.PedidoDemissao => "Pedido de demissão",
        MotivoRescisao.JustaCausa => "Rescisão por justa causa",
        MotivoRescisao.Acordo484A => "Rescisão por acordo (CLT, art. 484-A)",
        MotivoRescisao.TerminoContrato => "Término do contrato a termo",
        _ => "",
    };

    /// <summary>Código do motivo de desligamento (eSocial, Tabela 19).</summary>
    public static string CodigoEsocial(this MotivoRescisao m) => m switch
    {
        MotivoRescisao.JustaCausa => "01",
        MotivoRescisao.SemJustaCausa => "02",
        MotivoRescisao.TerminoContrato => "06",
        MotivoRescisao.PedidoDemissao => "07",
        MotivoRescisao.Acordo484A => "33",
        _ => "99",
    };

    public static string Sql(this TipoAviso t) => t switch
    {
        TipoAviso.Indenizado => "INDENIZADO",
        TipoAviso.Trabalhado => "TRABALHADO",
        TipoAviso.NaoCumprido => "NAO_CUMPRIDO",
        _ => "DISPENSADO",
    };

    public static TipoAviso AvisoDeSql(string s) => Enum.GetValues<TipoAviso>().Single(t => t.Sql() == s);
}

/// <param name="AquisitivoInicio">Início do período aquisitivo com férias vencidas e não gozadas.</param>
/// <param name="EmDobro">Período concessivo já expirado: férias pagas em dobro (CLT, art. 137).</param>
public sealed record FeriasVencidas(DateOnly AquisitivoInicio, int Dias, bool EmDobro);

public sealed record SolicitacaoRescisao(MotivoRescisao Motivo, DateOnly DataDesligamento, TipoAviso Aviso, decimal MediaVariaveis,
                                         decimal SaldoFgts, IReadOnlyList<FeriasVencidas> Vencidas, decimal DecimoPrimeiraParcelaPaga = 0);

public static class CalculadoraRescisao
{
    /// <summary>Aviso prévio proporcional (Lei 12.506/2011): 30 dias + 3 por ano completo de serviço, até 90 dias.</summary>
    public static int DiasAviso(DateOnly admissao, DateOnly desligamento)
    {
        var anos = AnosCompletos(admissao, desligamento);
        return Math.Min(90, 30 + 3 * Math.Max(0, anos));
    }

    public static int AnosCompletos(DateOnly de, DateOnly ate)
    {
        var anos = ate.Year - de.Year;
        if (ate < de.AddYears(anos)) anos--;
        return anos;
    }

    /// <summary>
    /// Avos de um período a partir de <paramref name="inicio"/> até <paramref name="fim"/>: cada mês completo conta 1/12 e a
    /// fração de 15 dias ou mais também (CLT, art. 146, parágrafo único, e Súmula 171 do TST).
    /// </summary>
    public static int AvosPeriodo(DateOnly inicio, DateOnly fim)
    {
        var meses = 0;
        while (inicio.AddMonths(meses + 1).AddDays(-1) <= fim) meses++;
        var resto = fim.DayNumber - inicio.AddMonths(meses).DayNumber + 1;
        if (resto >= 15) meses++;
        return Math.Min(12, meses);
    }

    /// <summary>Data do desligamento projetada pelo aviso prévio indenizado (base de férias e 13º proporcionais).</summary>
    public static DateOnly DataProjetada(DateOnly admissao, SolicitacaoRescisao s)
    {
        if (s.Aviso != TipoAviso.Indenizado) return s.DataDesligamento;
        var dias = DiasAviso(admissao, s.DataDesligamento);
        return s.DataDesligamento.AddDays(s.Motivo == MotivoRescisao.Acordo484A ? dias / 2 : dias);
    }

    public static IReadOnlyList<string> Validar(FuncionarioCalc f, SolicitacaoRescisao s)
    {
        var erros = new List<string>();
        if (s.DataDesligamento < f.Admissao) erros.Add("A data do desligamento não pode ser anterior à admissão.");
        var permitidos = s.Motivo switch
        {
            MotivoRescisao.SemJustaCausa or MotivoRescisao.Acordo484A => new[] { TipoAviso.Indenizado, TipoAviso.Trabalhado, TipoAviso.Dispensado },
            MotivoRescisao.PedidoDemissao => [TipoAviso.Trabalhado, TipoAviso.NaoCumprido, TipoAviso.Dispensado],
            _ => [TipoAviso.Dispensado],
        };
        if (!permitidos.Contains(s.Aviso)) erros.Add($"Tipo de aviso prévio incompatível com o motivo \"{s.Motivo.Descricao()}\".");
        if (s.SaldoFgts < 0) erros.Add("O saldo do FGTS não pode ser negativo.");
        return erros;
    }

    /// <summary>
    /// Rescisão: saldo de salário, aviso prévio indenizado (proporcional; metade no acordo), férias vencidas e proporcionais
    /// (+1/3), 13º proporcional e multa do FGTS. Aviso e férias indenizadas não têm INSS nem IRRF; o 13º tem tributação própria.
    /// </summary>
    public static ResultadoFolha Calcular(FuncionarioCalc f, SolicitacaoRescisao s, ContextoCalculo ctx)
    {
        var erros = Validar(f, s);
        if (erros.Count > 0) throw new ErroCalculoFolhaException($"{f.Nome}: {string.Join(" ", erros)}");

        ItemCalculo Novo(string codigo, decimal referencia, decimal valor, string memoria)
        {
            var r = ctx.Rubrica(codigo);
            return new ItemCalculo(codigo, r.Descricao, r.Tipo, referencia, Dinheiro.Arredondar(valor), memoria, r.IncInss, r.IncIrrf, r.IncFgts, r.Variavel);
        }

        var itens = new List<ItemCalculo>();
        var sm = ctx.Tabelas.SalarioMinimo;
        var remuneracao = PeriodosFerias.Remuneracao(f, sm, s.MediaVariaveis);
        var memoRem = $"remuneração {Formato.Moeda(remuneracao)} (salário + adicionais + média de variáveis {Formato.Moeda(s.MediaVariaveis)})";
        var competencia = new DateOnly(s.DataDesligamento.Year, s.DataDesligamento.Month, 1);

        // Saldo de salário
        var diasSaldo = Calendario.DiasTrabalhadosComerciais(competencia, f.Admissao, s.DataDesligamento);
        var fixa = f.SalarioMensal + PeriodosFerias.AdicionalFixoMensal(f, sm);
        if (diasSaldo > 0)
            itens.Add(Novo(RubricasPadrao.SaldoSalario, diasSaldo, fixa * diasSaldo / 30m,
                $"({Formato.Moeda(f.SalarioMensal)} salário + {Formato.Moeda(PeriodosFerias.AdicionalFixoMensal(f, sm))} adicionais) x {diasSaldo}/30 dias"));

        // Aviso prévio
        var dataProjetada = s.DataDesligamento;
        var diasAvisoLei = DiasAviso(f.Admissao, s.DataDesligamento);
        if (s.Aviso == TipoAviso.Indenizado)
        {
            var diasPagos = s.Motivo == MotivoRescisao.Acordo484A ? diasAvisoLei / 2 : diasAvisoLei;
            itens.Add(Novo(RubricasPadrao.AvisoPrevioIndenizado, diasPagos, remuneracao / 30m * diasPagos,
                $"{diasPagos} dias x ({memoRem} ÷ 30)" + (s.Motivo == MotivoRescisao.Acordo484A
                    ? $" — acordo: metade dos {diasAvisoLei} dias (Lei 12.506/2011)" : $" — 30 dias + 3 por ano completo (Lei 12.506/2011) = {diasAvisoLei}")));
            dataProjetada = s.DataDesligamento.AddDays(diasPagos);
        }
        else if (s.Aviso == TipoAviso.NaoCumprido)
            itens.Add(Novo(RubricasPadrao.AvisoNaoCumprido, 30, remuneracao, $"Aviso não cumprido pelo empregado: 30 dias de {memoRem}"));

        var direitoVerbas = s.Motivo != MotivoRescisao.JustaCausa;

        // Férias vencidas (sempre devidas, inclusive na justa causa)
        var vencDias = s.Vencidas.Sum(v => v.Dias);
        if (vencDias > 0)
        {
            var valor = s.Vencidas.Sum(v => remuneracao / 30m * v.Dias * (v.EmDobro ? 2 : 1));
            var dobro = s.Vencidas.Any(v => v.EmDobro) ? " (em dobro onde o período concessivo expirou)" : "";
            itens.Add(Novo(RubricasPadrao.FeriasVencidas, vencDias, valor, $"{vencDias} dias x ({memoRem} ÷ 30){dobro}"));
            itens.Add(Novo(RubricasPadrao.TercoFeriasVencidas, vencDias, Dinheiro.Arredondar(valor) / 3m, "1/3 constitucional sobre as férias vencidas"));
        }

        // Férias proporcionais e 13º proporcional (não devidos na justa causa)
        if (direitoVerbas)
        {
            var anosCompletos = AnosCompletos(f.Admissao, dataProjetada);
            var inicioPeriodo = f.Admissao.AddYears(Math.Max(0, anosCompletos));
            var avosFerias = AvosPeriodo(inicioPeriodo, dataProjetada);
            if (avosFerias > 0)
            {
                var valor = remuneracao / 12m * avosFerias;
                itens.Add(Novo(RubricasPadrao.FeriasProporcionais, avosFerias, valor,
                    $"{avosFerias}/12 avos do período aquisitivo iniciado em {inicioPeriodo:dd/MM/yyyy} x {memoRem}"
                    + (dataProjetada != s.DataDesligamento ? $"; aviso projetado até {dataProjetada:dd/MM/yyyy}" : "")));
                itens.Add(Novo(RubricasPadrao.TercoFeriasProporcionais, avosFerias, Dinheiro.Arredondar(valor) / 3m, "1/3 constitucional sobre as férias proporcionais"));
            }

            var avos13 = CalculadoraDecimoTerceiro.Avos(f.Admissao, dataProjetada, dataProjetada.Year);
            if (avos13 > 0)
            {
                itens.Add(Novo(RubricasPadrao.DecimoProporcional, avos13, remuneracao / 12m * avos13,
                    $"{avos13}/12 avos de {dataProjetada.Year} x {memoRem}" + (dataProjetada != s.DataDesligamento ? "; inclui a projeção do aviso prévio" : "")));
                if (s.DecimoPrimeiraParcelaPaga > 0)
                    itens.Add(Novo(RubricasPadrao.AdiantamentoDecimo, 0, s.DecimoPrimeiraParcelaPaga, "1ª parcela do 13º já paga"));
            }
        }

        // INSS/IRRF/FGTS: o fechamento comum trata o saldo de salário (mensal); o 13º tem tributação exclusiva à parte
        var resultado = CalculadoraFolhaMensal.Fechar(f, itens, ctx with { Acumulado = AcumuladoMes.Nenhum }, Novo);
        var extra = new List<ItemCalculo>();
        decimal inss13 = 0, irrf13 = 0;
        var memInss = resultado.MemoriaInss;
        var memIrrf = resultado.MemoriaIrrf;
        var decimo = itens.FirstOrDefault(i => i.Codigo == RubricasPadrao.DecimoProporcional)?.Valor ?? 0;
        if (decimo > 0)
        {
            if (!f.Estagiario) inss13 = CalculoInss.Calcular(decimo, ctx.Tabelas.Inss, out var m1);
            var r13 = CalculoIrrf.Calcular(decimo, inss13, f.DependentesIrrf, 0, ctx.Tabelas.Irrf);
            irrf13 = r13.Imposto;
            if (inss13 > 0) extra.Add(Novo(RubricasPadrao.InssDecimo, 0, inss13, $"13º proporcional: tabela própria. Base {Formato.Moeda(decimo)}"));
            if (irrf13 > 0) extra.Add(Novo(RubricasPadrao.IrrfDecimo, 0, irrf13, "13º proporcional: " + r13.Memoria));
            memInss += $"; 13º: {Formato.Moeda(inss13)}";
            memIrrf += $"; 13º: {Formato.Moeda(irrf13)}";
        }

        // Multa do FGTS: base = saldo da conta + depósito devido na rescisão (saldo de salário, aviso e 13º)
        decimal multa = 0;
        var pct = s.Motivo switch
        {
            MotivoRescisao.SemJustaCausa => ctx.Tabelas.Fgts.MultaSemJustaCausa,
            MotivoRescisao.Acordo484A => ctx.Tabelas.Fgts.MultaAcordo,
            _ => 0m,
        };
        if (pct > 0)
        {
            var depositoRescisao = resultado.Fgts;
            var baseMulta = s.SaldoFgts + depositoRescisao;
            multa = Dinheiro.Arredondar(baseMulta * pct / 100m);
            extra.Add(Novo(RubricasPadrao.MultaFgts, pct, multa,
                $"{Formato.Num(pct)}% x (saldo do FGTS {Formato.Moeda(s.SaldoFgts)} + FGTS da rescisão {Formato.Moeda(depositoRescisao)})"));
        }

        return new ResultadoFolha([.. resultado.Itens, .. extra], resultado.BaseInss, resultado.BaseIrrf, resultado.BaseFgts,
            resultado.Inss + inss13, resultado.Irrf + irrf13, resultado.Fgts, 0, memInss, memIrrf, multa);
    }
}
