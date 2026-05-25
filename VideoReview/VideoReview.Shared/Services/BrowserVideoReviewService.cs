namespace VideoReview.Shared.Services;

public sealed class BrowserVideoReviewService : IVideoReviewService
{
    public bool IsDesktopSupported => false;

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<VideoReviewItem>> ScanVideosAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<VideoReviewItem>>([]);
    }

    public Task ExtractFramesAsync(
        string workspaceFolder,
        IReadOnlyList<VideoReviewItem> videos,
        bool overwriteExisting,
        ExtractionPreset preset,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task LoadExistingFramesAsync(
        string workspaceFolder,
        IReadOnlyList<VideoReviewItem> videos,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<VideoReviewItem> ClassifyAsync(
        VideoReviewItem item,
        string workspaceFolder,
        ReviewDecision decision,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(item);
    }

    public Task OpenVideoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
