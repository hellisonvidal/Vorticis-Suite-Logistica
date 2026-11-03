using System.Text.Json;
using Vorticis.Fiscal;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Frota;
using Vorticis.Infra.Api;

namespace Vorticis.Logistica.Desktop;

/// <summary>
/// Funções da integração por API do Vorticis Suite Logística (versão v1). As consultas e gravações passam pelos mesmos serviços das telas,
/// com as permissões do usuário da chave. Datas no formato aaaa-mm-dd; sem período, os últimos 30 dias.
/// </summary>
public static class RotasApi
{
    /// <summary>Corpo do POST /api/v1/abastecimentos.</summary>
    public sealed record NovoAbastecimentoApi(string Placa, DateOnly Data, decimal Km, decimal Litros, decimal Valor, string? Combustivel = null,
                                              string? Posto = null, bool TanqueCheio = true, string? Observacao = null);

    private static (DateOnly Inicio, DateOnly Fim) Periodo(PedidoApi p)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var fim = p.Data("fim", hoje);
        return (p.Data("inicio", fim.AddDays(-30)), fim);
    }

    private static async Task<RespostaApi> DuplicatasAsync(PedidoApi p, TipoDuplicata tipo, CancellationToken ct)
    {
        var (ini, fim) = Periodo(p);
        var r = await p.App.Fiscal().Duplicatas.ConsultarAsync(p.Acesso.EmpresaId, new FiltroDuplicatas(tipo, ini, fim, SomenteAbertas: p.Parametro("abertas") == "true"),
                                                              p.Acesso.UsuarioId, ct);
        return r.Sucesso ? RespostaApi.Ok(r.Valor) : RespostaApi.Erro(403, [.. r.Erros]);
    }

    public static IReadOnlyList<RotaApi> Todas { get; } =
    [
        new("GET", "/api/v1/veiculos", false, "Veículos cadastrados",
            async (p, ct) => RespostaApi.Ok(await ServicosFrota.De(p.App).VeiculosAsync(p.Acesso.EmpresaId, p.Acesso.UsuarioId, ct))),
        new("GET", "/api/v1/motoristas", false, "Motoristas cadastrados",
            async (p, ct) => RespostaApi.Ok(await ServicosFrota.De(p.App).MotoristasAsync(p.Acesso.EmpresaId, p.Acesso.UsuarioId, ct))),
        new("GET", "/api/v1/abastecimentos", false, "Abastecimentos do período (?inicio=aaaa-mm-dd&fim=aaaa-mm-dd)", async (p, ct) =>
        {
            var (ini, fim) = Periodo(p);
            return RespostaApi.Ok(await ServicosFrota.De(p.App).AbastecimentosAsync(p.Acesso.EmpresaId, ini, fim, null, p.Acesso.UsuarioId, ct));
        }),
        new("POST", "/api/v1/abastecimentos", true, "Grava um abastecimento (placa, data, km, litros, valor; combustível opcional)", async (p, ct) =>
        {
            NovoAbastecimentoApi? n;
            try { n = JsonSerializer.Deserialize<NovoAbastecimentoApi>(p.Corpo, ServidorApi.Json); }
            catch (JsonException ex) { return RespostaApi.Erro(400, "JSON inválido: " + ex.Message); }
            if (n is null || string.IsNullOrWhiteSpace(n.Placa)) return RespostaApi.Erro(400, "Informe a placa, a data, o km, os litros e o valor.");
            var frota = ServicosFrota.De(p.App);
            var placa = RegrasFrota.NormalizarPlaca(n.Placa);
            var veiculo = (await frota.VeiculosAsync(p.Acesso.EmpresaId, p.Acesso.UsuarioId, ct)).FirstOrDefault(v => v.Placa == placa);
            if (veiculo is null) return RespostaApi.Erro(404, $"Veículo de placa {n.Placa} não cadastrado.");
            var r = await frota.GravarAbastecimentoAsync(p.Acesso.EmpresaId, new Abastecimento(0, veiculo.Id, null, n.Data, n.Km, n.Litros, n.Valor,
                n.Combustivel ?? veiculo.Combustivel, n.Posto, n.TanqueCheio, n.Observacao), p.Acesso.UsuarioId, ct);
            return r.Sucesso ? RespostaApi.Criado(new { id = r.Valor }) : RespostaApi.Erro(422, [.. r.Erros]);
        }),
        new("GET", "/api/v1/ctes", false, "Conhecimentos de transporte (CT-e) do período (?inicio&fim&situacao=DIGITADO|EMITIDO|CANCELADO)", async (p, ct) =>
        {
            var (ini, fim) = Periodo(p);
            return RespostaApi.Ok(await ServicosFrota.De(p.App).CtesAsync(p.Acesso.EmpresaId, ini, fim, p.Parametro("situacao"), p.Acesso.UsuarioId, ct));
        }),
        new("GET", "/api/v1/contas-receber", false, "Contas a receber com vencimento no período (?inicio&fim&abertas=true)", (p, ct) => DuplicatasAsync(p, TipoDuplicata.Receber, ct)),
        new("GET", "/api/v1/contas-pagar", false, "Contas a pagar com vencimento no período (?inicio&fim&abertas=true)", (p, ct) => DuplicatasAsync(p, TipoDuplicata.Pagar, ct)),
    ];
}
