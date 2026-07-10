using Microsoft.AspNetCore.StaticFiles;
using Octokit;
using ProductHeaderValue = Octokit.ProductHeaderValue;

namespace Generator;

internal static class GitHubAPI
{
    private static GitHubClient? _client;
    private static GitHubClient GetClient()
    {
        if (_client == null)
        {
            _client = new(new ProductHeaderValue(Config.GitHubRepoName));
            _client.Credentials = new(Config.GitHubApiKey);
        }
        return _client;
    }
    
    internal static async Task<IReadOnlyList<Release>> GetAllReleasesAsync()
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Get Releases
        return await client.Repository.Release.GetAll(Config.GitHubRepoOwner, Config.GitHubRepoName);
    }

    internal static async Task<Release> SetReleaseDraft(Release release, bool value)
    {
        var draftUpdate = release.ToUpdate();
        draftUpdate.Draft = value;
        return await UpdateRelease(release.Id, draftUpdate);
    }
    
    internal static async Task UploadFile(string filePath, Release? release)
    {
        string assetName = Path.GetFileName(filePath);
        Console.WriteLine($"Uploading {assetName}");
        var provider = new FileExtensionContentTypeProvider();
        string contentType = provider.TryGetContentType(filePath, out var mime)
            ? mime
            : "application/octet-stream";
        await UploadAsset(release!, assetName, filePath, contentType);
    }
    
    internal static async Task<Release> CreateRelease(string tag, string name, string body, bool draft = false)
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Create a Release
        return await client.Repository.Release.Create(Config.GitHubRepoOwner, Config.GitHubRepoName, new(tag)
        {
            Name = name,
            Body = body,
            Draft = draft
        });
    }

    internal static async Task<Release> UpdateRelease(long id, ReleaseUpdate update)
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Update Release
        return await client.Repository.Release.Edit(Config.GitHubRepoOwner, Config.GitHubRepoName, id, update);
    }

    internal static async Task<Reference> CreateTag(string tag)
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Create Tag
        var commitSha = (await client.Repository.Branch.Get(Config.GitHubRepoOwner, Config.GitHubRepoName, Config.GitHubRepoBranch)).Commit.Sha;
        return await client.Git.Reference.Create(Config.GitHubRepoOwner, Config.GitHubRepoName, new($"refs/tags/{tag}", commitSha));
    }

    internal static async Task UploadAsset(Release release,
        string assetName,
        string filePath,
        string fileType)
    {
        // Open File
        using var fileStream = File.OpenRead(filePath);
        
        // Upload Asset to Release
        await UploadAsset(release, assetName, fileStream, fileType);
        
        // Close File
        fileStream.Close();
    }
    
    internal static async Task UploadAsset(Release release,
        string assetName,
        Stream fileStream,
        string fileType)
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Upload Asset to Release
        await client.Repository.Release.UploadAsset(release, 
            new(assetName, fileType, fileStream, 
            TimeSpan.FromSeconds(Config.GitHubTimeout)));
    }
}