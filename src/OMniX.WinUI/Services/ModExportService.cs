using System.Diagnostics;
using System.IO.Compression;

namespace OMniX.WinUI.Services;

public enum ModExportFormat
{
    /// <summary>打包成一个 zip</summary>
    Zip,

    /// <summary>按「角色 / Mod」结构复制成文件夹</summary>
    Folder
}

public sealed class ModExportRequest
{
    public string CharacterName { get; init; } = string.Empty;
    public string ModName { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
}

public sealed class ModExportProgressInfo
{
    public int FilesDone { get; init; }
    public int FilesTotal { get; init; }
    public long TotalBytes { get; init; }
    public string CurrentText { get; init; } = string.Empty;

    public double Percent => FilesTotal <= 0 ? 0 : Math.Min(100, FilesDone * 100.0 / FilesTotal);

    public string SizeText => TotalBytes >= 1024L * 1024 * 1024
        ? $"{TotalBytes / 1024.0 / 1024 / 1024:F1} GB"
        : $"{TotalBytes / 1024.0 / 1024:F0} MB";
}

public sealed class ModExportResult
{
    public bool Cancelled { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public int ModCount { get; init; }
    public int FileCount { get; init; }
    public int SkippedFiles { get; init; }
    public long TotalBytes { get; init; }

    public string SizeText => TotalBytes >= 1024L * 1024 * 1024
        ? $"{TotalBytes / 1024.0 / 1024 / 1024:F2} GB"
        : $"{TotalBytes / 1024.0 / 1024:F1} MB";

    public string SummaryText => Cancelled
        ? $"已取消，已处理 {FileCount} 个文件。"
        : $"{ModCount} 个 Mod、{FileCount} 个文件（{SizeText}）" +
          (SkippedFiles > 0 ? $"，{SkippedFiles} 个文件被跳过" : string.Empty);
}

/// <summary>
///     二开：导出 / 备份的执行体。
///     支持 zip 与文件夹两种形态，支持暂停 / 取消，进度按文件算（比按 Mod 平滑得多）。
///     暂停靠 ManualResetEventSlim 卡住后台线程，取消走 CancellationToken，取消时会清理半成品。
/// </summary>
public sealed class ModExportJob
{
    /// <summary>默认导出目录：管理器 exe 同级的 OMniX Exports</summary>
    public static string DefaultExportDirectory => Path.Combine(AppContext.BaseDirectory, "OMniX Exports");

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _gate = new(true);

    public bool IsPaused => !_gate.IsSet;

    public bool IsCancelled => _cts.IsCancellationRequested;

    public void Pause()
    {
        _gate.Reset();
    }

    public void Resume()
    {
        _gate.Set();
    }

    public void Cancel()
    {
        _cts.Cancel();
        _gate.Set(); // 放行，让在 Wait 的线程抛出去
    }

    public Task<ModExportResult> RunAsync(
        IReadOnlyList<ModExportRequest> mods,
        ModExportFormat format,
        string targetDirectory,
        string outputName,
        bool removeOmniXSettings,
        IProgress<ModExportProgressInfo>? progress = null)
    {
        return Task.Run(() => Run(mods, format, targetDirectory, outputName, removeOmniXSettings, progress));
    }

    private ModExportResult Run(
        IReadOnlyList<ModExportRequest> mods,
        ModExportFormat format,
        string targetDirectory,
        string outputName,
        bool removeOmniXSettings,
        IProgress<ModExportProgressInfo>? progress)
    {
        var token = _cts.Token;

        if (mods.Count == 0)
            throw new ArgumentException("没有选中任何 Mod", nameof(mods));

        targetDirectory = string.IsNullOrWhiteSpace(targetDirectory) ? DefaultExportDirectory : targetDirectory;
        Directory.CreateDirectory(targetDirectory);

        var safeName = Sanitize(string.IsNullOrWhiteSpace(outputName) ? DefaultName() : outputName);

        // ① 先扫一遍，统计文件总量（进度条才有分母）
        progress?.Report(new ModExportProgressInfo { CurrentText = "正在统计文件…" });

        var plan = new List<PlannedFile>();
        var usedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long plannedBytes = 0;

        foreach (var mod in mods)
        {
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(mod.FolderPath) || !Directory.Exists(mod.FolderPath))
                continue;

            var baseEntry = Sanitize(mod.CharacterName) + "/" + Sanitize(mod.ModName);

            var entry = baseEntry;
            var suffix = 1;
            while (!usedEntries.Add(entry))
                entry = baseEntry + "_" + suffix++;

            try
            {
                foreach (var file in Directory.EnumerateFiles(mod.FolderPath, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();

                    var relative = Path.GetRelativePath(mod.FolderPath, file);

                    // OMniX 自己的元数据（.OMniX_ 前缀）对别人没用
                    if (removeOmniXSettings &&
                        relative.StartsWith(".OMniX_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    plan.Add(new PlannedFile(entry + "/" + relative.Replace('\\', '/'), file));

                    try
                    {
                        plannedBytes += new FileInfo(file).Length;
                    }
                    catch (Exception)
                    {
                        // 拿不到大小就算了，只影响体积预估
                    }
                }
            }
            catch (Exception)
            {
                // 单个 Mod 目录读不了就跳过
            }
        }

        if (plan.Count == 0)
            return new ModExportResult
            {
                OutputPath = targetDirectory,
                ModCount = mods.Count
            };

        // 让用户先看到这次要写多少东西（Mod 动辄几十 GB，早知道早决定）
        progress?.Report(new ModExportProgressInfo
        {
            FilesTotal = plan.Count,
            TotalBytes = plannedBytes,
            CurrentText = $"共 {plan.Count} 个文件，约 " +
                          (plannedBytes >= 1024L * 1024 * 1024
                              ? $"{plannedBytes / 1024.0 / 1024 / 1024:F1} GB"
                              : $"{plannedBytes / 1024.0 / 1024:F0} MB")
        });

        var filesDone = 0;
        var skipped = 0;
        long totalBytes = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = 0L;

        void Report(string current)
        {
            // 每 80ms 最多刷一次，避免几千个文件把 UI 线程淹了
            if (stopwatch.ElapsedMilliseconds - lastReport < 80 && filesDone < plan.Count)
                return;

            lastReport = stopwatch.ElapsedMilliseconds;
            progress?.Report(new ModExportProgressInfo
            {
                FilesDone = filesDone,
                FilesTotal = plan.Count,
                CurrentText = current
            });
        }

        string outputPath;
        string workRoot;

        if (format == ModExportFormat.Zip)
        {
            outputPath = Path.Combine(targetDirectory, safeName + ".zip");
            workRoot = outputPath;
        }
        else
        {
            outputPath = Path.Combine(targetDirectory, safeName);
            workRoot = outputPath;
        }

        try
        {
            if (format == ModExportFormat.Zip)
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

                foreach (var item in plan)
                {
                    _gate.Wait(token);
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var source = File.OpenRead(item.SourcePath);

                        // Mod 里全是 dds / 音频这种已经压过的二进制，二次压缩收益几乎为零，
                        // 反而让「导出备份」卡成龟速，所以这里只打包不压缩（store 模式）
                        var entry = archive.CreateEntry(item.EntryPath, CompressionLevel.NoCompression);
                        using var destination = entry.Open();
                        source.CopyTo(destination);

                        totalBytes += source.Length;
                        filesDone++;
                    }
                    catch (Exception)
                    {
                        skipped++;
                    }

                    Report(Path.GetFileName(item.SourcePath));
                }
            }
            else
            {
                if (Directory.Exists(outputPath))
                    Directory.Delete(outputPath, true);

                Directory.CreateDirectory(outputPath);

                foreach (var item in plan)
                {
                    _gate.Wait(token);
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var destination = Path.Combine(outputPath,
                            item.EntryPath.Replace('/', Path.DirectorySeparatorChar));

                        var folder = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(folder))
                            Directory.CreateDirectory(folder);

                        File.Copy(item.SourcePath, destination, true);

                        totalBytes += new FileInfo(destination).Length;
                        filesDone++;
                    }
                    catch (Exception)
                    {
                        skipped++;
                    }

                    Report(Path.GetFileName(item.SourcePath));
                }
            }

            progress?.Report(new ModExportProgressInfo
            {
                FilesDone = filesDone,
                FilesTotal = plan.Count,
                CurrentText = "完成"
            });

            return new ModExportResult
            {
                OutputPath = outputPath,
                ModCount = mods.Count,
                FileCount = filesDone,
                SkippedFiles = skipped,
                TotalBytes = totalBytes
            };
        }
        catch (OperationCanceledException)
        {
            Cleanup(workRoot, format);

            return new ModExportResult
            {
                Cancelled = true,
                OutputPath = targetDirectory,
                ModCount = mods.Count,
                FileCount = filesDone,
                SkippedFiles = skipped,
                TotalBytes = totalBytes
            };
        }
    }

    private static void Cleanup(string path, ModExportFormat format)
    {
        try
        {
            if (format == ModExportFormat.Zip)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception)
        {
            // 半成品删不掉就留着
        }
    }

    public static string DefaultName() => "OMniX_Mods_" + DateTime.Now.ToString("yyyyMMdd_HHmm");

    /// <summary>在资源管理器里定位到导出的文件 / 文件夹</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开就算了
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "未命名";

        var cleaned = new string(name.Select(c => InvalidChars.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "未命名" : cleaned;
    }

    private sealed record PlannedFile(string EntryPath, string SourcePath);
}
