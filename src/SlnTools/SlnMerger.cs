using System.Text;
using System.Xml;

namespace SlnTools;

using static SlnHelpers;

public static class SlnMerger
{
    public static SolutionConfiguration Merge(string mainSlnPath, params string[] toMergePaths)
        => Merge(SlnParser.ParseConfiguration(mainSlnPath)
            , toMergePaths.Select(SlnParser.ParseConfiguration).ToArray());

    public static SolutionConfiguration Merge(SolutionConfiguration mainSln, params SolutionConfiguration[] toMerge)
    {
        if (toMerge.Length == 0)
            return mainSln;

        Console.Write($"Merging {toMerge.Length + 1} sln files. ");
        SolutionConfiguration result = MergeSolutionAndLinkFiles(mainSln, toMerge);
        Console.WriteLine("Done");

        Console.Write("Replacing Packages by projects, adding dependant projects. ");
        foreach (Project project in result.Projects)
            if (project.ProjectFile != null)
            {
                XmlNode? refParent = project.ProjectReferences.RootNode;
                if (refParent == null)
                    refParent = project.ProjectFile.CreateNode(XmlNodeType.Element, ItemGroup, null);
                else
                    project.ProjectFile.DocumentElement!.RemoveChild(refParent);
                XmlNode rootNode = project.ProjectFile.RetrieveNodes("PropertyGroup").LastOrDefault()
                                   ?? project.PackageReferences.RootNode
                                   ?? throw new NullReferenceException();
                rootNode.ParentNode!.InsertAfter(refParent, rootNode);

                if (refParent.Name != ItemGroup)
                    throw new Exception($"refParent name should be {ItemGroup}");

                AddProjectReferences(result, project, project, refParent);
            }

        Console.WriteLine("Done");

        Console.Write("Cleaning packages. ");
        foreach (Project project in result.Projects)
        foreach (PackageReference package in project.PackageReferences.ToList())
            if (result.Projects.Any(p => p.OriginalName == package.Name))
                project.PackageReferences.Remove(package);
        Console.WriteLine("Done");

        return result;
    }

    private static SolutionConfiguration MergeSolutionAndLinkFiles(SolutionConfiguration mainSln
        , params SolutionConfiguration[] toMerge)
    {
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
                        "SolutionConfigurationPlatforms" or "ProjectConfigurationPlatforms"
                            or "NestedProjects" or "MonoDevelopProperties" or "ExtensibilityGlobals" => false,
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

        return result;
    }

    private static void AddProjectReferences(SolutionConfiguration result, Project rootProject,
        Project currentProject, XmlNode refParent)
    {
        foreach (PackageReference package in currentProject.PackageReferences)
            AddProjectReferences(result, rootProject, refParent, package);

        foreach (ProjectReference project in currentProject.ProjectReferences.ToList())
            AddProjectReferences(result, rootProject, refParent, project);
    }

    private static void AddProjectReferences(
        SolutionConfiguration result
        , Project rootProject
        , XmlNode refParent
        , IReference reference)
    {
        if (rootProject.ProjectFile == null)
            throw new Exception("Should not happen");

        Project? proj = result.Projects.SingleOrDefault(p => p.Name == reference.Name);
        if (proj == null)
            return;

        XmlNode newNode = rootProject.ProjectFile.CreateNode(XmlNodeType.Element, SlnHelpers.ProjectReference, null);
        XmlAttribute attr = rootProject.ProjectFile.CreateAttribute(IncludeAttribute);
        for (int i = 0; i < rootProject.FilePath.Count(c => c is '\\' or '/'); i++)
            attr.Value += "..\\";
        attr.Value += proj.FilePath;
        newNode.Attributes!.Append(attr);
        attr = rootProject.ProjectFile.CreateAttribute("PrivateAssets");
        attr.Value = "All";
        newNode.Attributes!.Append(attr);
        refParent.AppendChild(newNode);
        // Add need Parent to work properly
        if (!rootProject.ProjectReferences.Add(newNode))
            refParent.RemoveChild(newNode);
        AddProjectReferences(result, rootProject, proj, refParent);
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
        HashSet<string> embedded = new(project.ProjectFile.RetrieveValues("EmbeddedResource", IncludeAttribute));
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
                    file.EndsWith(n.Attributes![IncludeAttribute]?.Value ?? Guid.NewGuid().ToString()));
                string value = node.Attributes![IncludeAttribute]!.Value;
                XmlNode link = xmlDocument.CreateNode(XmlNodeType.Element, "Link", null);
                link.InnerText = value;
                node.AppendChild(link);
                node.Attributes![IncludeAttribute]!.Value = file;
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
                XmlAttribute attr = xmlDocument.CreateAttribute(IncludeAttribute);
                attr.Value = file;
                compile.Attributes!.Append(attr);
                XmlNode link = xmlDocument.CreateNode(XmlNodeType.Element, "Link", null);
                link.InnerText = file.Replace(absoluteProjectPath, "").TrimStart('\\', '/');
                compile.AppendChild(link);
                parent.AppendChild(compile);
            }
    }

    public static void WriteTo(string slnFilePath,
        SolutionConfiguration conf,
        bool copySlnFolderFiles,
        params (string fileToCopyName, string absolutePathToSource)[] fileToCopySource)
    {
        Dictionary<string, string> fileToCopySourceDic;
        try
        {
            fileToCopySourceDic = fileToCopySource.ToDictionary(c => c.fileToCopyName, c => c.absolutePathToSource);
        }
        catch (Exception e)
        {
            throw new Exception("fileToCopySource must not contains fileToCopyName duplicates.", e);
        }

        Console.WriteLine("Writing result sln file and projects. ");
        string baseDirectoryPath = Path.GetDirectoryName(slnFilePath) ?? throw new NullReferenceException();
        if (!Directory.Exists(baseDirectoryPath))
            Directory.CreateDirectory(baseDirectoryPath);

        SlnParser.WriteConfiguration(conf, slnFilePath);

        if (copySlnFolderFiles)
        {
            Console.WriteLine("Copying solution directory files");
            string originalRootName = Path.GetFileNameWithoutExtension(conf.SlnFile);
            string newRootName = Path.GetFileNameWithoutExtension(slnFilePath);
            foreach (string file in Directory.EnumerateFiles(conf.SlnDirectory))
                if (file != conf.SlnFile)
                {
                    string fileName = Path.GetFileName(file).Replace(originalRootName, newRootName);
                    Console.Write($"{fileName} ");
                    string dest = Path.Combine(baseDirectoryPath, fileName);
                    CopyWithBackup(file, dest, false);
                }

            Console.WriteLine();
        }

        XmlWriterSettings settings = new()
        {
            Indent = true, Encoding = Encoding.UTF8
        };
        Console.WriteLine("Merging projects");
        foreach (Project project in conf.Projects)
            if (project.ProjectFile != null)
            {
                string file = Path.Combine(baseDirectoryPath, project.FilePath.TrimStart('.', '\\', '/'));
                string projectDirectory = Path.GetDirectoryName(file)!;
                if (!Directory.Exists(projectDirectory))
                    Directory.CreateDirectory(projectDirectory);

                Console.Write(project.Name + ' ');

                string[] origPathParts = project.AbsoluteOriginalDirectory.Split(Path.DirectorySeparatorChar);
                string[] newPathParts = projectDirectory.Split(Path.DirectorySeparatorChar);
                string commonPath = "", relPath = "";
                for (int i = 0; i < origPathParts.Length; i++)
                    if (string.IsNullOrWhiteSpace(relPath) && origPathParts[i] == newPathParts[i])
                        commonPath += origPathParts[i] + Path.DirectorySeparatorChar;
                    else
                        relPath += string.IsNullOrWhiteSpace(origPathParts[i]) ? "" : ".." + Path.DirectorySeparatorChar;


                StringBuilder sb = new();
                using XmlWriter xmlWriter = XmlWriter.Create(sb, settings);
                project.ProjectFile.WriteTo(xmlWriter);
                xmlWriter.Flush();
                string xml = sb.ToString();
                string toWrite = xml.Replace(commonPath, relPath);
                File.WriteAllText(file, toWrite, Encoding.UTF8);
                foreach (string toCopy in GetToCopy(project.ProjectFile, ""))
                {
                    string destination = Path.Combine(projectDirectory, toCopy);
                    if (fileToCopySourceDic.TryGetValue(Path.GetFileName(destination), out string? source))
                        CopyWithBackup(source, destination, true);
                    else
                        CopyWithBackup(Path.Combine(project.AbsoluteOriginalDirectory, toCopy), destination, false);
                }
            }

        Console.WriteLine();
        Console.WriteLine("Done");
    }

    private static void CopyWithBackup(string origin, string destination, bool replaceDestination)
    {
        string destDir = Path.GetDirectoryName(destination) ?? throw new NullReferenceException($"{destination} has no root directory");
        if (!File.Exists(destination))
        {
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);
            File.Copy(origin, destination);
            return;
        }

        byte[] sourceContent = File.ReadAllBytes(origin);
        if (IsSameContent(sourceContent, destination))
            return;

        byte[] toCompareContent = replaceDestination ? File.ReadAllBytes(destination) : sourceContent;
        string fileName = Path.GetFileNameWithoutExtension(destination) + '*';
        bool copied = false;
        foreach (string exists in Directory.EnumerateFiles(destDir, fileName))
            if (exists != destination && IsSameContent(toCompareContent, exists))
            {
                copied = true;
                break;
            }

        if (replaceDestination)
        {
            if (!copied)
                File.Copy(destination, destination + $"_{DateTime.Now:yyyyMMdd_HHmmss}.back");
            File.Copy(origin, destination, true);
        }
        else if (!copied)
            File.Copy(origin, destination + $"_{DateTime.Now:yyyyMMdd_HHmmss}.source");
    }

    private static bool IsSameContent(byte[] original, string toTest)
    {
        byte[] toTestContent = File.ReadAllBytes(toTest);
        if (original.Length != toTestContent.Length)
            return false;
        for (int i = 0; i < original.Length; i++)
            if (original[i] != toTestContent[i])
                return false;
        return true;
    }

    private static IEnumerable<string> GetToCopy(XmlDocument xmlDoc, string prefix)
    {
        foreach (XmlNode node in xmlDoc.RetrieveNodes("CopyToOutputDirectory"))
        {
            string toCopy = prefix + node.ParentNode?.Attributes?["Update"]?.Value;
            if (toCopy != prefix && toCopy != "Never")
                yield return toCopy;
        }
    }
}