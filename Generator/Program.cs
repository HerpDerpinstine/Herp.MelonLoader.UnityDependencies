using System.Runtime.InteropServices;
using Generator.Packages;
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
    
    private static readonly PackageBase[] _packages =
    [
        // Windows
        new PackagePC(UnityPlatformID.Windows),
        new PackageArm64(UnityPlatformID.Windows),
        
        // Linux
        new PackagePC(UnityPlatformID.Linux),
        
        // MacOS
        new PackageMacOS(),
        new PackageArm64(UnityPlatformID.Mac),
    ];

    private static List<Release> _githubReleases = [];
    private static IEnumerable<UnityVersion> _unityReleases = [];

    // Herp:
    // Releases set as Draft signify generation failure/cancellation
    // Releases set as Prelease signify regeneration was requested and is awaiting to be reprocessed
    private static async Task Main()
    {
        // Load Configuration
        Console.WriteLine("Loading Environment Configuration...");
        Config.Load();

        // Fetch Unity Releases
        Console.WriteLine("Fetching Unity Releases...");
        _unityReleases = UnityAPI.GetAvailableVersionsAsync(false, false).Result;
        
        // Fetch GitHub Releases
        Console.WriteLine("Fetching GitHub Releases...");
        _githubReleases = GitHubAPI.GetAllReleasesAsync().Result.ToList();
        _githubReleases.RemoveAll(x =>
            !UnityVersion.TryParse(x.TagName, string.Empty, out UnityVersion releaseVersion)
            || !_unityReleases.Contains(releaseVersion));

        // Find Latest Version
        Console.WriteLine("Finding Latest GitHub Release...");
        UnityVersion? latest = FindLatest();
        if (latest != null)
            Console.WriteLine($"Found: {latest.Value}");

        // Set All as Prereleases to signify Regeneration
        if (Config.GitHubUploadPackages
            && Config.GitHubUpdateExistingReleases)
        {
            Console.WriteLine("Applying Prerelease Tag to signify regeneration...");
            foreach (var release in _githubReleases)
                if (release is { Draft: false } and { Prerelease: false })
                {
                    Console.WriteLine(release.TagName);
                    await GitHubAPI.SetReleaseType(release!, eReleaseType.Prelease);
                }
        }

        // Process Releases
        foreach (var unityVersion in _unityReleases)
        {
            // Exclude versions that aren't supported by extraction
            if (UnityVersionComparer.Instance.Compare(unityVersion, _minVersion) <= 0)
                continue;

            // Find Release
            string tag = unityVersion.ToString();
            Console.WriteLine($"Processing {tag}...");
            Release? release = FindGitHubRelease(tag);
            if (!string.IsNullOrEmpty(Config.UnityTargetVersion))
            {
                // Exclude versions that aren't specifically targeted
                if (tag != Config.UnityTargetVersion)
                    continue;
            }
            else
            {
                // Exclude anything that isn't set as Draft or Prelease
                if (!Config.GitHubUpdateExistingReleases 
                    && (release is { Draft: false } and { Prerelease: false }))
                    continue;
            }

            // Create Release
            if (Config.GitHubUploadPackages)
            {
                _ = GitHubAPI.SetupTag(tag).Result;
                if (release == null)
                    release = GitHubAPI.CreateRelease(tag, tag, _releaseBody, true).Result;
            }

            // Set as Draft
            if (Config.GitHubUploadPackages && (release != null))
                await GitHubAPI.SetReleaseType(release!, eReleaseType.Draft);
            
            // Handle Packages
            bool success = true;
            foreach (var package in _packages)
            {
                if (await TryProcess(package, release!, unityVersion))
                    continue;
                success = false;
                break;
            }
            if (!success)
                continue;

            // Set Release as Public
            if (Config.GitHubUploadPackages
                && (release != null))
            {
                if ((latest == null) 
                    || (UnityVersionComparer.Instance.Compare(latest.Value, unityVersion) <= 0))
                {
                    latest = unityVersion;
                    await GitHubAPI.SetReleaseType(release!, eReleaseType.Latest);
                }
                else 
                    await GitHubAPI.SetReleaseType(release!, eReleaseType.None);
            }
        }
    }
    
    private static Release? FindGitHubRelease(string unityVersion)
        => _githubReleases.FirstOrDefault(x => x.TagName == unityVersion);

    private static UnityVersion? FindLatest()
    {
        UnityVersion? latest = null;
        foreach (var release in _githubReleases)
            if (release is { Draft: false } and { Prerelease: false })
            {
                if (!UnityVersion.TryParse(release.TagName, string.Empty, out UnityVersion releaseVersion))
                    continue;
                if ((latest == null)
                    || (UnityVersionComparer.Instance.Compare(latest.Value, releaseVersion) <= 0))
                    latest = releaseVersion;
            }
        return latest;
    }

    private static async Task<bool> TryProcess(
        PackageBase package,
        Release release,
        UnityVersion unityVersion)
    {
        // Create Temporary Directory
        PackageHandler.RecreateDirectory(_tempDir);

        // Process Package
        bool success = true;
        try
        {
            if (await package.Download(unityVersion)
                && await package.Extract()
                && package.Bundle())
            {
                if (Config.GitHubUploadPackages)
                    await package.Upload(release);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            success = false;
        }
        
        // Remove Temporary Directory
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        
        Console.WriteLine();
        Console.WriteLine("------");
        Console.WriteLine();
        return success;
    }
}
