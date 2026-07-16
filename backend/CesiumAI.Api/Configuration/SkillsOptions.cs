using Microsoft.Extensions.Options;

namespace CesiumAI.Api.Configuration;

public sealed class SkillsOptions
{
    public const string SectionName = "Skills";

    public string Path { get; init; } = "../astrox-skills/skills";

    public string ResolveExistingDirectory(string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            throw new ArgumentException("Skills:Path cannot be blank.", nameof(Path));
        }

        if (System.IO.Path.IsPathRooted(Path))
        {
            throw new ArgumentException(
                "Skills:Path must be relative to the application content root.",
                nameof(Path));
        }

        string resolvedPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(contentRootPath, Path));
        if (!Directory.Exists(resolvedPath))
        {
            throw new DirectoryNotFoundException(
                $"Skills directory '{resolvedPath}' does not exist. Configure Skills:Path as a path relative to the application content root.");
        }

        return resolvedPath;
    }
}

public sealed class SkillsOptionsValidator(IHostEnvironment hostEnvironment)
    : IValidateOptions<SkillsOptions>
{
    private readonly IHostEnvironment _hostEnvironment =
        hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));

    public ValidateOptionsResult Validate(
        string? name,
        SkillsOptions options)
    {
        try
        {
            options.ResolveExistingDirectory(_hostEnvironment.ContentRootPath);
            return ValidateOptionsResult.Success;
        }
        catch (Exception exception)
            when (exception is ArgumentException or DirectoryNotFoundException)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
