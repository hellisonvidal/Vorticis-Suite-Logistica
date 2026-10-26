using System.Security.Cryptography;
using System.Text;

namespace Vorticis.Domain.Seguranca;

/// <summary>
/// Hash de senha com PBKDF2-HMAC-SHA256 (nativo do .NET; recomendação OWASP: ≥ 600.000 iterações).
/// Formato armazenado: <c>pbkdf2-sha256$iteracoes$saltBase64$hashBase64</c> — o número de iterações
/// fica no próprio hash, permitindo aumentá-lo no futuro sem invalidar senhas existentes.
/// </summary>
public static class Senha
{
    public const int IteracoesPadrao = 600_000;
    public const int TamanhoMinimo = 8;
    private const string Prefixo = "pbkdf2-sha256";
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;

    public static string GerarHash(string senha, int iteracoes = IteracoesPadrao)
    {
        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), salt, iteracoes, HashAlgorithmName.SHA256, TamanhoHash);
        return $"{Prefixo}${iteracoes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string senha, string armazenado)
    {
        var partes = armazenado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefixo || !int.TryParse(partes[1], out var iteracoes) || iteracoes < 1)
            return false;
        try
        {
            var salt = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var obtido = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), salt, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(obtido, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Hash armazenado com menos iterações que o padrão atual: regravar no próximo login.</summary>
    public static bool PrecisaRehash(string armazenado) =>
        armazenado.Split('$') is [Prefixo, var it, _, _] && int.TryParse(it, out var n) && n < IteracoesPadrao;

    /// <summary>Política mínima: 8+ caracteres, com letra e número, diferente do login.</summary>
    public static IReadOnlyList<string> ValidarPolitica(string senha, string login)
    {
        var erros = new List<string>();
        if (senha.Length < TamanhoMinimo) erros.Add($"A senha deve ter ao menos {TamanhoMinimo} caracteres.");
        if (!senha.Any(char.IsLetter) || !senha.Any(char.IsDigit)) erros.Add("A senha deve conter letras e números.");
        if (string.Equals(senha, login, StringComparison.OrdinalIgnoreCase)) erros.Add("A senha não pode ser igual ao login.");
        return erros;
    }
}
