namespace PkgDiff.Application.Interfaces;

public interface IReleaseNotesRenderer
{
    string RenderHtml(string markdown);
}
