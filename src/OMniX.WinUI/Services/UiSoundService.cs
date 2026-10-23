using System;
using System.Runtime.InteropServices;
using OMniX.WinUI.Contracts.Services;

namespace OMniX.WinUI.Services;

/// <summary>
///     二开：UI 交互音效。参数直接照抄参考网页里的 playTapSound / playPopupSound：
///     tap   = sine     960Hz → 1420Hz，0.12s
///     popup = triangle 720Hz → 1080Hz，0.2s
///     音量 0.18 指数衰减到 0.001，短促不刺耳。
///
///     不配任何音频文件，直接在内存里合成 22kHz 的 PCM，用 winmm 的 PlaySound 异步播。
///     简易版（MotionEnabled=false）自动静音，也可以在设置里单独关。
/// </summary>
public sealed class UiSoundService
{
    public const string SoundEnabledKey = "App.UiSound";

    // winmm
    private const uint SndMemory = 0x0004;
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;

    private const int SampleRate = 22050;
    private const double Amplitude = 0.18;     // 参考稿 gain 起始值
    private const double TailGain = 0.001;     // 参考稿衰减终点

    private readonly ILocalSettingsService _localSettingsService;

    private byte[]? _tapWave;
    private byte[]? _popupWave;
    private GCHandle _tapPinned;
    private GCHandle _popupPinned;
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

    /// <summary>点按钮：短促的"嗒"（sine 960→1420）</summary>
    public void PlayTap() => Play(false);

    /// <summary>切页面 / 开弹窗：带一点厚度的"咚"（triangle 720→1080）</summary>
    public void PlayPopup() => Play(true);

    /// <summary>
    ///     没开音效、简易版、事件太密集都会直接返回，不做任何事。
    /// </summary>
    private void Play(bool popup)
    {
        if (!Enabled)
            return;

        // 简易版整体静音
        if (ThemeStyleService.Instance is { MotionEnabled: false })
            return;

        // 同一时间堆了一堆事件时只响一次，免得变成噪音
        if ((DateTimeOffset.UtcNow - _lastPlay).TotalMilliseconds < 45)
            return;

        _lastPlay = DateTimeOffset.UtcNow;

        try
        {
            var wave = popup ? EnsurePopupWave() : EnsureTapWave();

            // 内存里的波形要一直固定住，SND_ASYNC 会在别的线程上读它
            PlaySound(wave, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
        }
        catch (Exception)
        {
            // 声音这种锦上添花的东西，怎么样都不该拖累操作
        }
    }

    private byte[] EnsureTapWave()
    {
        if (_tapWave is null)
        {
            // sine，960 → 1420Hz，0.08s 滑上去，总长 0.12s
            _tapWave = BuildWave(false, 960, 1420, 0.08, 0.12);
            _tapPinned = GCHandle.Alloc(_tapWave, GCHandleType.Pinned);
        }

        return _tapWave;
    }

    private byte[] EnsurePopupWave()
    {
        if (_popupWave is null)
        {
            // triangle，720 → 1080Hz，0.15s 滑上去，总长 0.2s
            _popupWave = BuildWave(true, 720, 1080, 0.15, 0.2);
            _popupPinned = GCHandle.Alloc(_popupWave, GCHandleType.Pinned);
        }

        return _popupWave;
    }

    /// <summary>
    ///     合成一段 16bit 单声道 PCM。频率按指数从 startHz 滑到 endHz（参考稿的
    ///     exponentialRampToValueAtTime），音量同样指数衰减。
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

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);
}
