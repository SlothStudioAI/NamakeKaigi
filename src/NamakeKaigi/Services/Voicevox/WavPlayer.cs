using System.IO;
using System.Media;

namespace NamakeKaigi.Services.Voicevox;

/// <summary>WAVの音声を鳴らす。</summary>
public interface IWavPlayer
{
    /// <summary>鳴らし終わるまで待つ。取り消されたら、すぐ止めて終わる。</summary>
    /// <param name="wav">WAVのバイト列</param>
    /// <param name="cancellationToken">取り消し</param>
    Task PlayAsync(byte[] wav, CancellationToken cancellationToken);

    /// <summary>鳴らしているものを止める。</summary>
    void Stop();
}

/// <summary>Windows標準の SoundPlayer でWAVを鳴らす(追加のソフトは要らない)。</summary>
public sealed class SoundWavPlayer : IWavPlayer
{
    private SoundPlayer? _current;

    /// <inheritdoc />
    public Task PlayAsync(byte[] wav, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            using var stream = new MemoryStream(wav);
            using var player = new SoundPlayer(stream);
            _current = player;
            using var registration = cancellationToken.Register(Stop);
            player.Load();
            if (!cancellationToken.IsCancellationRequested) player.PlaySync();
            _current = null;
        }, CancellationToken.None);
    }

    /// <inheritdoc />
    public void Stop() => _current?.Stop();
}
