using System.Xml;

namespace SlnTools;

public static class SlnParser
{
    public static readonly Dictionary<string, Action<SolutionConfiguration, string>> HeaderLinesPrefix = new()
    {
        { "Microsoft Visual Studio Solution File, Format Version ", (s, v) => s.SlnVersionStr = v },
        { "# Visual Studio Version ", (s, v) => s.VsMajorVersionStr = v },
        { "VisualStudioVersion = ", (s, v) => s.VsVersionStr = v },
        { "MinimumVisualStudioVersion = ", (s, v) => s.VsMinimalVersionStr = v }
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
        foreach (string line in content.Skip(maxHeaderLine + 1))
            if (line.StartsWith("\tEnd") || line.StartsWith("End") || line == "")
                currentSection = null;
            else
            {
                string[] parts = line.Split('(');
                switch (parts[0].Trim())
                {
                    case nameof(Global): break;
                    case nameof(Project):
                        parts = parts[1].Split(_Splits, _SplitOptions);
                        Project project = new()
                        {
                            FilePath = parts[2],
                            AbsoluteFilePath = Path.Combine(result.SlnDirectory, parts[2]),
                            Name = parts[1],
                            ProjectGuid = parts[3],
                            ProjectTypeGuid = parts[0]
                        };
                        project.AbsoluteOriginalDirectory = Path.GetDirectoryName(project.AbsoluteFilePath)
                                                            ?? throw new NullReferenceException();
                        if (project.FilePath.EndsWith(SlnHelpers.CsProjExtension))
                        {
                            project.ProjectFile = new XmlDocument();
                            project.ProjectFile.Load(
                                Path.Combine(result.SlnDirectory, project.FilePath));
                            project.PackageReferences.AddRange(
                                project.ProjectFile.RetrieveNodes(SlnHelpers.PackageReference));
                            project.ProjectReferences.AddRange(
                                project.ProjectFile.RetrieveNodes(SlnHelpers.ProjectReference));
                        }

                        result.Projects.Add(project);
                        break;
                    case "GlobalSection":
                        parts = parts[1].Split(_Splits, _SplitOptions);

                        if (currentSection != null)
                            throw new Exception("Should not happen");
                        currentSection = new()
                        {
                            IsPreSolution = parts[1]switch
                            {
                                "preSolution" => true,
                                "postSolution" => false,
                                _ => throw new Exception("Should not happen")
                            },
                            Name = parts[0]
                        };
                        result.Sections.Add(currentSection);
                        break;
                    default:
                        if (currentSection == null || line != "\tEndGlobalSection" && !line.StartsWith("\t\t"))
                            throw new Exception("Should not happen");
                        currentSection.Lines.Add(line);
                        break;
                }
            }

        return result;
    }

    public static void WriteConfiguration(SolutionConfiguration sln, string pathToWrite)
    {
        List<string> content = new() { "" };
        List<string> keys = HeaderLinesPrefix.Keys.ToList();
        TryAdd(content, keys[0], sln.SlnVersionStr);
        TryAdd(content, keys[1], sln.VsMajorVersionStr);
        TryAdd(content, keys[2], sln.VsVersionStr);
        TryAdd(content, keys[3], sln.VsMinimalVersionStr);

        foreach (Project p in sln.Projects)
            content.Add($$"""
                          Project("{{{p.ProjectTypeGuid}}}") = "{{p.Name}}", "{{p.FilePath}}", "{{{p.ProjectGuid}}}"
                          EndProject
                          """);
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