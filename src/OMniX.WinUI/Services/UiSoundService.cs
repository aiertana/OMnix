using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OMniX.WinUI.Contracts.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace OMniX.WinUI.Services;

/// <summary>
///     二开：UI 交互音效。
///     tap   优先用 Assets/Sounds/click.wav，没有就现场合成一段（sine 960→1420，0.12s）
///     popup 合成 triangle 720→1080，0.2s
///     简易版（MotionEnabled=false）自动静音，也可以在设置里单独关。
///
///     之前用 winmm 的 PlaySound(SND_ASYNC)：它是进程级单通道，
///     后一次调用会把前一次掐掉，点击密集（或者正好赶上切页面的 popup 音）时
///     前一声就没了，表现就是"大部分时候没声音，偶尔响一下"。
///     换成 WinRT 的 MediaPlayer，两个音效各一个实例，重播只把 Position 拨回 0，
///     不会互相打断；启动时静音预热一次，避免第一次点击还在解码。
/// </summary>
public sealed class UiSoundService
{
    public const string SoundEnabledKey = "App.UiSound";

    private const int SampleRate = 22050;

    // 参考稿是 0.18，实测偏轻（0.12 秒的一个短音），提到 0.28 才听得清
    private const double Amplitude = 0.28;
    private const double TailGain = 0.001;

    private const double Volume = 0.8;

    private readonly ILocalSettingsService _localSettingsService;

    private MediaPlayer? _tapPlayer;
    private MediaPlayer? _popupPlayer;

    // MediaSource.CreateFromStream 要求流在播放期间一直活着，得拿字段托住
    private readonly List<InMemoryRandomAccessStream> _waveStreams = [];

    private bool _prepared;

    private DateTimeOffset _lastPlay = DateTimeOffset.MinValue;

    public UiSoundService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        Instance = this;
    }

    public static UiSoundService? Instance { get; private set; }

    /// <summary>是否播放交互音效，默认开</summary>
    public bool Enabled { get; private set; } = true;

    public event EventHandler<bool>? EnabledChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var saved = await _localSettingsService.ReadSettingAsync<string>(SoundEnabledKey);
            Enabled = !string.Equals(saved, "0", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            Enabled = true;
        }

        Prepare();
    }

    public async Task SetEnabledAsync(bool value)
    {
        Enabled = value;

        try
        {
            await _localSettingsService.SaveSettingAsync(SoundEnabledKey, value ? "1" : "0");
        }
        catch (Exception)
        {
            // 存不进去也先把开关拨过去
        }

        EnabledChanged?.Invoke(this, value);
    }

    /// <summary>点按钮：短促的"嗒"</summary>
    public void PlayTap() => Play(_tapPlayer, false);

    /// <summary>切页面 / 开弹窗：带一点厚度的"咚"</summary>
    public void PlayPopup() => Play(_popupPlayer, true);

    private void Prepare()
    {
        if (_prepared)
            return;

        try
        {
            _tapPlayer = CreatePlayer(ResolveTapSource());
            _popupPlayer = CreatePlayer(CreateWaveSource(BuildWave(true, 720, 1080, 0.15, 0.2)));

            _prepared = true;

            // 静音放一次：MediaPlayer 第一次 Play 要先解码，赶在用户点第一下之前做完，
            // 不然第一次点击（或第一次切页面）是没声的
            _ = WarmUpAsync(_tapPlayer);
            _ = WarmUpAsync(_popupPlayer);
        }
        catch (Exception)
        {
            // 音频初始化失败就当没有音效，不能连累界面
        }
    }

    private static MediaPlayer CreatePlayer(MediaSource? source)
    {
        var player = new MediaPlayer
        {
            AutoPlay = false,
            Volume = Volume,
            IsLoopingEnabled = false
        };

        if (source is not null)
            player.Source = source;

        return player;
    }

    private static async Task WarmUpAsync(MediaPlayer? player)
    {
        if (player?.Source is null)
            return;

        try
        {
            player.Volume = 0;
            player.Play();

            await Task.Delay(80);

            player.Pause();
            player.PlaybackSession.Position = TimeSpan.Zero;
            player.Volume = Volume;
        }
        catch (Exception)
        {
        }
    }

    private MediaSource? ResolveTapSource()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "click.wav");

        if (File.Exists(path))
        {
            try
            {
                return MediaSource.CreateFromUri(new Uri(path));
            }
            catch (Exception)
            {
                // Uri 构造失败就退回合成音
            }
        }

        return CreateWaveSource(BuildWave(false, 960, 1420, 0.08, 0.12));
    }

    private MediaSource? CreateWaveSource(byte[] wave)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();

            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(wave);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            }

            stream.Seek(0);

            // MediaSource.CreateFromStream 要求流在播放期间一直活着，用字段托住
            _waveStreams.Add(stream);

            return MediaSource.CreateFromStream(stream, "audio/wav");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Play(MediaPlayer? player, bool popup)
    {
        if (!Enabled)
            return;

        // 简易版整体静音
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        // 点击会连点，密集时只响一次免得变成噪音；
        // 切页面这种低频事件不节流，不然总被点击那声挡掉，等于没加
        if (!popup && (DateTimeOffset.UtcNow - _lastPlay).TotalMilliseconds < 45)
            return;

        _lastPlay = DateTimeOffset.UtcNow;

        if (player?.Source is null)
            return;

        try
        {
            // 同一个 player 重播：位置拨回开头再 Play，不会掐掉别的音效
            var session = player.PlaybackSession;

            if (session.Position > TimeSpan.Zero)
                session.Position = TimeSpan.Zero;

            player.Play();
        }
        catch (Exception)
        {
            // 声音这种锦上添花的东西，怎么样都不该拖累操作
        }
    }

    /// <summary>
    ///     合成一段 16bit 单声道 PCM。频率按指数从 startHz 滑到 endHz，
    ///     音量同样指数衰减。
    /// </summary>
    private static byte[] BuildWave(bool triangle, double startHz, double endHz,
        double rampSeconds, double totalSeconds)
    {
        var samples = (int)(SampleRate * totalSeconds);
        var buffer = new byte[44 + samples * 2];

        WriteAscii(buffer, 0, "RIFF");
        WriteInt32(buffer, 4, 36 + samples * 2);
        WriteAscii(buffer, 8, "WAVE");
        WriteAscii(buffer, 12, "fmt ");
        WriteInt32(buffer, 16, 16);
        WriteInt16(buffer, 20, 1);                     // PCM
        WriteInt16(buffer, 22, 1);                     // mono
        WriteInt32(buffer, 24, SampleRate);
        WriteInt32(buffer, 28, SampleRate * 2);
        WriteInt16(buffer, 32, 2);
        WriteInt16(buffer, 34, 16);
        WriteAscii(buffer, 36, "data");
        WriteInt32(buffer, 40, samples * 2);

        var phase = 0.0;

        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)SampleRate;

            // 频率：指数插值，滑完之后保持在 endHz
            var rampProgress = Math.Min(t / rampSeconds, 1.0);
            var frequency = startHz * Math.Pow(endHz / startHz, rampProgress);
            phase += 2 * Math.PI * frequency / SampleRate;

            var wave = triangle ? TriangleWave(phase) : Math.Sin(phase);

            // 音量：指数衰减
            var gain = Amplitude * Math.Pow(TailGain / Amplitude, t / totalSeconds);
            var value = wave * gain;

            var sample = (short)(value * short.MaxValue);

            buffer[44 + i * 2] = (byte)(sample & 0xFF);
            buffer[45 + i * 2] = (byte)((sample >> 8) & 0xFF);
        }

        return buffer;
    }

    /// <summary>三角波，输出范围 [-1, 1]</summary>
    private static double TriangleWave(double phase)
    {
        var x = (phase / (2 * Math.PI)) % 1.0;
        return 2 * Math.Abs(2 * x - 1) - 1;
    }

    private static void WriteAscii(byte[] buffer, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
            buffer[offset + i] = (byte)text[i];
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
