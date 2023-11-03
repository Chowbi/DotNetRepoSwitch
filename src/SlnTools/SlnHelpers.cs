using System.Xml;

namespace SlnTools;

public static class SlnHelpers
{
    public const string CsProjExtension = ".csproj";
    public const string PackageReference = "PackageReference";
    public const string ProjectReference = "ProjectReference";
    public const string IncludeAttribute = "Include";
    
    
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
}