using System.Text;
using System.Xml;

namespace SlnTools;

using static SlnHelpers;

public static class SlnMerger
{
    public static SolutionConfiguration Merge(IEnumerable<string> toMergePaths, IEnumerable<string>? toIgnore)
        => Merge(toMergePaths.Select(SlnParser.ParseConfiguration), toIgnore);

    public static SolutionConfiguration Merge(IEnumerable<SolutionConfiguration> toMerge, IEnumerable<string>? toIgnore)
    {
        List<SolutionConfiguration> toMergeList = toMerge.ToList();
        int count = toMergeList.Count;
        switch (count)
        {
            case 0:
                throw new Exception("At least one Solution is needed");
            case 1:
                return toMergeList.First();
        }

        HashSet<string> ignored = toIgnore is null
            ? new HashSet<string>()
            : new HashSet<string>(toIgnore.Select(i => i.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));

        Console.Write($"Merging {count} sln files. ");
        SolutionConfiguration result = MergeSolutionAndLinkFiles(toMergeList, ignored);
        Console.WriteLine("Done");

        Console.Write("Replacing Packages by projects, adding dependant projects. ");
        foreach (Project project in result.Projects)
            if (project.ProjectFileMerge != null)
            {
                XmlNode? refParent = project.ProjectReferences.RootNode;
                if (refParent == null)
                    refParent = project.ProjectFileMerge.CreateNode(XmlNodeType.Element, ItemGroup, null);
                else
                    project.ProjectFileMerge.DocumentElement!.RemoveChild(refParent);
                XmlNode rootNode = project.ProjectFileMerge.RetrieveNodes("PropertyGroup").LastOrDefault()
                                   ?? project.PackageReferences.RootNode ?? throw new NullReferenceException();
                rootNode.ParentNode!.InsertAfter(refParent, rootNode);

                if (refParent.Name != ItemGroup)
                    throw new Exception($"refParent name should be {ItemGroup}");

                AddProjectReferences(result, project, project, refParent, ignored);
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

    private static SolutionConfiguration MergeSolutionAndLinkFiles(List<SolutionConfiguration> toMerge, HashSet<string> ignored)
    {
        SolutionConfiguration result = toMerge.First().Clone();
        List<string> commonPathParts = Path.GetDirectoryName(result.SlnFile)!.Split('\\', '/').ToList();

        foreach (Project project in result.Projects)
            if (project.ProjectFileOriginal is not null)
            {
                CloneProjectFile(project, ignored);
                // LinkFilesAndDirs(project, ignored);
            }

        foreach (SolutionConfiguration conf in toMerge.Skip(1))
            if (result.SlnVersionStr != conf.SlnVersionStr || (result.VsVersion ?? conf.VsVersion) != (conf.VsVersion ?? result.VsVersion))
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
                    result.Projects.Add(copy);
                    if (result.Projects.Any(p => StringComparer.OrdinalIgnoreCase.Equals(project.Name, p.Name)))
                    {
                        string newName = copy.Name + '_' + Path.GetFileNameWithoutExtension(conf.SlnFile);
                        copy.FilePath = copy.FilePath.Replace(copy.Name, newName);
                        copy.AbsoluteFilePath = copy.AbsoluteFilePath.Replace(copy.Name, newName);
                        copy.Name = newName;
                    }

                    CloneProjectFile(copy, ignored);
                    // LinkFilesAndDirs(copy, ignored);
                }

                foreach (Section section in conf.Sections)
                {
                    Section? exists = result.Sections.SingleOrDefault(s => StringComparer.OrdinalIgnoreCase.Equals(s.Name, section.Name));
                    if (exists == null)
                    {
                        exists = section.Clone(false);
                        result.Sections.Add(exists);
                    }

                    bool isConfSection = section.Name switch
                    {
                        "SolutionConfigurationPlatforms"
                            or "ProjectConfigurationPlatforms"
                            or "NestedProjects"
                            or "MonoDevelopProperties"
                            or "ExtensibilityGlobals" => false
                        , "SolutionProperties" => true, _ => throw new NotImplementedException()
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

        result.SlnRecalculatedDirectory = string.Join(Path.DirectorySeparatorChar, commonPathParts);

        return result;
    }

    private static void CloneProjectFile(Project project, HashSet<string> ignored)
    {
        if (project.ProjectFileOriginal == null)
            throw new ArgumentNullException(nameof(project.ProjectFileOriginal));
        project.ProjectFileMerge = (XmlDocument)project.ProjectFileOriginal.Clone();

        foreach (XmlNode node in project.ProjectFileMerge.ChildNodes)
            CloneProjectFile(node, ignored, project.AbsoluteOriginalDirectory);
    }

    private static void CloneProjectFile(XmlNode node, HashSet<string> ignored, string absoluteProjectPath)
    {
        if (node.Name == SlnHelpers.PackageReference)
            return;

        CheckFileAttribute(node, ignored, absoluteProjectPath, IncludeAttribute);
        CheckFileAttribute(node, ignored, absoluteProjectPath, SlnHelpers.UpdateAttribute);
        CheckFileAttribute(node, ignored, absoluteProjectPath, RemoveAttribute);

        foreach (XmlNode child in node.ChildNodes)
            CloneProjectFile(child, ignored, absoluteProjectPath);
    }

    private static void CheckFileAttribute(XmlNode node, HashSet<string> ignored, string absoluteProjectPath, string attrName)
    {
        string? file = node.Attributes?[attrName]?.Value;
        if (!string.IsNullOrWhiteSpace(file))
        {
            if (node.Attributes!.Count != 1)
                throw new NotImplementedException();
            string fullPath = absoluteProjectPath + Path.DirectorySeparatorChar + file.TrimStart('/', '\\', '.', ' ');
            if (!string.IsNullOrWhiteSpace(node.Attributes![ReplaceWithAttribute]?.Value))
                throw new Exception("Both Include and Update attributes are not expected on the same node");

            XmlAttribute attr = node.OwnerDocument!.CreateAttribute(Original);
            attr.Value = file;
            node.Attributes.Append(attr);

            attr = node.OwnerDocument!.CreateAttribute(ReplaceWithAttribute);
            attr.Value = fullPath;
            node.Attributes.Append(attr);
            ignored.Add(file);
        }
    }

    private static void LinkFilesAndDirs(Project project, HashSet<string> toIgnore)
    {
        if (project.ProjectFileOriginal == null)
            throw new ArgumentNullException(nameof(project.ProjectFileOriginal));
        toIgnore.Add(project.FilePath);
        LinkFiles(project.ProjectFileOriginal, project.AbsoluteOriginalDirectory, project.AbsoluteOriginalDirectory, toIgnore);
        foreach (string dir in Directory.EnumerateDirectories(Path.GetDirectoryName(project.AbsoluteFilePath)!))
            if (toIgnore.All(f => !dir.EndsWith(f)))
                LinkFilesAndDirs(project.ProjectFileOriginal, project.AbsoluteOriginalDirectory, dir, toIgnore);
    }

    private static void LinkFilesAndDirs(
        XmlDocument xmlDocument
        , string absoluteProjectPath
        , string dir
        , HashSet<string> ignored)
    {
        LinkFiles(xmlDocument, absoluteProjectPath, dir, ignored);

        foreach (string child in Directory.EnumerateDirectories(dir))
            LinkFilesAndDirs(xmlDocument, absoluteProjectPath, child, ignored);
    }

    private static void LinkFiles(XmlDocument xmlDocument, string absoluteProjectPath, string dir, HashSet<string> ignored)
    {
        XmlNode parent = xmlDocument.CreateNode(XmlNodeType.Element, "ItemGroup", null);
        bool parentAdded = false;
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs"))
            if (ignored.All(i => !file.EndsWith(i)))
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
                XmlNode compile = xmlDocument.CreateNode(XmlNodeType.Element, "Compile", null);
                XmlAttribute attr = xmlDocument.CreateAttribute(IncludeAttribute);
                attr.Value = file;
                compile.Attributes!.Append(attr);
                XmlNode link = xmlDocument.CreateNode(XmlNodeType.Element, "Link", null);
                link.InnerText = file.Replace(absoluteProjectPath, "").TrimStart('\\', '/');
                compile.AppendChild(link);
                parent.AppendChild(compile);
            }
    }


    private static void AddProjectReferences(
        SolutionConfiguration result
        , Project rootProject
        , Project currentProject
        , XmlNode refParent
        , HashSet<string>? toIgnore)
    {
        foreach (PackageReference package in currentProject.PackageReferences)
            AddProjectReferences(result, rootProject, refParent, package, toIgnore);

        foreach (ProjectReference project in currentProject.ProjectReferences.ToList())
            AddProjectReferences(result, rootProject, refParent, project, toIgnore);
    }

    private static void AddProjectReferences(
        SolutionConfiguration result
        , Project rootProject
        , XmlNode refParent
        , IReference reference
        , HashSet<string>? toIgnore)
    {
        if (rootProject.ProjectFileOriginal == null)
            throw new Exception("Should not happen");

        Project? proj = result.Projects.SingleOrDefault(p => p.Name == reference.Name);
        if (proj == null)
            return;

        XmlNode newNode = rootProject.ProjectFileOriginal.CreateNode(XmlNodeType.Element, SlnHelpers.ProjectReference, null);
        XmlAttribute attr = rootProject.ProjectFileOriginal.CreateAttribute(IncludeAttribute);
        for (int i = 0; i < rootProject.FilePath.Count(c => c is '\\' or '/'); i++)
            attr.Value += $"..{Path.DirectorySeparatorChar}";
        attr.Value += proj.FilePath;
        newNode.Attributes!.Append(attr);
        refParent.AppendChild(newNode);
        // Add need Parent to work properly
        if (!rootProject.ProjectReferences.Add(newNode))
            refParent.RemoveChild(newNode);
        AddProjectReferences(result, rootProject, proj, refParent, toIgnore);
    }


    public static void WriteTo(
        string slnFilePath
        , SolutionConfiguration conf
        , bool copySlnFolderFiles
        , List<FileReplacement>? fileToCopySource)
    {
        Dictionary<string, string?> fileToCopySourceDic = fileToCopySource?.ToDictionary(
                                                              c => c.CsprojFilePath ?? throw new NullReferenceException()
                                                              , c => c.ReplaceWithFilePath)
                                                          ?? new Dictionary<string, string?>();

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

        XmlWriterSettings settings = new() { Indent = true, Encoding = Encoding.UTF8 };
        Console.WriteLine($"Merging projects to {slnFilePath}");
        foreach (Project project in conf.Projects)
            if (project.ProjectFileMerge != null)
            {
                string file = Path.Combine(baseDirectoryPath, project.FilePath.TrimStart('.', '\\', '/'));
                string projectDirectory = Path.GetDirectoryName(file)!;
                if (!Directory.Exists(projectDirectory))
                    Directory.CreateDirectory(projectDirectory);

                Console.Write(project.Name + ' ');

                StringComparer comparer = Environment.OSVersion.Platform.ToString().StartsWith("Win")
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;

                string[] origPathParts = project.AbsoluteOriginalDirectory.Split(Path.DirectorySeparatorChar);
                string[] newPathParts = projectDirectory.Split(Path.DirectorySeparatorChar);
                string commonPath = "", relPath = "";
                for (int i = 0; i < origPathParts.Length; i++)
                    if (string.IsNullOrWhiteSpace(relPath) && comparer.Equals(origPathParts[i], newPathParts[i]))
                        commonPath += origPathParts[i] + Path.DirectorySeparatorChar;
                    else
                        relPath += string.IsNullOrWhiteSpace(origPathParts[i]) ? "" : ".." + Path.DirectorySeparatorChar;

                if (string.IsNullOrWhiteSpace(commonPath))
                    commonPath = $"..{Path.DirectorySeparatorChar}";

                XmlDocument newProj = new();
                CopyProject(project.ProjectFileMerge, newProj, fileToCopySourceDic, relPath);

                StringBuilder sb = new();
                using XmlWriter xmlWriter = XmlWriter.Create(sb, settings);
                newProj.WriteTo(xmlWriter);
                xmlWriter.Flush();
                string xml = sb.ToString();
                string toWrite = xml.Replace(commonPath, relPath);
                File.WriteAllText(file, toWrite, Encoding.UTF8);
                foreach (string toCopy in GetToCopy(newProj, ""))
                {
                    string destination = Path.Combine(projectDirectory, toCopy);
                    if (fileToCopySourceDic.TryGetValue(Path.GetFileName(destination), out string? source))
                    {
                        if (!string.IsNullOrWhiteSpace(source))
                            CopyWithBackup(source, destination, true);
                    }
                    else
                        CopyWithBackup(Path.Combine(project.AbsoluteOriginalDirectory, toCopy), destination, false);
                }
            }

        Console.WriteLine();
        Console.WriteLine("Done");
    }

    private static void CopyProject(XmlNode from, XmlNode to, Dictionary<string, string?> fileToCopySourceDic, string destination, string relPath)
    {
        if (from.Name == "Project")
        {
            // <Content Include="..\..\MyContentFiles\**\*.*"><Link>%(RecursiveDir)%(Filename)%(Extension)</Link></Content>

            XmlNode elt = to.OwnerDocument!.CreateElement("Compile");
            to.AppendChild(elt);
            XmlAttribute attr = to.OwnerDocument!.CreateAttribute(IncludeAttribute);
            attr.Value = Path.Combine(relPath, "**", "*.cs");
            elt.Attributes!.Append(attr);
            XmlNode link = to.OwnerDocument!.CreateElement("Link");
            elt.AppendChild(link);
            link.InnerText = "%(RecursiveDir)%(Filename)%(Extension)";
        }

        foreach (XmlNode child in from.ChildNodes)
        {
            XmlNode copy = to.OwnerDocument!.CreateElement(child.Name);
            foreach (XmlAttribute attrFrom in from.Attributes!)
            {
                XmlAttribute attrTo = to.OwnerDocument!.CreateAttribute(attrFrom.Name);
                attrTo.Value = attrFrom.Value;
                copy.Attributes!.Append(attrTo);
            }

            if (child.Name != SlnHelpers.PackageReference)
            {
                ReplaceFileAttribute(copy, UpdateAttribute, fileToCopySourceDic, destination, relPath);
                ReplaceFileAttribute(copy, IncludeAttribute, fileToCopySourceDic, destination, relPath);
                ReplaceFileAttribute(copy, RemoveAttribute, fileToCopySourceDic, destination, relPath);
            }

            CopyProject(child, copy, fileToCopySourceDic, destination, relPath);
        }
    }

    private static void ReplaceFileAttribute(
        XmlNode node
        , string attrName
        , Dictionary<string, string?> fileToCopySourceDic
        , string destination
        , string relPath)
    {
        if (node.Name == SlnHelpers.PackageReference)
            return;

        XmlAttribute? attr = node.Attributes?[attrName];
        if (string.IsNullOrWhiteSpace(attr?.Value))
            return;

        string copyTo = Path.Combine(destination, attr.Value);
        if (fileToCopySourceDic.TryGetValue(attr.Value, out string? source))
        {
            if (!string.IsNullOrWhiteSpace(source))
                CopyWithBackup(source, destination, true);
        }
        else
            CopyWithBackup(Path.Combine(project.AbsoluteOriginalDirectory, copyTo), destination, false);

        attr.Value = Path.Combine(relPath, attr.Value);
    }


    private static void CopyWithBackup(string origin, string destination, bool replaceDestination)
    {
        origin = origin.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        destination = destination.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
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
            string toCopy = prefix + (node.ParentNode?.Attributes?["Update"]?.Value ?? node.ParentNode?.Attributes?["Include"]?.Value);
            if (toCopy != prefix && toCopy != "Never")
                yield return toCopy;
        }
    }
}

internal class SlnMergeConfiguration
{
    public required Project Project { get; set; }
    public required Dictionary<string, string> toReplace { get; set; }
}
