using System.Text;
using System.Xml;

namespace SlnTools;

public static class SlnMerger
{
    public static SolutionConfiguration Merge(string mainSlnPath, params string[] toMergePaths)
        => Merge(SlnParser.ParseConfiguration(mainSlnPath)
            , toMergePaths.Select(SlnParser.ParseConfiguration).ToArray());

    public static SolutionConfiguration Merge(SolutionConfiguration mainSln, params SolutionConfiguration[] toMerge)
    {
        if (toMerge.Length == 0)
            return mainSln;

        SolutionConfiguration result = mainSln.Clone();
        List<string> commonPathParts = Path.GetDirectoryName(mainSln.SlnFile)!.Split('\\', '/').ToList();

        foreach (Project project in result.Projects)
            LinkFilesAndDirs(project);

        foreach (SolutionConfiguration conf in toMerge)
            if (result.SlnVersionStr != conf.SlnVersionStr
                || (result.VsVersion ?? conf.VsVersion) != (conf.VsVersion ?? result.VsVersion))
                throw new NotImplementedException();
            else
            {
                if (result.VsMinimalVersion < conf.VsMinimalVersion)
                    result.VsMinimalVersionStr = conf.VsMinimalVersionStr;
                if (result.VsVersion > conf.VsVersion)
                    result.VsVersionStr = conf.VsVersionStr;
                List<string> partsResult = new();
                List<string> partsConf = Path.GetDirectoryName(conf.SlnFile)!.Split('\\', '/').ToList();
                int minLength = Math.Min(partsConf.Count, commonPathParts.Count);
                for (int i = 0; i < minLength; i++)
                    if (commonPathParts[i] == partsConf[i])
                        partsResult.Add(partsConf[i]);
                    else
                        break;
                commonPathParts = partsResult;
                foreach (Project project in conf.Projects)
                {
                    Project copy = project.Clone();

                    LinkFilesAndDirs(copy);

                    if (result.Projects.Any(p => StringComparer.OrdinalIgnoreCase.Equals(project.Name, p.Name)))
                    {
                        string newName = copy.Name + '_' + Path.GetFileNameWithoutExtension(conf.SlnFile);
                        copy.FilePath = copy.FilePath.Replace(copy.Name, newName);
                        copy.AbsoluteFilePath = copy.AbsoluteFilePath.Replace(copy.Name, newName);
                        copy.Name = newName;
                    }

                    result.Projects.Add(copy);
                }

                foreach (Section section in conf.Sections)
                {
                    Section? exists = result.Sections.SingleOrDefault(s =>
                        StringComparer.OrdinalIgnoreCase.Equals(s.Name, section.Name));
                    if (exists == null)
                    {
                        exists = section.Clone(false);
                        result.Sections.Add(exists);
                    }

                    bool isConfSection = section.Name switch
                    {
                        "SolutionConfigurationPlatforms" or "ProjectConfigurationPlatforms" => false,
                        "SolutionProperties" => true,
                        _ => throw new NotImplementedException()
                    };

                    foreach (string line in section.Lines)
                        if (isConfSection)
                        {
                            string[] lineParts = line.Split('=');
                            string key = lineParts[0];
                            string? existsLine = exists.Lines.SingleOrDefault(l => l.StartsWith(key));
                            if (existsLine != null)
                            {
                                string value = existsLine.Split('=')[1].Trim();
                                if (value != lineParts[1].Trim())
                                    throw new NotImplementedException();
                            }
                        }
                        else if (!exists.Lines.Contains(line))
                            exists.Lines.Add(line);
                }
            }

        result.SlnRecalculatedDirectory = string.Join("\\", commonPathParts);

        foreach (Project project in result.Projects)
        {
            if (project.PackageReferences.Count == 0)
                continue;
            XmlNode? refParent = project.ProjectReferences.FirstOrDefault();
            if (refParent == null)
            {
                refParent = project.ProjectFile!.CreateNode(XmlNodeType.Element, "ItemGroup", null);
                project.ProjectFile.DocumentElement!.AppendChild(refParent);
            }

            foreach (XmlNode package in project.PackageReferences)
            {
                string name = package.Attributes?[SlnHelpers.IncludeAttribute]?.Value.TrimStart('.', '/', '\\') ??
                              throw new NullReferenceException();
                Project? proj = result.Projects.SingleOrDefault(p => p.FilePath.EndsWith(name));
                if (proj == null)
                    continue;
                project.PackageReferences.Remove(package);
                package.ParentNode!.RemoveChild(package);
                XmlNode newNode =
                    project.ProjectFile!.CreateNode(XmlNodeType.Element, SlnHelpers.ProjectReference, null);
                refParent.AppendChild(newNode);
                XmlAttribute attr = project.ProjectFile.CreateAttribute(SlnHelpers.IncludeAttribute);
                attr.Value = proj.FilePath;
                newNode.Attributes!.Append(attr);
                attr = project.ProjectFile.CreateAttribute("PrivateAssets");
                attr.Value = "All";
                newNode.Attributes!.Append(attr);
            }
        }

        return result;
    }

    public static readonly List<string> ExcludedFolders = new() { "\\.idea", "\\bin", "\\obj" };

    private static void LinkFilesAndDirs(Project project)
    {
        if (project.ProjectFile == null)
            return;
        //<None Remove="DgfipInvoice\UBLInherits\UBLEn16931Invoice.cs.autogenerated" />
        HashSet<string> ignored = new(project.ProjectFile.RetrieveValues("None", "Remove"));
        foreach (string toCopy in GetToCopy(project.ProjectFile, "\\"))
            ignored.Add(toCopy);
        HashSet<string> embedded = new(project.ProjectFile.RetrieveValues("EmbeddedResource", "Include"));
        ignored.ExceptWith(embedded);
        ignored.Add(project.FilePath);
        string absoluteProjectPath = Path.GetDirectoryName(project.AbsoluteFilePath)!;
        LinkFiles(project.ProjectFile, absoluteProjectPath, absoluteProjectPath, ignored, embedded);
        foreach (string dir in Directory.EnumerateDirectories(Path.GetDirectoryName(project.AbsoluteFilePath)!))
            if (ExcludedFolders.All(f => !dir.EndsWith(f)))
                LinkFilesAndDirs(project.ProjectFile, absoluteProjectPath, dir, ignored, embedded);
    }

    private static void LinkFilesAndDirs(XmlDocument xmlDocument, string absoluteProjectPath, string dir,
        HashSet<string> ignored, HashSet<string> embedded)
    {
        LinkFiles(xmlDocument, absoluteProjectPath, dir, ignored, embedded);

        foreach (string child in Directory.EnumerateDirectories(dir))
            LinkFilesAndDirs(xmlDocument, absoluteProjectPath, child, ignored, embedded);
    }

    private static void LinkFiles(XmlDocument xmlDocument, string absoluteProjectPath, string dir,
        HashSet<string> ignored, HashSet<string> embedded)
    {
        XmlNode parent = xmlDocument.CreateNode(XmlNodeType.Element, "ItemGroup", null);
        bool parentAdded = false;
        foreach (string file in Directory.EnumerateFiles(dir))
            if (embedded.Any(e => file.EndsWith(e)))
            {
                List<XmlNode> nodes = xmlDocument.RetrieveNodes("EmbeddedResource").ToList();
                XmlNode node = nodes.Single(n =>
                    file.EndsWith(n.Attributes!["Include"]?.Value ?? Guid.NewGuid().ToString()));
                string value = node.Attributes!["Include"]!.Value;
                XmlNode link = xmlDocument.CreateNode(XmlNodeType.Element, "Link", null);
                link.InnerText = value;
                node.AppendChild(link);
                node.Attributes!["Include"]!.Value = file;
            }
            else if (ignored.All(i => !file.EndsWith(i)))
            {
                if (!parentAdded)
                {
                    parentAdded = true;
                    xmlDocument.DocumentElement!.AppendChild(parent);
                }

                /*
                    <Compile Include="..\..\A3CsUtilsCommon\A3_Common\Api\Excel\Excel.cs">
                        <Link>Api\Excel\Excel.cs</Link>
                    </Compile>
                 */
                string nodeName = Path.GetExtension(file) == ".cs" ? "Compile" : "Content";
                XmlNode compile = xmlDocument.CreateNode(XmlNodeType.Element, nodeName, null);
                XmlAttribute attr = xmlDocument.CreateAttribute("Include");
                attr.Value = file;
                compile.Attributes!.Append(attr);
                XmlNode link = xmlDocument.CreateNode(XmlNodeType.Element, "Link", null);
                link.InnerText = file.Replace(absoluteProjectPath, "").TrimStart('\\', '/');
                compile.AppendChild(link);
                parent.AppendChild(compile);
            }
    }


    public static void WriteTo(string slnFilePath, SolutionConfiguration conf)
    {
        string baseDirectoryPath = Path.GetDirectoryName(slnFilePath) ?? throw new NullReferenceException();
        if (!Directory.Exists(baseDirectoryPath))
            Directory.CreateDirectory(baseDirectoryPath);

        SlnParser.WriteConfiguration(conf, slnFilePath);

        XmlWriterSettings settings = new() { Indent = true, Encoding = Encoding.UTF8 };
        foreach (Project project in conf.Projects)
            if (project.ProjectFile != null)
            {
                string file = Path.Combine(baseDirectoryPath, project.FilePath.TrimStart('.', '\\', '/'));
                string directory = Path.GetDirectoryName(file)!;
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                using XmlWriter xmlWriter = XmlWriter.Create(file, settings);
                project.ProjectFile.WriteTo(xmlWriter);
                foreach (string toCopy in GetToCopy(project.ProjectFile, ""))
                    File.Copy(Path.Combine(project.AbsoluteOriginalDirectory, toCopy), Path.Combine(directory, toCopy), true);
            }
    }

    private static HashSet<string> GetToCopy(XmlDocument xmlDoc, string prefix)
    {
        HashSet<XmlNode> toCopyNodes = new(xmlDoc.RetrieveNodes("CopyToOutputDirectory"));
        HashSet<string> toCopy = new(toCopyNodes
            .Select(n => prefix + n.ParentNode?.Attributes?["Update"]?.Value).Where(n => n != prefix && n != "Never"));
        return toCopy;
    }
}