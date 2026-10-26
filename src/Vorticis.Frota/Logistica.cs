using System.Globalization;
using System.Text.Json;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota;

/// <summary>Frota (grupo de veículos): própria, agregada ou de terceiros; pode ter um centro de custo padrão.</summary>
public sealed record FrotaCadastro(long Id, int Codigo, string Descricao, string Tipo, string? Responsavel, long? CentroCustoId, bool Ativa = true,
                                   string? CentroCustoNome = null, int Veiculos = 0);

/// <summary>Município de uma rota (UF + código IBGE).</summary>
public sealed record ParadaRota(string Uf, string MunicipioIbge, string Municipio);

/// <summary>Rota: a primeira parada é a origem, a última o destino e as do meio as paradas intermediárias. DistanciaOrigem: ESTIMADA, RODOVIARIA ou MANUAL.</summary>
public sealed record Rota(long Id, int Codigo, string Descricao, decimal DistanciaKm, string DistanciaOrigem, decimal TempoHoras, decimal Pedagios, bool Ativa,
                          IReadOnlyList<ParadaRota> Paradas)
{
    public string Origem => Paradas.Count > 0 ? $"{Paradas[0].Municipio}/{Paradas[0].Uf}" : "";
    public string Destino => Paradas.Count > 1 ? $"{Paradas[^1].Municipio}/{Paradas[^1].Uf}" : "";
    public string Trajeto => string.Join(" → ", Paradas.Select(p => $"{p.Municipio}/{p.Uf}"));
}

/// <summary>Diária de motorista ou ajudante. Saldo do acerto = adiantamento - valor gasto (positivo: o colaborador devolve; negativo: a empresa paga a diferença).</summary>
public sealed record Diaria(long Id, int Numero, DateOnly Data, long MotoristaId, string Funcao, long? VeiculoId, long? FrotaId, long? ViagemId, long? RotaId, string Tipo,
                            decimal Quantidade, decimal ValorUnitario, decimal Adiantamento, decimal? ValorGasto, string Situacao, long? CentroCustoId, long? DuplicataId,
                            string? Observacao, string? Colaborador = null, string? Placa = null, string? FrotaNome = null, string? RotaNome = null, string? CentroCustoNome = null)
{
    public decimal Total => RegrasLogistica.TotalDiaria(Quantidade, ValorUnitario);
    /// <summary>O que sai no pagamento: o adiantamento, se houver; senão o total das diárias.</summary>
    public decimal ValorPagamento => Adiantamento > 0 ? Adiantamento : Total;
    public decimal? Saldo => ValorGasto is { } g ? Adiantamento - g : null;
}

/// <summary>Resultado do pagamento: a conta a pagar, o lançamento contábil (contabilização on-line) ou a pendência (em lote); Aviso quando não foi possível contabilizar.</summary>
public sealed record ResultadoPagamentoDiaria(long DuplicataId, decimal Valor, bool PendenciaRegistrada, long? LancamentoId = null, string? Aviso = null);

public sealed record Coleta(long Id, int Numero, DateTime AgendadaEm, long? RemetenteId, long? DestinatarioId, string EnderecoColeta, string? Mercadoria, int Volumes,
                            decimal PesoKg, decimal ValorMercadoria, long? VeiculoId, long? MotoristaId, long? RotaId, string Situacao, string? Observacao,
                            string? Remetente = null, string? Destinatario = null, string? Placa = null, string? MotoristaNome = null, string? RotaNome = null);

public sealed record ParticipanteResumo(long Id, string Codigo, string Nome)
{
    public override string ToString() => $"{Codigo} – {Nome}";
}

public sealed record DistanciaRodoviaria(decimal Km, decimal Horas);

public sealed record Municipio(string Ibge, string Nome, string Uf, double Latitude, double Longitude)
{
    public override string ToString() => $"{Nome}/{Uf}";
}

/// <summary>Municípios do IBGE com coordenadas (arquivo embutido; fonte: kelvins/municipios-brasileiros, licença MIT).</summary>
public static class BaseMunicipios
{
    private static readonly Lazy<IReadOnlyList<Municipio>> Carregados = new(Carregar);
    private static readonly Lazy<Dictionary<string, Municipio>> PorIbge = new(() => Carregados.Value.ToDictionary(m => m.Ibge));

    public static IReadOnlyList<Municipio> Todos => Carregados.Value;
    public static Municipio? Obter(string? ibge) => ibge is not null && PorIbge.Value.TryGetValue(ibge.Trim(), out var m) ? m : null;
    public static IEnumerable<Municipio> DaUf(string? uf) => Todos.Where(m => string.Equals(m.Uf, uf?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<Municipio> Carregar()
    {
        using var s = typeof(BaseMunicipios).Assembly.GetManifestResourceStream("Vorticis.Frota.municipios.csv")
                      ?? throw new InvalidOperationException("Base de municípios não encontrada.");
        using var r = new StreamReader(s);
        var lista = new List<Municipio>();
        while (r.ReadLine() is { Length: > 0 } linha)
        {
            var c = linha.Split(';');
            if (c.Length < 4 || Vorticis.Domain.Cadastros.Uf.DeCodigoIbge(c[0]) is not { } uf) continue;
            lista.Add(new Municipio(c[0], c[1], uf, double.Parse(c[2], CultureInfo.InvariantCulture), double.Parse(c[3], CultureInfo.InvariantCulture)));
        }
        return lista;
    }
}

public static class RegrasLogistica
{
    public const decimal RaioTerraKm = 6371m;
    public static readonly IReadOnlyList<string> TiposFrota = ["PROPRIA", "AGREGADA", "TERCEIROS"];
    public static readonly IReadOnlyList<string> TiposDiaria = ["PERNOITE", "ALIMENTACAO", "REFEICAO", "HOSPEDAGEM", "OUTRA"];
    public static readonly IReadOnlyList<string> Funcoes = ["MOTORISTA", "AJUDANTE"];
    public static readonly IReadOnlyList<string> SituacoesColeta = ["AGENDADA", "EM_COLETA", "COLETADA", "CANCELADA"];

    public static decimal TotalDiaria(decimal quantidade, decimal valorUnitario) => Math.Round(quantidade * valorUnitario, 2, MidpointRounding.AwayFromZero);

    /// <summary>Distância em linha reta (km) entre dois pontos, pela fórmula de Haversine (raio médio da Terra 6.371 km).</summary>
    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return (double)RaioTerraKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static double Haversine(Municipio a, Municipio b) => Haversine(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

    /// <summary>Soma das retas entre cidades consecutivas vezes o fator rodoviário; null se alguma parada não está na base.</summary>
    public static decimal? EstimarKm(IReadOnlyList<ParadaRota> paradas, decimal fator)
    {
        var cidades = paradas.Select(p => BaseMunicipios.Obter(p.MunicipioIbge)).ToList();
        if (cidades.Count < 2 || cidades.Any(c => c is null)) return null;
        double reta = 0;
        for (var i = 1; i < cidades.Count; i++) reta += Haversine(cidades[i - 1]!, cidades[i]!);
        return Math.Round((decimal)reta * fator, 1, MidpointRounding.AwayFromZero);
    }

    public static decimal EstimarHoras(decimal km, decimal velocidadeMedia) => velocidadeMedia > 0 ? Math.Round(km / velocidadeMedia, 2, MidpointRounding.AwayFromZero) : 0;

    public static IReadOnlyList<string> Validar(Rota r)
    {
        var erros = new List<string>();
        if (r.Codigo <= 0) erros.Add("Informe o código da rota.");
        if (string.IsNullOrWhiteSpace(r.Descricao)) erros.Add("Informe a descrição da rota.");
        if (r.Paradas.Count < 2) erros.Add("Informe ao menos a origem e o destino.");
        foreach (var p in r.Paradas)
            if (BaseMunicipios.Obter(p.MunicipioIbge) is not { } m || !string.Equals(m.Uf, p.Uf, StringComparison.OrdinalIgnoreCase))
                erros.Add($"Município inválido na rota: {p.Municipio}/{p.Uf}.");
        if (r.DistanciaKm < 0 || r.Pedagios < 0 || r.TempoHoras < 0) erros.Add("Distância, tempo e pedágios não podem ser negativos.");
        if (r.DistanciaOrigem is not ("ESTIMADA" or "RODOVIARIA" or "MANUAL")) erros.Add("Origem da distância inválida.");
        return erros.Distinct().ToList();
    }

    public static IReadOnlyList<string> Validar(FrotaCadastro f)
    {
        var erros = new List<string>();
        if (f.Codigo <= 0) erros.Add("Informe o código da frota.");
        if (string.IsNullOrWhiteSpace(f.Descricao)) erros.Add("Informe a descrição da frota.");
        if (!TiposFrota.Contains(f.Tipo)) erros.Add("Escolha o tipo da frota (própria, agregada ou terceiros).");
        return erros;
    }

    public static IReadOnlyList<string> Validar(Diaria d)
    {
        var erros = new List<string>();
        if (d.MotoristaId <= 0) erros.Add("Escolha o colaborador.");
        if (!TiposDiaria.Contains(d.Tipo)) erros.Add("Escolha o tipo da diária.");
        if (!Funcoes.Contains(d.Funcao)) erros.Add("Escolha a função (motorista ou ajudante).");
        if (d.Quantidade <= 0) erros.Add("A quantidade de diárias deve ser maior que zero.");
        if (d.ValorUnitario < 0 || d.Adiantamento < 0 || d.ValorGasto < 0) erros.Add("Valores não podem ser negativos.");
        return erros;
    }

    public static IReadOnlyList<string> Validar(Coleta c)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(c.EnderecoColeta)) erros.Add("Informe o endereço de coleta.");
        if (c.Volumes < 0 || c.PesoKg < 0 || c.ValorMercadoria < 0) erros.Add("Volumes, peso e valor não podem ser negativos.");
        if (!SituacoesColeta.Contains(c.Situacao)) erros.Add("Situação da coleta inválida.");
        return erros;
    }

    /// <summary>Agendada → Em coleta → Coletada; Cancelada a partir de agendada ou em coleta. Coletada e cancelada são finais.</summary>
    public static bool TransicaoColetaValida(string de, string para) => de == para || (de, para) switch
    {
        ("AGENDADA", "EM_COLETA" or "CANCELADA" or "COLETADA") => true,
        ("EM_COLETA", "COLETADA" or "CANCELADA") => true,
        _ => false,
    };

    public static bool ColetaEditavel(string situacao) => situacao is "AGENDADA" or "EM_COLETA";
}

/// <summary>Consulta opcional de distância rodoviária num serviço público de rotas (OSRM com dados do OpenStreetMap).</summary>
public static class ServicoRotasOnline
{
    public const string UrlPadrao = "https://router.project-osrm.org";

    public static async Task<Resultado<DistanciaRodoviaria>> ConsultarAsync(HttpClient http, IReadOnlyList<ParadaRota> paradas, string? urlBase = null, CancellationToken ct = default)
    {
        const string semServico = "Não foi possível consultar o serviço de rotas (verifique a internet). A distância estimada foi mantida.";
        var cidades = paradas.Select(p => BaseMunicipios.Obter(p.MunicipioIbge)).ToList();
        if (cidades.Count < 2 || cidades.Any(c => c is null)) return Resultado<DistanciaRodoviaria>.Falha("Informe origem e destino com municípios válidos.");
        var pontos = string.Join(';', cidades.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c!.Longitude:0.#####},{c.Latitude:0.#####}")));
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(20));
            using var resposta = await http.GetAsync($"{(urlBase ?? UrlPadrao).TrimEnd('/')}/route/v1/driving/{pontos}?overview=false", limite.Token);
            if (!resposta.IsSuccessStatusCode) return Resultado<DistanciaRodoviaria>.Falha(semServico);
            using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(limite.Token));
            var raiz = doc.RootElement;
            if (raiz.GetProperty("code").GetString() != "Ok" || raiz.GetProperty("routes").GetArrayLength() == 0)
                return Resultado<DistanciaRodoviaria>.Falha("O serviço de rotas não encontrou caminho entre as cidades. A distância estimada foi mantida.");
            var rota = raiz.GetProperty("routes")[0];
            var km = Math.Round((decimal)rota.GetProperty("distance").GetDouble() / 1000m, 1);
            var horas = Math.Round((decimal)rota.GetProperty("duration").GetDouble() / 3600m, 2);
            return Resultado<DistanciaRodoviaria>.Ok(new DistanciaRodoviaria(km, horas));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            if (ct.IsCancellationRequested) throw;
            return Resultado<DistanciaRodoviaria>.Falha(semServico);
        }
    }
}
