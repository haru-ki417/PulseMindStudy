using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace PulseMind.Core.Devices;

/// <summary>
/// 機器トークンの発行と照合。
/// トークンは 256 ビットの乱数なので総当たりでは推測できず、保存するのは SHA-256 のハッシュだけでよい
/// （パスワードのように「遅いハッシュ」を使う必要はない。パスワードは人が覚えられる短い文字列だから必要になる）。
/// </summary>
public static class DeviceTokens
{
    /// <summary>トークンの頭に付ける印。ログや設定ファイルに紛れ込んだときに、何のトークンかすぐ分かるようにする。</summary>
    public const string Prefix = "pmd_";

    /// <summary>一覧画面に表示する先頭の文字数</summary>
    public const int DisplayPrefixLength = 10;

    private const int RandomBytes = 32;

    private static readonly SearchValues<char> Base64UrlChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    /// <summary>新しいトークンを作る。平文のトークンは利用者に一度だけ見せ、保存はしない。</summary>
    public static (string Token, string DisplayPrefix, string Hash) Create()
    {
        string token = Prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomBytes));
        return (token, token[..DisplayPrefixLength], Hash(token));
    }

    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>形が明らかにおかしいものは、データベースを見る前に断る</summary>
    public static bool HasValidShape(string? token) =>
        token is { Length: > 40 and < 100 } && token.StartsWith(Prefix, StringComparison.Ordinal)
        && token.AsSpan(Prefix.Length).IndexOfAnyExcept(Base64UrlChars) < 0;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
