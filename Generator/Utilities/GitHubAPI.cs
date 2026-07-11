using Microsoft.AspNetCore.StaticFiles;
using Octokit;
using ProductHeaderValue = Octokit.ProductHeaderValue;

namespace Generator;

internal static class GitHubAPI
{
    private static string? _repoOwner;
    private static string? _repoName;
    
    private static GitHubClient? _client;
    private static GitHubClient GetClient()
    {
        if (string.IsNullOrEmpty(_repoOwner)
            || string.IsNullOrEmpty(_repoName))
        {
            string[] repoSplit = Config.GitHubRepo.Split('/');
            _repoOwner = repoSplit[0];
            _repoName = repoSplit[1];
        }
        
        if (_client == null)
        {
            _client = new(new ProductHeaderValue(_repoName));
            _client.Credentials = new(Config.GitHubApiKey);
        }

        return _client;
    }
    
    internal static async Task<IReadOnlyList<Release>> GetAllReleasesAsync()
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Get Releases
        return await client.Repository.Release.GetAll(_repoOwner, _repoName);
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
        return await client.Repository.Release.Create(_repoOwner, _repoName, new(tag)
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
        return await client.Repository.Release.Edit(_repoOwner, _repoName, id, update);
    }

    internal static async Task<Reference> CreateTag(string tag)
    {
        // Get Client
        GitHubClient client = GetClient();
        
        // Create Tag
        var commitSha = (await client.Repository.Branch.Get(_repoOwner, _repoName, Config.GitHubRepoBranch)).Commit.Sha;
        return await client.Git.Reference.Create(_repoOwner, _repoName, new($"refs/tags/{tag}", commitSha));
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