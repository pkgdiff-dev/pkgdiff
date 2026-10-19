using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IManifestParser
{
    // Detects the file type from the content and reads the packages it names. Throws ArgumentException
    // with a message for the visitor when the file is not one of the registry's supported types.
    ManifestParseResult Parse(string registry, string content, string? fileName);
}
