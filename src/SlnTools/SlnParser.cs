using System.Diagnostics;
using System.Xml;

namespace SlnTools;

public static class SlnParser
{
    public static readonly Dictionary<string, Action<SolutionConfiguration, string>> HeaderLinesPrefix = new()
    {
        { "Microsoft Visual Studio Solution File, Format Version ", (s, v) => s.SlnVersionStr = v }
        , { "# Visual Studio Version ", (s, v) => s.VsMajorVersionStr = v }, { "VisualStudioVersion = ", (s, v) => s.VsVersionStr = v }
        , { "MinimumVisualStudioVersion = ", (s, v) => s.VsMinimalVersionStr = v }
    };

    private static readonly char[] _Splits = { ')', '"', '{', '}', '=', ',' };

    private const StringSplitOptions _SplitOptions
        = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    public static SolutionConfiguration ParseConfiguration(string slnFilePath)
    {
        List<string> content = File.ReadAllLines(slnFilePath).ToList();
        SolutionConfiguration result = new(slnFilePath);
        int maxHeaderLine = 0;
        for (int i = 0; i < 10; i++)
            if (!string.IsNullOrWhiteSpace(content[i]))
            {
                bool set = false;
                string line = content[i];
                foreach (KeyValuePair<string, Action<SolutionConfiguration, string>> kvp in HeaderLinesPrefix)
                    if (line.StartsWith(kvp.Key))
                    {
                        kvp.Value(result, line[kvp.Key.Length..]);
                        maxHeaderLine = i;
                        set = true;
                        break;
                    }

                if (!set)
                    break;
            }

        Section? currentSection = null;
        Project? currentProject = null;
        foreach (string line in content.Skip(maxHeaderLine + 1))
            if (line.StartsWith("\tEndGlobalSection") || line.StartsWith("EndGlobal") || line == "")
                currentSection = null;
            else if (line.Contains("EndProject"))
                currentProject = null;
            else
            {
                string[] parts = line.Split('(');
                switch (parts[0].Trim())
                {
                    case "Global": break;
                    case nameof(Project):
                        parts = parts[1].Split(_Splits, _SplitOptions);
                        currentProject = new()
                        {
                            FilePath = parts[2].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
                            , AbsoluteFilePath = Path.Combine(result.SlnDirectory, parts[2])
                                .Replace('/', Path.DirectorySeparatorChar)
                                .Replace('\\', Path.DirectorySeparatorChar)
                            , Name = parts[1]
                            , OriginalName = parts[1]
                            , ProjectGuid = parts[3]
                            , ProjectTypeGuid = parts[0]
                        };
                        currentProject.AbsoluteOriginalDirectory =
                            Path.GetDirectoryName(currentProject.AbsoluteFilePath)
                            ?? throw new NullReferenceException();
                        if (currentProject.FilePath.EndsWith(SlnHelpers.CsProjExtension))
                        {
                            currentProject.ProjectFile = new XmlDocument();
                            currentProject.ProjectFile.Load(Path.Combine(result.SlnDirectory, currentProject.FilePath));
                            currentProject.PackageReferences.AddRange(currentProject.ProjectFile.RetrieveNodes(SlnHelpers.PackageReference));
                            currentProject.ProjectReferences.AddRange(currentProject.ProjectFile.RetrieveNodes(SlnHelpers.ProjectReference));
                        }

                        result.Projects.Add(currentProject);
                        break;
                    case "GlobalSection":
                        parts = parts[1].Split(_Splits, _SplitOptions);

                        if (currentSection != null)
                            throw new Exception("Should not happen");
                        currentSection = new()
                        {
                            IsPreSolution = parts[1]switch
                            {
                                "preSolution" => true, "postSolution" => false, _ => throw new Exception("Should not happen")
                            }
                            , Name = parts[0]
                        };
                        result.Sections.Add(currentSection);
                        break;
                    default:
                        if (currentProject != null)
                            currentProject.SlnLines.Add(line);
                        else if (currentSection != null)
                            currentSection.Lines.Add(line);
                        else
                            throw new Exception("Should not happen");
                        break;
                }
            }

        return result;
    }

    public static void WriteConfiguration(SolutionConfiguration sln, string pathToWrite)
    {
        if (!pathToWrite.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("You must give a sln file path to write to", nameof(pathToWrite));

        List<string> content = new() { "" };
        List<string> keys = HeaderLinesPrefix.Keys.ToList();
        TryAdd(content, keys[0], sln.SlnVersionStr);
        TryAdd(content, keys[1], sln.VsMajorVersionStr);
        TryAdd(content, keys[2], sln.VsVersionStr);
        TryAdd(content, keys[3], sln.VsMinimalVersionStr);

        foreach (Project p in sln.Projects)
        {
            content.Add(
                $$"""
                  Project("{{{p.ProjectTypeGuid}}}") = "{{p.Name}}", "{{p.FilePath}}", "{{{p.ProjectGuid}}}"
                  """);
            foreach (string slnLine in p.SlnLines)
                content.Add(slnLine);
            content.Add("EndProject");
        }

        content.Add("Global");
        foreach (Section s in sln.Sections)
        {
            content.Add($"\tGlobalSection({s.Name}) = {(s.IsPreSolution ? "preSolution" : "postSolution")}");
            foreach (string line in s.Lines)
                content.Add(line);
            content.Add("\tEndGlobalSection");
        }

        content.Add("EndGlobal");
        File.WriteAllLines(pathToWrite, content);
    }

    public static void TryAdd(List<string> content, string prefix, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        content.Add(prefix + value);
    }
}
