using System.Runtime.InteropServices;
using Octokit;

namespace Generator.Packages;

internal class PackageMacOS
    : PackagePC
{
    internal PackageMacOS() 
        : base(UnityPlatformID.Mac)
    {
        
    }

    internal override async Task<bool> Download(UnityVersion unityVersion)
    {
        // Run Main Download
        if (!await base.Download(unityVersion))
            return false;
        
        // Return Success
        return true;
    }

    internal override async Task<bool> Extract()
    {
        // Run Main Extract
        if (!await base.Extract())
            return false;
        
        // Return Success
        return true;
    }

    internal override bool Bundle()
    {
        // Run Main Bundle
        if (!base.Bundle())
            return false;
        
        // Return Success
        return true;
    }
    
    internal override async Task Upload(Release release)
    {
        await base.Upload(release);
    }
}