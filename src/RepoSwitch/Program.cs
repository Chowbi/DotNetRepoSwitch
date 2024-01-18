// See https://aka.ms/new-console-template for more information

using SlnTools;

SolutionConfiguration conf = SlnMerger.Merge(
    "AbsolutePathTo.sln",
    "AbsolutePathTo.sln");

SlnMerger.WriteTo("AbsolutePathToAnotherToBeCreated.sln", conf, true /* copy files from main sln dir (or not) */);
// or if you need to replace some file
SlnMerger.WriteTo("AbsolutePathToAnotherToBeCreated.sln", conf, true /* copy files from main sln dir (or not) */
    , ("a file name that need copy (ie: appsettings.json", "full path to the source to use"));