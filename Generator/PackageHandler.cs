using System.IO.Compression;
using System.Runtime.InteropServices;
using Octokit;
using FileMode = System.IO.FileMode;

namespace Generator;

public static class PackageHandler
{
    private const string _searchMscorlib = "mscorlib.dll";
    private const string _searchUnityEngine = "UnityEngine.dll";
    private const string _searchPayload = "Payload";

    private enum eSetupType
    {
        SETUP,
        SUPPORT,
    }
    
    public static async Task Process(Release release,
        UnityVersion unityVersion, 
        UnityPlatformID platformId,
        Architecture arch)
    {
        // Download the Support Package
        string supportDownload = await Download(unityVersion, platformId, arch, eSetupType.SUPPORT);
        bool shouldHandleAndroid = (Config.UnityProcessAndroid && (platformId == UnityPlatformID.Mac) && (arch == Architecture.X64));
        if (string.IsNullOrEmpty(supportDownload) && !shouldHandleAndroid)
            return;
        
        // Download the Android Support for Mac on x64
        string androidDownload = string.Empty;
        if (shouldHandleAndroid)
        {
            androidDownload = await Download(unityVersion, UnityPlatformID.Android, arch, eSetupType.SUPPORT);
            if (string.IsNullOrEmpty(androidDownload))
                shouldHandleAndroid = false;
        }
        
        // Download the Setup Package
        string setupDownload = await Download(unityVersion, platformId, arch, eSetupType.SETUP);
        if (string.IsNullOrEmpty(setupDownload))
            return;

        // Extract the Support Package
        string? supportDirPath = Path.Combine(Program._tempDir, "Support");
        if (!Directory.Exists(supportDirPath))
            Directory.CreateDirectory(supportDirPath);
        bool shouldProcessSupport = !string.IsNullOrEmpty(supportDownload);
        if (shouldProcessSupport)
        {
            await Extract(platformId, supportDownload, supportDirPath);
            
            // Find Support Folder
            List<string> supportFilters = GetFilters(platformId, arch, eSetupType.SUPPORT);
            supportDirPath = FindFilteredFolder(supportDirPath, _searchUnityEngine, supportFilters);
            if (string.IsNullOrEmpty(supportDirPath))
            {
                shouldProcessSupport = false;
                Console.WriteLine("Could not find support folder");
            }
        }
        
        // Extract the Android Support for Mac on x64
        string? androidDirPath = Path.Combine(Program._tempDir, "Android");
        if (shouldHandleAndroid)
        {
            if (!Directory.Exists(androidDirPath))
                Directory.CreateDirectory(androidDirPath);
            
            await Extract(platformId, androidDownload, androidDirPath);
            
            // Find Android Support Folder
            List<string> androidFilters = GetFilters(UnityPlatformID.Android, arch, eSetupType.SUPPORT);
            androidDirPath = FindFilteredFolder(androidDirPath, _searchUnityEngine, androidFilters);
            if (string.IsNullOrEmpty(androidDirPath))
            {
                shouldHandleAndroid = false;
                Console.WriteLine("Could not find android support folder");
            }
        }
        if (!shouldProcessSupport && !shouldHandleAndroid)
            return;

        // Extract the Setup Package
        string? setupDirPath = Path.Combine(Program._tempDir, "Setup");
        string? androidSetupDirPath = setupDirPath;
        if (!Directory.Exists(setupDirPath))
            Directory.CreateDirectory(setupDirPath);
        await Extract(platformId, setupDownload, setupDirPath);
        
        // Find Setup Folder
        List<string> setupFilters = GetFilters(platformId, arch, eSetupType.SETUP);
        setupDirPath = FindFilteredFolder(setupDirPath, _searchMscorlib, setupFilters);
        if (string.IsNullOrEmpty(setupDirPath))
            throw new Exception("Could not find setup folder");
        
        // Find Android Setup Folder
        if (shouldHandleAndroid)
        {
            List<string> androidSetupFilters = GetFilters(UnityPlatformID.Android, arch, eSetupType.SETUP);
            androidSetupDirPath = FindFilteredFolder(androidSetupDirPath, _searchMscorlib, androidSetupFilters);
            if (string.IsNullOrEmpty(androidSetupDirPath))
            {
                shouldHandleAndroid = false;
                Console.WriteLine("Could not find android setup folder");
            }
        }
        if (!shouldProcessSupport && !shouldHandleAndroid)
            return;

        // Bundle Extracted Files
        string packageName = string.Empty;
        string packagePath = string.Empty; 
        if (shouldProcessSupport)
        {
            packageName = GetPackageName(platformId, arch);
            packagePath = Path.Combine(Program._tempDir, packageName);
            Bundle(packageName, packagePath, setupDirPath, supportDirPath!);
        }

        // Bundle Android Files
        string androidPackageName = string.Empty;
        string androidPackagePath = string.Empty;
        if (shouldHandleAndroid)
        {
            androidPackageName = GetPackageName(UnityPlatformID.Android, arch);
            androidPackagePath = Path.Combine(Program._tempDir, androidPackageName);
            Bundle(androidPackageName, androidPackagePath, androidSetupDirPath!, androidDirPath!);
        }
        
        // Bundle x86
        string x86PackageName = string.Empty;
        string x86PackagePath = string.Empty;
        bool shouldHandleX86 = (arch == Architecture.X64) && platformId is UnityPlatformID.Windows or UnityPlatformID.Mac;
        if (shouldProcessSupport && shouldHandleX86)
        {
            // Find x86 Support Folder
            List<string> x86SupportFilters = GetFilters(platformId, Architecture.X86, eSetupType.SUPPORT);
            string? x86SupportDirPath = FindFilteredFolder(supportDirPath!, _searchUnityEngine, x86SupportFilters);
            if (string.IsNullOrEmpty(x86SupportDirPath))
            {
                shouldHandleX86 = false;
                Console.WriteLine("Could not find x86 support folder");
            }
            else
            {
                x86PackageName = GetPackageName(platformId, Architecture.X86);
                x86PackagePath = Path.Combine(Program._tempDir, x86PackageName);
                Bundle(x86PackageName, x86PackagePath, setupDirPath!, x86SupportDirPath!);
            }
        }
        if (!shouldProcessSupport && !shouldHandleAndroid && !shouldHandleX86)
            return;

        // Upload the newly created Bundle
        if (Config.GitHubUploadPackages)
        {
            if (shouldProcessSupport)
            {
                Console.WriteLine($"Uploading {packageName}");
                await GitHubAPI.UploadFile(packagePath, release);
            }

            // Upload Android Bundle and Libs
            if (shouldHandleAndroid)
            {
                // Android Bundle
                Console.WriteLine($"Uploading {androidPackageName}");
                await GitHubAPI.UploadFile(androidPackagePath, release);
                
                // Android Libs
            }

            // Upload x86 Bundle
            if (shouldHandleX86)
            {
                Console.WriteLine($"Uploading {x86PackageName}");
                await GitHubAPI.UploadFile(x86PackagePath, release);
            }
        }
    }

    private static string GetDownloadURL(UnityVersion unityVersion, 
        UnityPlatformID platformId,
        UnityPlatformID supportPlatform,
        Architecture arch,
        eSetupType setupType)
    {
        if (setupType == eSetupType.SETUP)
            return UnityAPI.GetSetupURL(unityVersion, platformId, arch);
        return UnityAPI.GetComponentURL(unityVersion, platformId, supportPlatform, UnityRuntimeID.IL2CPP);
    }

    private static async Task<string> Download(UnityVersion unityVersion,
        UnityPlatformID platformId,
        Architecture arch,
        eSetupType setupType)
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

    private static async Task Extract(UnityPlatformID platformId, string? downloadPath, string outputPath)
    {
        if (string.IsNullOrEmpty(downloadPath))
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

    private static void Bundle(
        string packageName,
        string packagePath,
        string setupDirPath,
        string supportDirPath)
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
        {
            BundleDirectory(managedZip, setupDirPath, searchPattern);
            BundleDirectory(managedZip, supportDirPath, searchPattern);
        }
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
    
    private static string? FindFilteredFolder(string dirPath, string targetFile, List<string> filters)
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
    
    private static string GetPackageName(UnityPlatformID platformId, Architecture arch)
    {
        if (platformId == UnityPlatformID.Android)
            return "IL2CPP.AOT.Android.zip";
        return $"IL2CPP.AOT.{Enum.GetName(platformId)}.{Enum.GetName(arch)!.ToLowerInvariant()}.zip";
    }
    
    private static List<string> GetFilters(UnityPlatformID platformId, Architecture architecture, eSetupType setupType)
    {
        string arch = (architecture == Architecture.Arm64) 
            ? "arm64"
            : (architecture == Architecture.X86) 
                ? "32" 
                : "64";
        switch (platformId)
        {
            case UnityPlatformID.Android:
                if (setupType == eSetupType.SETUP)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-android",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
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
                if (setupType == eSetupType.SETUP)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-linux",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
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
            
            case UnityPlatformID.Windows:
                if (setupType == eSetupType.SETUP)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-win32",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
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
                if (setupType == eSetupType.SETUP)
                {
                    return
                    [
                        "/MonoBleedingEdge/lib/mono/unityaot-macos",
                        "/MonoBleedingEdge/lib/mono/unityaot",
                        "/MonoBleedingEdge/lib/mono/unity_aot",
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