// See https://aka.ms/new-console-template for more information

using SlnTools;

SolutionConfiguration conf = SlnMerger.Merge(
    "AbsolutePathTo.sln",
    "AbsolutePathTo.sln");

SlnMerger.WriteTo("AbsolutePathToAnotherToBeCreated.sln", conf);