// See https://aka.ms/new-console-template for more information

using SlnTools;

SolutionConfiguration conf = SlnMerger.Merge(
    "AbsolutePathTo.sln",
    "AbsolutePathTo.sln");

SlnMerger.WriteTo("AbsolutePathToAnotherToBeCreated.sln", conf);
// or if you need to replace some file
SlnMerger.WriteTo("AbsolutePathToAnotherToBeCreated.sln", conf
    , ("a file name that need copy (ie: appsettings.json", "full path to the source to use"));