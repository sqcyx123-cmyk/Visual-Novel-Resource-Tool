namespace VisualNovelResourceTool.Core;

public enum ResourceKind { RenPyRpa, KirikiriXp3, RpgMaker, RpgMakerPak, Dpmx, EnigmaExecutable, TyranoScript, AlreadyUnpacked, Unknown }
public enum SupportLevel { Extractable, Recognized, NoExtractionNeeded, Unsupported, Error }

public sealed record ScanResult(
    string Path,
    string DisplayName,
    ResourceKind Kind,
    SupportLevel Support,
    string Format,
    string Description,
    long Size = 0,
    int ArchiveCount = 0);

public sealed record RpaEntry(string Name, long Offset, long Length, byte[] Prefix);
public sealed record Xp3Segment(bool Compressed, long Offset, long OriginalSize, long StoredSize);
public sealed record Xp3Entry(string Name, long Length, IReadOnlyList<Xp3Segment> Segments);
public sealed record DpmxEntry(string Name, long Offset, long Length);
public sealed record PakEntry(string Name, long Length);
public sealed record LooseEntry(string SourcePath, string Name, long Length);

public sealed record ExtractionProgress(int Completed, int Total, string CurrentFile, long BytesWritten);

public sealed record ExtractionSummary(int Extracted, int Skipped, long BytesWritten, string OutputDirectory, int Failed = 0, int Renamed = 0);
