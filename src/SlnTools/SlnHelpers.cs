using System.Xml;

namespace SlnTools;

public static class SlnHelpers
{
    public const string CsProjExtension = ".csproj";
    public const string PackageReference = "PackageReference";
    public const string ProjectReference = "ProjectReference";
    public const string IncludeAttribute = "Include";
    public const string RemoveAttribute = "Remove";
    public const string ItemGroup = "ItemGroup";


    public static IEnumerable<XmlNode> RetrieveNodes(this XmlNode root, string nodeName)
    {
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.Name == nodeName)
                yield return node;

            foreach (XmlNode value in RetrieveNodes(node, nodeName))
                yield return value;
        }
    }

    public static IEnumerable<string> RetrieveValues(this XmlNode root, string nodeName, string? attributeName)
    {
        foreach (XmlNode node in RetrieveNodes(root, nodeName))
        {
            if (node.Name == nodeName)
                if (attributeName == null && node.Value != null)
                    yield return node.Value;
                else if (attributeName != null)
                {
                    string? value = node.Attributes?[attributeName]?.Value;
                    if (value != null)
                        yield return value;
                }
        }
    }

    public static (string Name, string AttributeName) GetNameAndAttribute(XmlNode node)
    {
        if (node.Attributes == null)
            throw new NullReferenceException(nameof(node.Attributes));
        else if (node.Attributes[IncludeAttribute] != null && node.Attributes[RemoveAttribute] != null)
            throw new Exception($"It is not expected to have both {IncludeAttribute} and {RemoveAttribute}.");
        else if (node.Attributes[IncludeAttribute] != null)
            return (node.Attributes[IncludeAttribute]!.Value, IncludeAttribute);
        else if (node.Attributes[RemoveAttribute] != null)
            return (node.Attributes[RemoveAttribute]!.Value, RemoveAttribute);
        else
            throw new Exception($"{IncludeAttribute} nor {RemoveAttribute} was found.");
    }
}