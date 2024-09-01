
namespace SlnTools;

public class MergeConfiguration
{
    public List<string>? Solutions { get; set; }

    public string? DestinationPath { get; set; }

    public bool CopySolutionFolderFiles { get; set; } = true;

    public FileReplacements? FileReplacements { get; set; }

    public List<string>? FolderToIgnore { get; set; }
}

public class FileReplacement
{
    public string? CsprojFilePath { get; set; }
    public string? ReplaceWithFilePath { get; set; }
}

public class FileReplacements : List<FileReplacement>;
