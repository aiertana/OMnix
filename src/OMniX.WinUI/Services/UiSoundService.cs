using System;
using System.IO;
using System.Runtime.InteropServices;
using OMniX.WinUI.Contracts.Services;

namespace OMniX.WinUI.Services;

/// <summary>
///     二开：UI 交互音效。
///     tap   优先用 Assets/Sounds/click.wav，没有就现场合成一段（sine 960→1420，0.12s）
///     popup 合成 triangle 720→1080，0.2s
///     简易版（MotionEnabled=false）自动静音，也可以在设置里单独关。
/// </summary>
public sealed class UiSoundService
{
    public const string SoundEnabledKey = "App.UiSound";

    // winmm 的 PlaySound 标志位
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;
    private const uint SndFilename = 0x20000;

    private const int SampleRate = 22050;

    // 参考稿是 0.18，实测偏轻（0.12 秒的一个短音），提到 0.28 才听得清
    private const double Amplitude = 0.28;
    private const double TailGain = 0.001;

    private readonly ILocalSettingsService _localSettingsService;

    private byte[]? _tapWave;
    private IntPtr _tapPointer;
    private GCHandle _tapPin;

    private byte[]? _popupWave;
    private IntPtr _popupPointer;
    private GCHandle _popupPin;

    private string? _tapFile;

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

    /// <summary>点按钮：短促的"嗒"</summary>
    public void PlayTap() => Play(false);

    /// <summary>切页面 / 开弹窗：带一点厚度的"咚"</summary>
    public void PlayPopup() => Play(true);

    private void Play(bool popup)
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

        try
        {
            // 优先放现成的 wav 文件：文件由 winmm 自己读盘，不涉及托管内存，
            // 异步播放期间不会被 GC 动到
            if (!popup && ResolveTapFile() is { } file)
            {
                PlaySoundFile(file, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault);
                return;
            }

            var pointer = popup ? PopupPointer() : TapPointer();
            if (pointer == IntPtr.Zero)
                return;

            // 传的是固定住的内存地址，不是 byte[] 本身 ——
            // 传数组的话 marshaler 会临时复制一份，SND_ASYNC 还没读完就被回收了，
            // 表现就是"大部分时候没声音，偶尔响一下"
            PlaySoundMemory(pointer, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
        }
        catch (Exception)
        {
            // 声音这种锦上添花的东西，怎么样都不该拖累操作
        }
    }

    private string? ResolveTapFile()
    {
        if (_tapFile is not null)
            return _tapFile.Length == 0 ? null : _tapFile;

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "click.wav");
        _tapFile = File.Exists(path) ? path : string.Empty;

        return _tapFile.Length == 0 ? null : _tapFile;
    }

    private IntPtr TapPointer()
    {
        if (_tapPointer != IntPtr.Zero)
            return _tapPointer;

        // sine，960 → 1420Hz，0.08s 滑上去，总长 0.12s
        _tapWave ??= BuildWave(false, 960, 1420, 0.08, 0.12);
        _tapPin = GCHandle.Alloc(_tapWave, GCHandleType.Pinned);
        _tapPointer = _tapPin.AddrOfPinnedObject();

        return _tapPointer;
    }

    private IntPtr PopupPointer()
    {
        if (_popupPointer != IntPtr.Zero)
            return _popupPointer;

        // triangle，720 → 1080Hz，0.15s 滑上去，总长 0.2s
        _popupWave ??= BuildWave(true, 720, 1080, 0.15, 0.2);
        _popupPin = GCHandle.Alloc(_popupWave, GCHandleType.Pinned);
        _popupPointer = _popupPin.AddrOfPinnedObject();

        return _popupPointer;
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

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySoundFile(string pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySoundMemory(IntPtr pszSound, IntPtr hmod, uint fdwSound);
}
