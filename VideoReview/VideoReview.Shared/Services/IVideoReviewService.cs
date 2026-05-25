namespace VideoReview.Shared.Services;

public interface IVideoReviewService
{
    bool IsDesktopSupported { get; }

    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoReviewItem>> ScanVideosAsync(string folderPath, CancellationToken cancellationToken = default);

    Task ExtractFramesAsync(
        string workspaceFolder,
        IReadOnlyList<VideoReviewItem> videos,
        bool overwriteExisting,
        ExtractionPreset preset,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task LoadExistingFramesAsync(
        string workspaceFolder,
        IReadOnlyList<VideoReviewItem> videos,
        CancellationToken cancellationToken = default);

    Task<VideoReviewItem> ClassifyAsync(
        VideoReviewItem item,
        string workspaceFolder,
        ReviewDecision decision,
        CancellationToken cancellationToken = default);

    Task OpenVideoAsync(string videoPath, CancellationToken cancellationToken = default);
}
