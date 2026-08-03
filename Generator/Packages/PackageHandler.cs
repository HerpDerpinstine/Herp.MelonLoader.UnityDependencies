using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Octokit;
using FileMode = System.IO.FileMode;

namespace Generator.Packages;

public static class PackageHandler
{
    private const string _searchPayload = "Payload";

    private static string GetDownloadURL(UnityVersion unityVersion, 
        UnityPlatformID platformId,
        UnityPlatformID supportPlatform,
        Architecture arch,
        ePackageType setupType)
    {
        if (setupType == ePackageType.Setup)
            return UnityAPI.GetSetupURL(unityVersion, platformId, arch);
        
        if ((platformId == UnityPlatformID.Windows)
            && ((unityVersion.Major < 2018)
                || unityVersion is { Major: 2018, Minor: < 1 }))
            supportPlatform = UnityPlatformID.UWP;
        return UnityAPI.GetComponentURL(unityVersion, platformId, supportPlatform, UnityRuntimeID.IL2CPP);
    }

    internal static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }

    internal static async Task<string> Download(UnityVersion unityVersion,
        UnityPlatformID platformId,
        Architecture arch,
        ePackageType setupType)
    {
        UnityPlatformID supportPlatform = platformId;
        if (platformId == UnityPlatformID.Android)
        {
            platformId = UnityPlatformID.Mac;
            arch = Architecture.X64;
        }
        
        string downloadUrl = GetDownloadURL(unityVersion, platformId, supportPlatform, arch, setupType);
        string downloadName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string downloadPath = Path.Combine(Program._tempDir, downloadName);
        Console.WriteLine($"Downloading {unityVersion} -> {downloadUrl}");
        
        try
        {
            if (Config.WebRequestPrintDownloadProgress)
                Console.WriteLine("0%");
            await HttpRequest.DownloadFileAsync(downloadUrl, downloadPath, (progress) => Console.WriteLine($"{progress}%"));
            Console.WriteLine();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
            Console.WriteLine();
            return string.Empty;
        }
        
        return downloadPath;
    }

    internal static async Task Extract(UnityPlatformID platformId, string? downloadPath, string outputPath)
    {
        if (string.IsNullOrEmpty(downloadPath)
            || !File.Exists(downloadPath))
            return;
        
        string downloadName = Path.GetFileName(downloadPath);
        Console.WriteLine($"Extracting {downloadName}");
        
        switch (platformId)
        {
            case UnityPlatformID.Windows:
                await NSISExtractor.ExtractAsync(downloadPath, outputPath);
                File.Delete(downloadPath);
                break;
            
            case UnityPlatformID.Linux:
                await SevenZip.ExtractAsync(downloadPath, outputPath, false, "*.tar");
                File.Delete(downloadPath);
                
                Console.WriteLine("Extracting the Payload Archive");
                downloadPath = FindFile(outputPath, "*.tar");
                if (string.IsNullOrEmpty(downloadPath))
                    throw new FileNotFoundException(downloadPath);
                await SevenZip.ExtractAsync(downloadPath, outputPath, true);
                File.Delete(downloadPath);
                
                break;
            
            case UnityPlatformID.Mac:
                await SevenZip.ExtractAsync(downloadPath, outputPath, false, "*.pkg.tmp/Payload");
                File.Delete(downloadPath);

                Console.WriteLine("Extracting the Payload Archive");
                downloadPath = FindFile(outputPath, $"{_searchPayload}*");
                if (string.IsNullOrEmpty(downloadPath))
                    throw new FileNotFoundException(downloadPath);
                await SevenZip.ExtractAsync(downloadPath, outputPath, false, $"{_searchPayload}*");
                File.Delete(downloadPath);

                Console.WriteLine("Extracting the Payload Archive Archive");
                downloadPath = FindFile(outputPath, $"{_searchPayload}*");
                if (string.IsNullOrEmpty(downloadPath))
                    throw new FileNotFoundException(downloadPath);
                await SevenZip.ExtractAsync(downloadPath, outputPath, true);
                File.Delete(downloadPath);
                
                break;
        }
        
        Console.WriteLine();
    }

    internal static void Bundle(
        string packageName,
        string packagePath,
        params string[] targetPaths)
    {
        Console.WriteLine($"Bundling {packageName}");
        
        if (!string.IsNullOrEmpty(Config.UnityOutputDirectory))
        {
            if (!Directory.Exists(Config.UnityOutputDirectory))
                Directory.CreateDirectory(Config.UnityOutputDirectory);
            packagePath = Path.Combine(Config.UnityOutputDirectory, packageName);
        }

        string searchPattern = "*.dll";
        bool fileExists = File.Exists(packagePath);
        using var managedZipStr = File.Open(packagePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using (var managedZip = new ZipArchive(managedZipStr, fileExists
                   ? ZipArchiveMode.Update
                   : ZipArchiveMode.Create, true))
            foreach (var targetPath in targetPaths)
                BundleDirectory(managedZip, targetPath, searchPattern);
        managedZipStr.Close();
        Console.WriteLine();
    }

    private static void BundleDirectory(ZipArchive archive, string searchDir, string searchPattern)
    {
        foreach (var file in Directory.EnumerateFiles(searchDir, searchPattern))
            archive.CreateEntryFromFile(file, Path.GetFileName(file));
    }

    private static string? FindFile(string dirPath, string searchPattern)
    {
        foreach (var file in Directory.EnumerateFiles(dirPath, searchPattern, SearchOption.AllDirectories))
            return file;
        return null;
    }
    
    internal static string? FindFilteredFolder(string dirPath, string targetFile, List<string> filters)
    {
        foreach (var file in Directory.EnumerateFiles(dirPath, targetFile, SearchOption.AllDirectories))
        {
            foreach (var filter in filters)
            {
                string filterPath = Path.Combine(filter, targetFile);
                if (file.EndsWith(filterPath))
                    return Path.GetDirectoryName(file);
            }
        }
        return null;
    }
    
    internal static string GetPackageName(UnityPlatformID platformId, Architecture arch)
    {
        return $"IL2CPP.{Enum.GetName(platformId)}.{Enum.GetName(arch)!.ToLowerInvariant()}.zip";
    }
    
    internal static List<string> GetFilters(UnityPlatformID platformId, Architecture architecture, ePackageType setupType)
    {
        string arch = (architecture == Architecture.Arm64) 
            ? "arm64"
            : (architecture == Architecture.X86) 
                ? "32" 
                : "64";
        
        switch (platformId)
        {
            case UnityPlatformID.Android:
                if (setupType == ePackageType.Setup)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-android",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
                        "/MonoBleedingEdge/lib/mono/unity",
                    ];
                }
                else
                {
                    return
                    [
                        "/Variations/il2cpp/Managed",
                    ];
                }
            
            case UnityPlatformID.Linux:
                if (setupType == ePackageType.Setup)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-linux",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
                        "/MonoBleedingEdge/lib/mono/unity",
                    ];
                }
                else
                {
                    return [
                        $"/Variations/linux_{arch}_player_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/linux_{arch}_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/linux{arch}_player_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/linux{arch}_nondevelopment_il2cpp/Data/Managed",
                        "/Variations/il2cpp/Managed",
                    ];
                }
            
            case UnityPlatformID.UWP:
            case UnityPlatformID.Windows:
                if (setupType == ePackageType.Setup)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-win32",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
                        "/MonoBleedingEdge/lib/mono/unity",
                    ];
                }
                else
                {
                    return [
                        $"/Variations/win_{arch}_player_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/win_{arch}_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/win{arch}_player_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/win{arch}_nondevelopment_il2cpp/Data/Managed",
                        "/Variations/il2cpp/Managed",
                        "/Managed/il2cpp"
                    ];
                }

            case UnityPlatformID.Mac:
                if (setupType == ePackageType.Setup)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-macos",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
                        "/MonoBleedingEdge/lib/mono/unity",
                    ];
                }
                else
                {
                    return [
                        $"/Variations/macos_x{arch}_player_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/macos_x{arch}_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/macosx{arch}_player_nondevelopment_il2cpp/Data/Managed",
                        $"/Variations/macosx{arch}_nondevelopment_il2cpp/Data/Managed",
                        "/Variations/il2cpp/Managed",
                    ];
                }
        }
        return new();
    }
}