using AutoShopper.Core.Localization;

namespace AutoShopper.Core.Changelog;

internal readonly record struct ChangelogEntry(string Version, string Date, LocString[] Highlights);
