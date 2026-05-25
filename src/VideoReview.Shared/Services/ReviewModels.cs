namespace VideoReview.Shared.Services;

public enum ReviewDecision
{
    Unreviewed,
    Keep,
    Delete,
    Later
}

public enum FrameKind
{
    Key,
    Random
}

public enum ExtractionPreset
{
    Balanced,
    SensitiveScene,
    StrictScene,
    EvenCoverage,
    RandomReview
}

public sealed record FramePreview(string Path, string DataUri, FrameKind Kind);

public sealed class VideoReviewItem
{
    public required string OriginalPath { get; init; }
    public required string CurrentPath { get; set; }
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public TimeSpan? Duration { get; set; }
    public ReviewDecision Decision { get; set; }
    public List<FramePreview> Frames { get; } = [];
}

public sealed record ExtractionProgress(int Current, int Total, string Message);

public sealed record ReviewStats(int Total, int Pending, int Keep, int Delete, int Later);
