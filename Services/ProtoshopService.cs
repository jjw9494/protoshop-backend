using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using fileshare.Entities;
using Microsoft.Extensions.Options;
using fileshare.Controllers;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon;
using Amazon.CloudFront;
using Microsoft.Extensions.Configuration;


namespace fileshare.Services;

public class ProtoshopService 
{

    private readonly IMongoCollection<TopLevelUserObject> _userCollection;
    private readonly IConfiguration _configuration;

    public ProtoshopService(IOptions<MongoDbSettings> mongoDBSettings, IConfiguration configuration)
    {
        var mongoClient = new MongoClient(mongoDBSettings.Value.AtlasURI);
        var mongoDatabase = mongoClient.GetDatabase(mongoDBSettings.Value.DatabaseName);

        _userCollection = mongoDatabase.GetCollection<TopLevelUserObject>("TopLevelUserObjects");
        _configuration = configuration;
    }

    public TopLevelUserObject? GetUserObject(string id)
    {
        var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, id);
        var userObject = _userCollection.Find(filter).FirstOrDefault();

        return userObject;
    }
    public TopLevelUserObject? GetUserIndividualObject(string id, string objId)
    {
        var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, id);
        
        var userObject = _userCollection.Find(filter).FirstOrDefault();

        return userObject;
    }

    public void CreateNewUserObject(string userId, string username){
        var newUserObject = new TopLevelUserObject();
        UserDirectory defaultUserDirectory = new UserDirectory();
        newUserObject.UserId = userId;
        newUserObject.Username = username;

        defaultUserDirectory.ObjId = "root";
        defaultUserDirectory.Name = "My Files";
        defaultUserDirectory.IsFolder = true;
        defaultUserDirectory.ObjChildren = [];        

        List<UserDirectory> userDirectoryList = [defaultUserDirectory];
        
        newUserObject.Directory = userDirectoryList;

        _userCollection.InsertOneAsync(newUserObject);

    }

public async Task<bool> DeleteItem(string userId, string itemObjId)
{
    Console.WriteLine($"DeleteItem called for user {userId}, item {itemObjId}");
    var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, userId);
    var user = await _userCollection.Find(filter).FirstOrDefaultAsync();
    if (user == null)
    {
        Console.WriteLine("User not found");
        return false;
    }

    bool itemFound = false;
    var itemsToDelete = new List<string>();
    user.Directory = DeleteItemRecursive(user.Directory, itemObjId, ref itemFound, itemsToDelete);

    if (itemFound)
    {
        Console.WriteLine($"Item found. Collected {itemsToDelete.Count} items to delete");
        var update = Builders<TopLevelUserObject>.Update.Set(u => u.Directory, user.Directory);
        var result = await _userCollection.UpdateOneAsync(filter, update);

        if (result.ModifiedCount > 0)
        {
            Console.WriteLine("MongoDB document updated successfully");
            // Delete items from S3
            if (itemsToDelete.Count > 0)
            {
                Console.WriteLine("Calling DeleteS3ObjectsBatch");
                // await DeleteS3ObjectsBatch(userId, itemsToDelete);
                Console.WriteLine("DeleteS3ObjectsBatch completed");
            }
            else
            {
                Console.WriteLine("No items to delete in S3");
            }
            return true;
        }
        else
        {
            Console.WriteLine("MongoDB document not modified");
        }
    }
    else
    {
        Console.WriteLine("Item not found");
    }
    return false;
}

private List<UserDirectory> DeleteItemRecursive(List<UserDirectory> items, string itemObjId, ref bool itemFound, List<string> itemsToDelete, string currentPath = "")
{
    for (int i = items.Count - 1; i >= 0; i--)
    {
        string itemPath = string.IsNullOrEmpty(currentPath) ? items[i].Name : $"{currentPath}/{items[i].Name}";
        
        if (items[i].ObjId == itemObjId)
        {
            CollectItemsToDelete(items[i], itemsToDelete, itemPath);
            items.RemoveAt(i);
            itemFound = true;
            return items;
        }
        
        if (items[i].IsFolder && items[i].ObjChildren != null)
        {
            items[i].ObjChildren = DeleteItemRecursive(items[i].ObjChildren, itemObjId, ref itemFound, itemsToDelete, itemPath);
            if (itemFound)
            {
                // If the folder is now empty after deletion, remove it
                if (items[i].ObjChildren.Count == 0)
                {
                    items.RemoveAt(i);
                }
                return items;
            }
        }
    }
    return items;
}

private void CollectItemsToDelete(UserDirectory item, List<string> itemsToDelete, string currentPath)
{
    if (item.IsFolder)
    {
        Console.WriteLine($"Adding folder: {currentPath}/");
        itemsToDelete.Add($"{currentPath}/");  
        if (item.ObjChildren != null)
        {
            foreach (var child in item.ObjChildren)
            {
                string childPath = $"{currentPath}/{child.Name}";
                CollectItemsToDelete(child, itemsToDelete, childPath);
            }
        }
    }
    else
    {
        Console.WriteLine($"Adding file: {currentPath}");
        itemsToDelete.Add(currentPath); 
    }
}

public async Task<bool> AddFile(string userId, string parentFolderObjId, UserDirectory newFile)
{
    var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, userId);
    var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

    if (user == null)
        return false;

    bool fileAdded = AddFileRecursive(user.Directory, parentFolderObjId, newFile);

    if (fileAdded)
    {
        var update = Builders<TopLevelUserObject>.Update.Set(u => u.Directory, user.Directory);
        var result = await _userCollection.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    return false;
}

private bool AddFileRecursive(List<UserDirectory> items, string parentFolderObjId, UserDirectory newFile)
{
    foreach (var item in items)
    {
        if (item.ObjId == parentFolderObjId && item.IsFolder)
        {
            item.ObjChildren ??= new List<UserDirectory>();
            item.ObjChildren.Add(newFile);
            return true;
        }

        if (item.IsFolder && item.ObjChildren != null)
        {
            if (AddFileRecursive(item.ObjChildren, parentFolderObjId, newFile))
                return true;
        }
    }

    return false;
}

public async Task<bool> CreateFolder(string userId, string parentFolderObjId, UserDirectory newFolder)
{
    newFolder.IsFolder = true;
    newFolder.ObjChildren = new List<UserDirectory>();

    var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, userId);
    var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

    if (user == null)
        return false;

    bool folderCreated = CreateFolderRecursive(user.Directory, parentFolderObjId, newFolder);

    if (folderCreated)
    {
        var update = Builders<TopLevelUserObject>.Update.Set(u => u.Directory, user.Directory);
        var result = await _userCollection.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    return false;
}

private bool CreateFolderRecursive(List<UserDirectory> items, string parentFolderObjId, UserDirectory newFolder)
{
    foreach (var item in items)
    {
        if (item.ObjId == parentFolderObjId && item.IsFolder)
        {
            item.ObjChildren ??= new List<UserDirectory>();
            item.ObjChildren.Add(newFolder);
            return true;
        }

        if (item.IsFolder && item.ObjChildren != null)
        {
            if (CreateFolderRecursive(item.ObjChildren, parentFolderObjId, newFolder))
                return true;
        }
    }

    return false;
}

public async Task<bool> RenameItem(string userId, string itemObjId, string newName)
{
    var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, userId);
    var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

    if (user == null)
        return false;

    bool itemRenamed = RenameItemRecursive(user.Directory, itemObjId, newName);

    if (itemRenamed)
    {
        var update = Builders<TopLevelUserObject>.Update.Set(u => u.Directory, user.Directory);
        var result = await _userCollection.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    return false;
}

private bool RenameItemRecursive(List<UserDirectory> items, string itemObjId, string newName)
{
    for (int i = 0; i < items.Count; i++)
    {
        if (items[i].ObjId == itemObjId)
        {
            items[i].Name = newName;
            return true;
        }

        if (items[i].IsFolder && items[i].ObjChildren != null)
        {
            if (RenameItemRecursive(items[i].ObjChildren, itemObjId, newName))
                return true;
        }
    }

    return false;
}

public async Task<bool> MoveItem(string userId, string itemObjId, string newParentFolderObjId)
{
    var filter = Builders<TopLevelUserObject>.Filter.Eq(u => u.UserId, userId);
    var user = await _userCollection.Find(filter).FirstOrDefaultAsync();

    if (user == null)
        return false;

    bool itemFound = false;
    UserDirectory movedItem = null;
    user.Directory = DeleteItemRecursiveAndReturn(user.Directory, itemObjId, ref itemFound, ref movedItem);

    if (itemFound && movedItem != null)
    {
        bool itemAdded = AddFileRecursive(user.Directory, newParentFolderObjId, movedItem);

        if (itemAdded)
        {
            var update = Builders<TopLevelUserObject>.Update.Set(u => u.Directory, user.Directory);
            var result = await _userCollection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }
    }

    return false;
}

private List<UserDirectory> DeleteItemRecursiveAndReturn(List<UserDirectory> items, string itemObjId, ref bool itemFound, ref UserDirectory deletedItem)
{
    for (int i = items.Count - 1; i >= 0; i--)
    {
        if (items[i].ObjId == itemObjId)
        {
            deletedItem = items[i];
            items.RemoveAt(i);
            itemFound = true;
            return items;
        }
        if (items[i].IsFolder && items[i].ObjChildren != null)
        {
            items[i].ObjChildren = DeleteItemRecursiveAndReturn(items[i].ObjChildren, itemObjId, ref itemFound, ref deletedItem);
            if (itemFound)
                return items;
        }
    }
    return items;
}

public string GenerateSignedUrl(string s3ObjectPath)
{
    var builder = WebApplication.CreateBuilder();
    string? cloudFrontUrl = Environment.GetEnvironmentVariable("CLOUDFRONT_URL") 
            ?? _configuration["CloudFront:Url"];
    
    if (string.IsNullOrEmpty(cloudFrontUrl))
        throw new ArgumentException("CloudFront URL is not configured");

    string url = $"https://{cloudFrontUrl}/{s3ObjectPath}";
    
    try
    {
        string privateKeyString = Environment.GetEnvironmentVariable("PRIVATE_KEY_TRADITIONAL") 
            ?? _configuration["CloudFront:PrivateKey"] 
            ?? throw new ArgumentNullException("Private key is not configured");

        using (StringReader privateKey = new StringReader(privateKeyString))
        {
            string? keyPairId = Environment.GetEnvironmentVariable("CLOUDFRONT_KEY_PAIR_ID") 
                ?? _configuration["CloudFront:KeyPairId"]
                ?? throw new ArgumentNullException("CloudFront KeyPairId is not configured");
                
            DateTime expirationTime = DateTime.UtcNow.AddMinutes(5);
            
            return AmazonCloudFrontUrlSigner.GetCannedSignedURL(
                url,
                privateKey,
                keyPairId,
                expirationTime
            );
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error generating signed URL: {ex.Message}");
        throw;
    }
}

public async Task UploadToS3(IFormFile fileContent, string fileKey)
{
    var builder = WebApplication.CreateBuilder();
    string? accesskey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY") 
            ?? _configuration["S3:AccessKey"];
    string? secretkey = Environment.GetEnvironmentVariable("S3_SECRET_KEY") 
            ?? _configuration["S3:Secret_Key"];
    string? bucketname = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") 
            ?? _configuration["S3:Bucket_Name"];
    RegionEndpoint? region = RegionEndpoint.USEast1;

    var s3Client = new AmazonS3Client(accesskey, secretkey, region);

        using var stream = fileContent.OpenReadStream();
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketname,
            Key = fileKey,
            InputStream = stream,
            ContentType = fileContent.ContentType
        };

        await s3Client.PutObjectAsync(putRequest);

    }
public async Task DeleteS3Object(string userId, string objForDeletion){
    var builder = WebApplication.CreateBuilder();
    string? accesskey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY") 
            ?? _configuration["S3:Access_Key"];
    string? secretkey = Environment.GetEnvironmentVariable("S3_SECRET_KEY") 
            ?? _configuration["S3:Secret_Key"];
    string? bucketname = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") 
            ?? _configuration["S3:Bucket_Name"];
    RegionEndpoint? region = RegionEndpoint.USEast1;

    string filekey = $"{userId}/{objForDeletion}";
    var s3Client = new AmazonS3Client(accesskey, secretkey, region);

        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = bucketname,
            Key = filekey,
        };

        await s3Client.DeleteObjectAsync(deleteRequest);
    
}

public async Task DeleteS3ObjectsBatch(string userId, List<string> itemPaths)
{
    var builder = WebApplication.CreateBuilder();
    string? accessKey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY") 
            ?? _configuration["S3:Access_Key"];
    string? secretKey = Environment.GetEnvironmentVariable("S3_SECRET_KEY") 
            ?? _configuration["S3:Secret_Key"];
    string? bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") 
            ?? _configuration["S3:Bucket_Name"];
    RegionEndpoint? region = RegionEndpoint.USEast1;

    Console.WriteLine($"Attempting to delete {itemPaths.Count} items for user {userId}");

    var s3Client = new AmazonS3Client(accessKey, secretKey, region);

    var objectsToDelete = new List<KeyVersion>();

    foreach (var itemPath in itemPaths)
    {
        string fullPath = $"{userId}/{itemPath.TrimStart('/')}";
        objectsToDelete.Add(new KeyVersion { Key = fullPath });
        Console.WriteLine($"Added for deletion: {fullPath}");
    }

    // Delete all collected objects in batches
    const int batchSize = 1000;  // S3 allows up to 1000 objects per delete request
    for (int i = 0; i < objectsToDelete.Count; i += batchSize)
    {
        var batch = objectsToDelete.Skip(i).Take(batchSize).ToList();
        var deleteRequest = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = batch
        };
        try
        {
            Console.WriteLine($"Sending delete request for batch {i / batchSize + 1}");
            var deleteResponse = await s3Client.DeleteObjectsAsync(deleteRequest);
            Console.WriteLine($"Deleted {deleteResponse.DeletedObjects.Count} objects in this batch");
            if (deleteResponse.DeleteErrors.Count > 0)
            {
                foreach (var error in deleteResponse.DeleteErrors)
                {
                    Console.WriteLine($"Error deleting object {error.Key}: {error.Code} - {error.Message}");
                }
            }
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"S3 Exception: {ex.Message}");
            Console.WriteLine($"Error Code: {ex.ErrorCode}");
            Console.WriteLine($"Request ID: {ex.RequestId}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
            throw;
        }
    }
    Console.WriteLine("S3 deletion process completed");
}


}

