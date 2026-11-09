using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NamakeKaigi.Services.Remote;

/// <summary>リモートの安全まわりの部品: 合言葉の作成・照合と、待ち受けるアドレスの決定。</summary>
public static class RemoteAccess
{
    /// <summary>推測できない合言葉(32文字の英数字)を作る。</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>合言葉が合っているか調べる(一致するまでの時間で推測されないよう、固定時間で比べる)。</summary>
    /// <param name="expected">正しい合言葉</param>
    /// <param name="given">受け取った合言葉(なければnull)</param>
    public static bool TokenMatches(string expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || given is null) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(given)));
    }

    /// <summary>Tailscaleのアドレス(100.64.0.0/10)か。</summary>
    /// <param name="address">調べるアドレス</param>
    public static bool IsTailscale(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    /// <summary>待ち受けるアドレスを決める。設定があればそれ、なければTailscaleのアドレス、なければ自分のPC内だけ。</summary>
    /// <param name="configured">設定のアドレス(空なら自動)</param>
    /// <param name="local">このPCが持つアドレス</param>
    /// <returns>アドレスと、自分のPC内だけに絞ったか(Tailscaleが見つからなかったときtrue)</returns>
    public static (IPAddress Address, bool LoopbackOnly) PickBindAddress(string? configured, IEnumerable<IPAddress> local)
    {
        if (!string.IsNullOrWhiteSpace(configured) && IPAddress.TryParse(configured.Trim(), out var fixedAddress))
            return (fixedAddress, IPAddress.IsLoopback(fixedAddress));
        var tailscale = local.FirstOrDefault(IsTailscale);
        return tailscale is not null ? (tailscale, false) : (IPAddress.Loopback, true);
    }

    /// <summary>このPCのIPv4アドレスの一覧を返す。</summary>
    public static IReadOnlyList<IPAddress> LocalAddresses() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
}
