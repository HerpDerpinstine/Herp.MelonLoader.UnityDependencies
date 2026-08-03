using System.Runtime.InteropServices;
using Octokit;

namespace Generator.Packages;

internal class PackageArm64
    : PackageBase
{
    private readonly PackageBase _componentArm64;

    internal PackageArm64(UnityPlatformID platform)
    {
        // Create Setup Package
        Platform = platform;
        Arch = Architecture.Arm64;
        Type = ePackageType.Setup;
        
        // Create Component Package for Arm64
        _componentArm64 = new()
        {
            Platform = platform,
            Arch = Architecture.Arm64,
            Type = ePackageType.Component
        };
    }

    internal override async Task<bool> Download(UnityVersion unityVersion)
    {
        // Download Setup
        if (!await base.Download(unityVersion))
            return false;

        // Download Component for Arm64
        await _componentArm64.Download(unityVersion);
        
        // Return Success
        return true;
    }

    internal override async Task<bool> Extract()
    {
        // Extract Setup
        if (!await base.Extract())
            return false;
        
        // Extract Component for Arm64
        await _componentArm64.Extract();
        
        // Return Success
        return true;
    }

    internal override bool Bundle()
    {
        // Find Setup Folder
        if (!FindTargetFolder())
        {
            Console.WriteLine($"Unable to find Target Folder in {ExtractedPath}");
            return false;
        }

        // Bundle Arm64
        if (!BundlePackage(_componentArm64))
            return false;
        
        // Return Success
        return true;
    }

    internal override async Task Upload(Release release)
        => await _componentArm64.Upload(release);
}