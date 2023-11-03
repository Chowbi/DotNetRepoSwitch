using System.Xml;

namespace SlnTools;

public class SolutionConfiguration
{
    public string SlnFile { get; }
    public string SlnDirectory { get; }

    public string SlnRecalculatedDirectory { get; internal set; }

    public string? SlnVersionStr
    {
        get => _SlnVersionStr;
        set
        {
            _SlnVersionStr = value;
            if (!string.IsNullOrWhiteSpace(_SlnVersionStr))
                SlnVersion = Version.Parse(_SlnVersionStr);
        }
    }

    public string? VsMajorVersionStr { get; set; }

    public Version? SlnVersion { get; private set; }
    private string? _SlnVersionStr;

    public string? VsVersionStr
    {
        get => _VsVersionStr;
        set
        {
            _VsVersionStr = value;
            if (!string.IsNullOrWhiteSpace(_VsVersionStr))
                VsVersion = Version.Parse(_VsVersionStr);
        }
    }

    public Version? VsVersion { get; private set; }
    private string? _VsVersionStr;


    public string? VsMinimalVersionStr
    {
        get => _VsMinimalVersionStr;
        set
        {
            _VsMinimalVersionStr = value;
            if (!string.IsNullOrWhiteSpace(_VsMinimalVersionStr))
                VsMinimalVersion = Version.Parse(_VsMinimalVersionStr);
        }
    }

    public Version? VsMinimalVersion { get; private set; }
    private string? _VsMinimalVersionStr;
    public Projects Projects { get; } = new();
    public Sections Sections { get; } = new();

    public SolutionConfiguration(string slnFile)
    {
        SlnFile = slnFile;
        SlnDirectory = Path.GetDirectoryName(SlnFile) ?? throw new NullReferenceException();
        SlnRecalculatedDirectory = SlnDirectory;
    }
}

public class Project
{
    public string ProjectTypeGuid { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string FilePath { get; set; } = null!;

    public string AbsoluteFilePath { get; set; } = null!;

    public string AbsoluteOriginalDirectory { get; set; } = null!;
    public string ProjectGuid { get; set; } = null!;

    public XmlDocument? ProjectFile { get; set; }

    public List<XmlNode> ProjectReferences { get; } = new();
    public List<XmlNode> PackageReferences { get; } = new();
    public override string ToString() => $"{Name} ({FilePath})";
}

public class Projects : List<Project>
{
}

public class Global
{
}

public class Section
{
    public string Name { get; set; } = null!;
    public bool IsPreSolution { get; set; }

    public List<string> Lines { get; } = new();
}

public class Sections : List<Section>
{
}