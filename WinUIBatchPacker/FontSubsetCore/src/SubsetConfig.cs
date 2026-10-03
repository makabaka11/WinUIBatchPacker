namespace AssFontSubset.Core;

public struct SubsetConfig
{
    public bool SourceHanEllipsis;
    public bool DebugMode;
    public bool AllowMissingFonts;
    public IReadOnlyDictionary<string, string>? FontAliases;
    public SubsetBackend Backend;
}

public enum SubsetBackend
{
    PyFontTools = 1,
    HarfBuzzSubset = 2,
}
