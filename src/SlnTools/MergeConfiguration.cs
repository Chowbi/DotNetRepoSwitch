using System.Diagnostics.CodeAnalysis;

namespace SlnTools;

public class MergeConfiguration
{
    public List<string>? Solutions { get; set; }

    public string? DestinationPath { get; set; }

    public bool CopySolutionFolderFiles { get; set; } = true;

    public bool GenerateEditorConfig { get; set; } = true;

    public FileReplacements? FileReplacements { get; set; }

    [MemberNotNull(nameof(Solutions), nameof(DestinationPath))]
    public void Check()
    {
        if (Solutions is null || Solutions.Count == 0)
            throw new Exception($"Nothing to merge, populate {nameof(Solutions)}.");
        if (string.IsNullOrWhiteSpace(DestinationPath))
            throw new Exception($"Nowhere to write, populate {nameof(DestinationPath)}.");
    }
}

public class FileReplacement
{
    public string? CsprojFilePath { get; set; }
    public string? ReplaceWithFilePath { get; set; }
}

public class FileReplacements : List<FileReplacement>;
