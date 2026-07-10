using System.Runtime.InteropServices;
using Octokit;

namespace Generator;

internal static class Program
{
    internal static readonly string _tempDir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Temp");

    private const string _releaseBody =
        "Automatically generated and uploaded by the MelonLoader.UnityDependencies Generator";

    private static readonly UnityVersion _minVersion = new() 
    { 
        Id = string.Empty, 
        Major = 5, 
        Minor = 3,
        Patch = 0, 
        BuildType = 'b', 
        BuildNumber = 1
    };

    private static IReadOnlyList<Release> _githubReleases = [];
    private static IEnumerable<UnityVersion> _unityReleases = [];

    private static async Task Main()
    {
        // Load Configuration
        Config.Load();
        
        // Get All Releases
        _githubReleases = GitHubAPI.GetAllReleasesAsync().Result;
        _unityReleases = UnityAPI.GetAvailableVersionsAsync(false, false).Result;

        // Process Releases
        foreach (var unityVersion in _unityReleases)
        {
            // Exclude versions that aren't supported by extraction
            if (UnityVersionComparer.Instance.Compare(unityVersion, _minVersion) <= 0)
                continue;

            string tag = unityVersion.ToString();
            Release? release = FindGitHubRelease(tag);
            if (!string.IsNullOrEmpty(Config.UnityTargetVersion))
            {
                // Exclude versions that aren't specifically targeted
                if (tag != Config.UnityTargetVersion)
                    continue;
            }
            else
            {
                // Exclude versions that already exist
                if (!Config.GitHubUpdateExistingReleases
                    && (release != null))
                    continue;
            }

            // Create Release
            if (Config.GitHubUploadPackages
                && (release == null))
            {
                _ = GitHubAPI.CreateTag(tag).Result;
                release = GitHubAPI.CreateRelease(tag, tag, _releaseBody, true).Result;
            }
            
            // Process Version
            await TryProcess(release!, unityVersion, UnityPlatformID.Windows, Architecture.X64);
            //await TryProcess(release!, unityVersion, UnityPlatformID.Windows, Architecture.Arm64);
            await TryProcess(release!, unityVersion, UnityPlatformID.Linux, Architecture.X64);
            await TryProcess(release!, unityVersion, UnityPlatformID.Mac, Architecture.X64);
            await TryProcess(release!, unityVersion, UnityPlatformID.Mac, Architecture.Arm64);
            
            // Set Release as Public
            if (Config.GitHubUploadPackages)
                await GitHubAPI.SetReleaseDraft(release!, false);
        }
    }
    
    private static Release? FindGitHubRelease(string unityVersion)
        => _githubReleases.FirstOrDefault(x => x.TagName == unityVersion);

    private static async Task TryProcess(Release release,
        UnityVersion unityVersion,
        UnityPlatformID platform,
        Architecture architecture)
    {
        // Create Temporary Directory
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        Directory.CreateDirectory(_tempDir);
        
        try
        {
            await PackageHandler.Process(release!, unityVersion, platform, architecture);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        
        // Remove Temporary Directory
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        
        Console.WriteLine();
        Console.WriteLine("------");
        Console.WriteLine();
    }
}
