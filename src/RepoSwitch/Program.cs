// See https://aka.ms/new-console-template for more information

using SlnTools;

namespace RepoSwitch;

public class Program
{
    public static void Main(string[] args)
    {
        string file = args.FirstOrDefault() ?? "appsettings.json";
        string searched = Path.Combine(AppContext.BaseDirectory, file);

        if (!File.Exists(searched))
            throw new Exception($"Configuration file has not been found: {searched}.");

        string content = File.ReadAllText(searched);
        MergeConfiguration? mergeConf = System.Text.Json.JsonSerializer.Deserialize<MergeConfiguration>(content);

        if (mergeConf?.Solutions is null || mergeConf.Solutions.Count == 0)
            throw new Exception($"Nothing to merge, populate {nameof(MergeConfiguration.Solutions)}.");
        if (string.IsNullOrWhiteSpace(mergeConf.DestinationPath))
            throw new Exception($"Nowhere to write, populate {nameof(MergeConfiguration.DestinationPath)}.");
        if (mergeConf.FileReplacements is not null && mergeConf.FileReplacements.Any(r => string.IsNullOrWhiteSpace(r.CsprojFilePath)))
            throw new Exception(
                $"All {nameof(MergeConfiguration.FileReplacements)} must have a {nameof(FileReplacement.CsprojFilePath)} property."
                + $"If {nameof(FileReplacement.ReplaceWithFilePath)} is set, {nameof(FileReplacement.CsprojFilePath)} will be replaced,"
                + $"else it will be not be copied even if referenced in csproj.");

        SolutionConfiguration conf = SlnMerger.Merge(mergeConf.Solutions);

        SlnMerger.WriteTo(mergeConf.DestinationPath, conf, mergeConf.CopySolutionFolderFiles, mergeConf.FileReplacements);
    }
}
