using System.Runtime.InteropServices;
using Octokit;

namespace Generator.Packages;

internal class PackagePC
    : PackageBase
{
    private readonly PackageBase _componentX86;
    private readonly PackageBase _componentX64;

    internal PackagePC(UnityPlatformID platform)
    {
        // Create Setup Package
        Platform = platform;
        Arch = Architecture.X64;
        Type = ePackageType.Setup;
        
        // Create Component Package for x86
        _componentX86 = new()
        {
            Platform = platform,
            Arch = Architecture.X86,
            Type = ePackageType.Component
        };

        // Create Component Package for x64
        _componentX64 = new()
        {
            Platform = platform,
            Arch = Architecture.X64,
            Type = ePackageType.Component
        };
    }

    internal override async Task<bool> Download(UnityVersion unityVersion)
    {
        // Download Setup
        if (!await base.Download(unityVersion))
            return false;

        // Download Component for x64
        await _componentX64.Download(unityVersion);
        
        // Return Success
        _componentX86.DownloadPath = _componentX64.DownloadPath;
        return true;
    }

    internal override async Task<bool> Extract()
    {
        // Extract Setup
        if (!await base.Extract())
            return false;
        
        // Extract Component for x64
        await _componentX64.Extract();
        
        // Return Success
        _componentX86.ExtractedPath = _componentX64.ExtractedPath;
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

        // Bundle x86
        if (!BundlePackage(_componentX86))
            return false;
        
        // Bundle x64
        if (!BundlePackage(_componentX64))
            return false;
        
        // Return Success
        return true;
    }

    internal override async Task Upload(Release release)
    {
        await _componentX86.Upload(release);
        await _componentX64.Upload(release);
    }
}