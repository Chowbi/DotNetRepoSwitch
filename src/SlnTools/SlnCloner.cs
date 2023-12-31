﻿using System.Xml;

namespace SlnTools;

public static class SlnCloner
{
    public static SolutionConfiguration Clone(this SolutionConfiguration config)
    {
        SolutionConfiguration result = new(config.SlnFile)
        {
            VsMinimalVersionStr = config.VsMinimalVersionStr,
            SlnVersionStr = config.SlnVersionStr,
            VsMajorVersionStr = config.VsMajorVersionStr,
            VsVersionStr = config.VsVersionStr
        };
        foreach (Project project in config.Projects)
            result.Projects.Add(project.Clone());

        foreach (Section section in config.Sections)
            result.Sections.Add(section.Clone());

        return result;
    }

    public static Project Clone(this Project project)
    {
        Project copy = new()
        {
            FilePath = project.FilePath,
            AbsoluteFilePath = project.AbsoluteFilePath,
            AbsoluteOriginalDirectory = project.AbsoluteOriginalDirectory,
            Name = project.Name,
            OriginalName = project.OriginalName,
            ProjectGuid = project.ProjectGuid,
            ProjectTypeGuid = project.ProjectTypeGuid
        };
        if (project.ProjectFile != null)
        {
            copy.ProjectFile = new XmlDocument();
            copy.ProjectFile.LoadXml(project.ProjectFile.OuterXml);
            copy.PackageReferences.AddRange(copy.ProjectFile.RetrieveNodes(SlnHelpers.PackageReference));
            copy.ProjectReferences.AddRange(copy.ProjectFile.RetrieveNodes(SlnHelpers.ProjectReference));
        }

        return copy;
    }

    public static Section Clone(this Section section, bool cloneLines = true)
    {
        Section copy = new()
        {
            IsPreSolution = section.IsPreSolution,
            Name = section.Name
        };
        if (cloneLines)
            foreach (string line in section.Lines)
                copy.Lines.Add(line);
        return copy;
    }
}