using System.Diagnostics;
using System.Globalization;
using VideoReview.Shared.Services;

namespace VideoReview.Services;

public sealed class DesktopVideoReviewService : IVideoReviewService
{
    private const string Ffmpeg = "ffmpeg";
    private const string Ffprobe = "ffprobe";

    private static readonly string[] VideoExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".webm", ".flv", ".mpeg", ".mpg",
        ".m3u8", ".ts", ".m2ts", ".mts"
    ];

    private const string FrameCacheFolderName = "VideoReviewFrames";

    private static readonly string[] ReviewFolders = ["保留", "删除", "待定", FrameCacheFolderName];

    public bool IsDesktopSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS();

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
        };
        picker.FileTypeFilter.Add("*");

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "请选择目标文件夹中的任意一个视频文件"
        });

        return string.IsNullOrWhiteSpace(result?.FullPath) ? null : Path.GetDirectoryName(result.FullPath);
#endif
    }

    public Task<IReadOnlyList<VideoReviewItem>> ScanVideosAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            return Task.FromResult<IReadOnlyList<VideoReviewItem>>([]);
        }

        var reviewFolderPaths = ReviewFolders
            .Select(name => Path.GetFullPath(Path.Combine(folderPath, name)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var videos = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsInsideReviewFolder(path, reviewFolderPaths))
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new VideoReviewItem
                {
                    OriginalPath = path,
                    CurrentPath = path,
                    FileName = info.Name,
                    SizeBytes = info.Length
                };
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<VideoReviewItem>>(videos);
    }

    public async Task ExtractFramesAsync(
        string workspaceFolder,
        IReadOnlyList<VideoReviewItem> videos,
        bool overwriteExisting,
        ExtractionPreset preset,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var cacheRoot = Path.Combine(workspaceFolder, FrameCacheFolderName);
        Directory.CreateDirectory(cacheRoot);
        var ffmpegPath = ResolveToolPath(Ffmpeg);
        var ffprobePath = ResolveToolPath(Ffprobe);

        for (var i = 0; i < videos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var video = videos[i];
            progress?.Report(new ExtractionProgress(i + 1, videos.Count, $"抽帧中：{video.FileName}"));

            var videoCache = Path.Combine(cacheRoot, MakeSafeFolderName(video.CurrentPath));
            Directory.CreateDirectory(videoCache);

            if (!overwriteExisting && HasExistingFrames(videoCache))
            {
                PopulateFrames(video, videoCache);
                continue;
            }

            if (overwriteExisting)
            {
                DeleteExistingFrames(videoCache);
            }

            video.Duration = await ProbeDurationAsync(ffprobePath, video.CurrentPath, cancellationToken);
            await ExtractKeyFramesAsync(ffmpegPath, video.CurrentPath, video.Duration, videoCache, preset, cancellationToken);
            await ExtractRandomFramesAsync(ffmpegPath, video.CurrentPath, video.Duration, videoCache, preset, cancellationToken);

            PopulateFrames(video, videoCache);
        }

        progress?.Report(new ExtractionProgress(videos.Count, videos.Count, "抽帧完成"));
    }

    public Task LoadExistingFramesAsync(
        string workspaceFolder,
        IReadOnlyList<VideoReviewItem> videos,
        CancellationToken cancellationToken = default)
    {
        var cacheRoot = Path.Combine(workspaceFolder, FrameCacheFolderName);
        if (!Directory.Exists(cacheRoot))
        {
            return Task.CompletedTask;
        }

        foreach (var video in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var videoCache = Path.Combine(cacheRoot, MakeSafeFolderName(video.CurrentPath));
            if (Directory.Exists(videoCache))
            {
                PopulateFrames(video, videoCache);
            }
        }

        return Task.CompletedTask;
    }

    public Task<VideoReviewItem> ClassifyAsync(
        VideoReviewItem item,
        string workspaceFolder,
        ReviewDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var folderName = decision switch
        {
            ReviewDecision.Keep => "保留",
            ReviewDecision.Delete => "删除",
            ReviewDecision.Later => "待定",
            _ => throw new InvalidOperationException("请选择有效分类。")
        };

        var targetFolder = Path.Combine(workspaceFolder, folderName);
        Directory.CreateDirectory(targetFolder);

        var targetPath = GetAvailablePath(Path.Combine(targetFolder, Path.GetFileName(item.CurrentPath)));
        if (!Path.GetFullPath(item.CurrentPath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(item.CurrentPath, targetPath);
        }

        item.CurrentPath = targetPath;
        item.Decision = decision;
        return Task.FromResult(item);
    }

    public Task OpenVideoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(videoPath) { UseShellExecute = true }
            : new ProcessStartInfo("open", Quote(videoPath)) { UseShellExecute = false };

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private static bool IsInsideReviewFolder(string path, HashSet<string> reviewFolderPaths)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (reviewFolderPaths.Contains(directory))
            {
                return true;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return false;
    }

    private static bool HasExistingFrames(string videoCache)
    {
        return Directory.Exists(videoCache) && Directory.EnumerateFiles(videoCache, "*.jpg").Any();
    }

    private static void DeleteExistingFrames(string videoCache)
    {
        foreach (var framePath in Directory.EnumerateFiles(videoCache, "*.jpg"))
        {
            File.Delete(framePath);
        }
    }

    private static void PopulateFrames(VideoReviewItem video, string videoCache)
    {
        video.Frames.Clear();
        foreach (var framePath in Directory.EnumerateFiles(videoCache, "*.jpg").OrderBy(path => path))
        {
            var fileName = Path.GetFileName(framePath);
            var kind = fileName.StartsWith("key-", StringComparison.OrdinalIgnoreCase) ? FrameKind.Key : FrameKind.Random;
            video.Frames.Add(new FramePreview(framePath, ToDataUri(framePath), kind));
        }
    }

    private static async Task<TimeSpan?> ProbeDurationAsync(string ffprobePath, string videoPath, CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync(ffprobePath,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Quote(videoPath)}",
            cancellationToken);

        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static async Task ExtractKeyFramesAsync(
        string ffmpegPath,
        string videoPath,
        TimeSpan? duration,
        string cacheFolder,
        ExtractionPreset preset,
        CancellationToken cancellationToken)
    {
        var options = GetPresetOptions(preset);
        foreach (var existingKeyFrame in Directory.EnumerateFiles(cacheFolder, "key-*.jpg"))
        {
            File.Delete(existingKeyFrame);
        }

        var outputPattern = Path.Combine(cacheFolder, "key-scene-%02d.jpg");
        if (options.SceneThreshold is not null)
        {
            var threshold = options.SceneThreshold.Value.ToString("0.##", CultureInfo.InvariantCulture);
            var arguments = $"-hide_banner -loglevel error -y -i {Quote(videoPath)} -vf \"select='gt(scene\\,{threshold})',scale=960:-2\" -vsync vfr -frames:v {options.KeyFrameCount} {Quote(outputPattern)}";
            try
            {
                await RunProcessAsync(ffmpegPath, arguments, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Some videos have no frames above a strict scene threshold. Fall back to spread sampling.
            }

            if (Directory.EnumerateFiles(cacheFolder, "key-*.jpg").Count() >= options.MinimumSceneFrames)
            {
                return;
            }

            foreach (var weakSceneFrame in Directory.EnumerateFiles(cacheFolder, "key-*.jpg"))
            {
                File.Delete(weakSceneFrame);
            }
        }

        var totalSeconds = Math.Max(duration?.TotalSeconds ?? 60, 8);
        var fallbackSeconds = Enumerable.Range(1, options.KeyFrameCount)
            .Select(index => index / (double)(options.KeyFrameCount + 1))
            .Select(ratio => Math.Clamp(totalSeconds * ratio, 1, Math.Max(totalSeconds - 1, 1)))
            .ToArray();

        for (var i = 0; i < fallbackSeconds.Length; i++)
        {
            var outputPath = Path.Combine(cacheFolder, $"key-spread-{i + 1:00}.jpg");
            var seek = fallbackSeconds[i].ToString("0.###", CultureInfo.InvariantCulture);
            var fallbackArguments = $"-hide_banner -loglevel error -y -ss {seek} -i {Quote(videoPath)} -frames:v 1 -vf \"scale=960:-2\" {Quote(outputPath)}";
            await RunProcessAsync(ffmpegPath, fallbackArguments, cancellationToken);
        }
    }

    private static async Task ExtractRandomFramesAsync(
        string ffmpegPath,
        string videoPath,
        TimeSpan? duration,
        string cacheFolder,
        ExtractionPreset preset,
        CancellationToken cancellationToken)
    {
        var options = GetPresetOptions(preset);
        var totalSeconds = Math.Max(duration?.TotalSeconds ?? 60, 8);
        var random = new Random(unchecked(Environment.TickCount * 31 + Guid.NewGuid().GetHashCode()));
        var batchId = $"{DateTime.Now:yyyyMMddHHmmss}-{preset}";
        var seconds = Enumerable.Range(0, options.RandomFrameCount)
            .Select(index => options.UseJitteredCoverage
                ? GetJitteredSecond(index, options.RandomFrameCount, totalSeconds, random)
                : random.NextDouble() * Math.Max(totalSeconds - 2, 1) + 1)
            .OrderBy(value => value)
            .ToArray();

        for (var i = 0; i < seconds.Length; i++)
        {
            var outputPath = Path.Combine(cacheFolder, $"random-{batchId}-{i + 1:00}.jpg");
            var seek = seconds[i].ToString("0.###", CultureInfo.InvariantCulture);
            var arguments = $"-hide_banner -loglevel error -y -ss {seek} -i {Quote(videoPath)} -frames:v 1 -vf \"scale=960:-2\" {Quote(outputPath)}";
            await RunProcessAsync(ffmpegPath, arguments, cancellationToken);
        }
    }

    private static ExtractionPresetOptions GetPresetOptions(ExtractionPreset preset) => preset switch
    {
        ExtractionPreset.SensitiveScene => new ExtractionPresetOptions(0.20, 5, 4, 3, true),
        ExtractionPreset.StrictScene => new ExtractionPresetOptions(0.45, 4, 4, 2, true),
        ExtractionPreset.EvenCoverage => new ExtractionPresetOptions(null, 5, 4, 0, true),
        ExtractionPreset.RandomReview => new ExtractionPresetOptions(0.32, 3, 6, 2, false),
        _ => new ExtractionPresetOptions(0.32, 5, 4, 3, true)
    };

    private static double GetJitteredSecond(int index, int count, double totalSeconds, Random random)
    {
        var segment = Math.Max((totalSeconds - 2) / count, 1);
        var start = 1 + segment * index;
        var jitter = random.NextDouble() * segment * 0.65;
        return Math.Clamp(start + jitter, 1, Math.Max(totalSeconds - 1, 1));
    }

    private sealed record ExtractionPresetOptions(
        double? SceneThreshold,
        int KeyFrameCount,
        int RandomFrameCount,
        int MinimumSceneFrames,
        bool UseJitteredCoverage);

    private static string ResolveToolPath(string toolName)
    {
        var executableName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, executableName),
            Path.Combine(baseDirectory, "ffmpeg", executableName),
            Path.Combine(baseDirectory, "ffmpeg", "bin", executableName),
            Path.Combine(baseDirectory, "Tools", "ffmpeg", executableName),
            Path.Combine(baseDirectory, "Tools", "ffmpeg", "bin", executableName),
            Path.Combine(Directory.GetCurrentDirectory(), executableName),
            Path.Combine(Directory.GetCurrentDirectory(), "Tools", "ffmpeg", "bin", executableName)
        };

        var localTool = candidates.FirstOrDefault(File.Exists);
        if (localTool is not null)
        {
            return localTool;
        }

        if (IsCommandAvailable(executableName))
        {
            return executableName;
        }

        throw new FileNotFoundException(
            $"找不到 {executableName}。请把 ffmpeg/ffprobe 放到应用目录，或放到项目的 Tools/ffmpeg/bin 目录，或安装到系统 PATH。");
    }

    private static bool IsCommandAvailable(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        return pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.Combine(path, executableName))
            .Any(File.Exists);
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} 执行失败：{stderr.Trim()}");
        }

        return stdout;
    }

    private static string MakeSafeFolderName(string path)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..12];
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        return $"{name}-{hash}";
    }

    private static string GetAvailablePath(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var folder = Path.GetDirectoryName(targetPath)!;
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);

        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string ToDataUri(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
