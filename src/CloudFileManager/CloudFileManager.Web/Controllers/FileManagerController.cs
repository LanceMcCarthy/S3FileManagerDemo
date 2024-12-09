using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using CloudFileManager.Web.Helpers;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;

namespace CloudFileManager.Web.Controllers;

public class FileManagerController : Controller
{
    private readonly IAmazonS3 s3Client;
    private const string BucketName = "bkt-for-deployment";
    private const string SessionDirectory = "Dir";

    public FileManagerController(IConfiguration config)
    {
        var credentials = new Amazon.Runtime.BasicAWSCredentials(config["AWS_ACCESS_KEY_ID"], config["AWS_SECRET_ACCESS_KEY"]);
        s3Client = new AmazonS3Client(credentials, RegionEndpoint.USEast1);
    }

    // This is for creating a new folder in the s3 bucket
    public virtual async Task<ActionResult> CreateDirectory(string target, FileManagerEntry entry)
    {
        FileManagerEntry newEntry;

        // If the path is empty, we're creating a new folder
        if (string.IsNullOrEmpty(entry.Path))
        {
            //'New Folder/' Created
            newEntry = await S3CreateNewDirectoryAsync(target, entry);
        }
        else // Otherwise, we're copying an existing item
        {
            newEntry = await S3CopyItemAsync(target, entry);
        }

        UpdateSessionDir();

        return Json(newEntry);
    }

    // Return a list of files and folders at the desired path
    public virtual async Task<JsonResult> Read(string target)
    {
        try
        {
            var sessionDir = await S3ListContentsAsync(target);

            HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

            return Json(sessionDir);
        }
        catch (DirectoryNotFoundException)
        {
            throw new Exception("File Not Found");
        }
    }

    // rename a folder path or a filename
    public virtual async Task<ActionResult> Update(string target, FileManagerEntry entry)
    {
        FileManagerEntry updatedEntry = null;

        try
        {
            if (entry.IsDirectory) // rename a folder
            {
                updatedEntry = await S3RenameDirectoryAsync(target, entry);
            }
            else
            {
                updatedEntry = await S3RenameFileAsync(target, entry);
            }
        }
        catch (AmazonS3Exception e)
        {
            Console.WriteLine("Error encountered on server. Message:'{0}' when renaming an object", e.Message);
        }
        catch (Exception e)
        {
            Console.WriteLine("Unknown encountered on server. Message:'{0}' when renaming an object", e.Message);
        }

        return Json(updatedEntry);
    }

    // Deletes item at the desired path
    public virtual async Task<ActionResult> Destroy(FileManagerEntry entry)
    {
        var sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var currentEntry = sessionDir.FirstOrDefault(x => x.Path == entry.Path);

        await S3DeleteAsync(entry);

        sessionDir.Remove(currentEntry);
        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
        return Json(Array.Empty<object>());
    }

    // Uploads actual file data
    [AcceptVerbs("POST")]
    public virtual async Task<ActionResult> Upload(string path, IFormFile file)
    {
        var sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var newEntry = new FileManagerEntry();
        var newPath = path ?? "";

        try
        {
            // IMPORTANT!
            // We are saving a temporary file because AWS SDK doesn't support uploading from stream.
            var tempFilePath = Path.Combine(Path.GetTempPath(), file.FileName);
            await using var fileStream = System.IO.File.OpenWrite(tempFilePath);
            await file.CopyToAsync(fileStream);
            fileStream.Close();

            // Take the temp file and upload it to the s3 bucket
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = newPath + file.FileName,
                FilePath = tempFilePath,
            };

            var response = await s3Client.PutObjectAsync(request);

            if (response.HttpStatusCode == HttpStatusCode.OK)
            {
                newEntry.Name = file.FileName;
                newEntry.Path = Path.Combine(newPath, file.FileName);
                newEntry.Modified = DateTime.Now;
                newEntry.ModifiedUtc = DateTime.Now;
                newEntry.Created = DateTime.Now;
                newEntry.CreatedUtc = DateTime.UtcNow;
                newEntry.Size = file.Length;
                newEntry.Extension = Path.GetExtension(file.FileName);
                sessionDir.Add(newEntry);

                return Json(sessionDir);
            }
        }
        catch (Exception e)
        {
            throw new Exception("Forbidden");
        }

        return Forbid();
    }

    #region S3 Methods

    private async Task<List<FileManagerEntry>> S3ListContentsAsync(string directory)
    {
        var entries = new List<FileManagerEntry>();

        var request = new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = directory ?? "",
            Delimiter = "/"
        };

        ListObjectsV2Response response;

        do
        {
            response = await s3Client.ListObjectsV2Async(request);

            // List folders
            foreach (var commonPrefix in response.CommonPrefixes)
            {
                var lastFolderName = commonPrefix;

                if (commonPrefix.Contains('/'))
                {
                    var folders = commonPrefix.Split('/');

                    if (folders.Length is var count)
                    {
                        lastFolderName = count >= 2 ? folders[^2] : folders[^1];
                    }
                }

                // Add folder to list for the FileManager
                entries.Add(new FileManagerEntry
                {
                    Name = lastFolderName,
                    Path = commonPrefix,
                    IsDirectory = true,
                    HasDirectories = await CheckForSubdirectories(commonPrefix)
                });
            }

            // List files
            foreach (var s3Object in response.S3Objects)
            {
                var name = Path.GetFileNameWithoutExtension(s3Object.Key);

                //if folder has a name, add item.  Otherwise skip
                if (name == "")
                    continue;

                var entry = new FileManagerEntry
                {
                    Name = name,
                    Path = s3Object.Key,
                    Extension = Path.GetExtension(s3Object.Key),
                    IsDirectory = false,
                    HasDirectories = false,
                    Created = s3Object.LastModified,
                    CreatedUtc = DateTime.SpecifyKind(s3Object.LastModified, DateTimeKind.Utc),
                    Modified = s3Object.LastModified,
                    ModifiedUtc = DateTime.SpecifyKind(s3Object.LastModified, DateTimeKind.Utc),
                    Size = s3Object.Size
                };

                // If the item is a directory, update related properties
                if (s3Object.Key.Last() == '/')
                {
                    entry.IsDirectory = true;
                }

                // Add file to the list for FileManager
                entries.Add(entry);
            }

            // for do-while continuation
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated);

        return entries;
    }

    private async Task<FileManagerEntry> S3RenameDirectoryAsync(string target, FileManagerEntry entry)
    {
        string newPath = "";

        // Get a list of the current folder's objects
        var originalContentsResponse = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = entry.Path
        });

        // Iterate over the items and copy them into the new destination
        originalContentsResponse.S3Objects.ForEach(async (s3Object) =>
        {
            // copy of the Name so we do not modify the original fileManagerEntry object
            var comparer = entry.Name;

            // IMPORTANT:
            // The trailing slash is required for directories in S3
            if (entry.IsDirectory && entry.Name.Last() != '/') comparer = entry.Name + "/";

            // Simpler approach to create the new path by replacing the old name with the new name in the full key
            newPath = s3Object.Key.Replace(entry.Path, comparer);

            await s3Client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = BucketName,
                SourceKey = s3Object.Key,
                DestinationBucket = BucketName,
                DestinationKey = newPath
            });
        });

        // Cleanup phase - Delete original folder and all its contents
        //await s3Client.DeleteObjectAsync(BucketName, entry.Path);
        await S3DeleteAsync(entry);

        // Phase 3. Renaming FileManager data source
        // We have finished updating the S3 bucket, now we need to update the FileManagerEntry object with the new folder name and return it.
        entry.Path = entry.Path.Replace(entry.Path, entry.Name);

        // TODO This is a workaround to keep the same pattern of using trailing slashed in FileManager to have the same appearance as S3
        // This is not required, but needs ot be handled carefully.
        if (entry.IsDirectory && entry.Name.Last() != '/') entry.Path += "/";

        return entry;
    }

    private async Task<FileManagerEntry> S3RenameFileAsync(string target, FileManagerEntry entry)
    {
        string directory = Path.GetDirectoryName(entry.Path);
        string ext = entry.Extension ?? "";

        //Concat extension
        var newPath = NormalizePath(Path.Combine(directory, entry.Name + ext));

        // Phase 1. Copy the object to a new key
        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = BucketName,
            SourceKey = entry.Path,
            DestinationBucket = BucketName,
            DestinationKey = newPath
        };

        var copyResponse = await s3Client.CopyObjectAsync(copyRequest);

        if (copyResponse.HttpStatusCode != HttpStatusCode.OK)
        {
            throw new Exception("Error copying object in Update method.");
        }

        //// Phase 2. Delete the original object
        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = entry.Path
        };

        var deleteResponse = await s3Client.DeleteObjectAsync(deleteRequest);

        //delete original file object after rename successfully with 204
        if (deleteResponse.HttpStatusCode != HttpStatusCode.NoContent)
        {
            throw new AmazonS3Exception("Error deleting original object in Update method.");
        }

        // Phase 3. Renaming FileManager data source
        var sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var currentEntry = sessionDir.FirstOrDefault(x => x.Path == entry.Path);

        currentEntry.Name = entry.Name;
        currentEntry.Path = newPath;
        currentEntry.Extension = entry.Extension ?? "";

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        return currentEntry;
    }

    private async Task<FileManagerEntry> S3CreateNewDirectoryAsync(string target, FileManagerEntry entry)
    {
        // TODO This is a workaround to keep the same pattern of using trailing slashed in FileManager to have the same appearance as S3

        // This is not required, but needs ot be handled carefully.
        if (entry.IsDirectory && entry.Name.Last() != '/') entry.Name += "/";

        var newPath = Path.Combine(target ?? "", entry.Name);

        // Warning: If creating a new folder when the "New Folder" exists, it will overwrite The developer will need to rename for each initialization
        var putRequest = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = newPath,
        };

        var response = await s3Client.PutObjectAsync(putRequest);

        if (response.HttpStatusCode != HttpStatusCode.OK)
        {
            throw new Exception("Error creating directory... HttpStatusCode != HttpStatusCode.OK");
        }

        return new FileManagerEntry
        {
            Name = entry.Name,
            Path = newPath,
            IsDirectory = true,
            HasDirectories = false,
            Created = DateTime.Now,
            CreatedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc),
            Modified = DateTime.Now,
            ModifiedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc)
        };
    }

    private async Task<FileManagerEntry> S3CopyItemAsync(string target, FileManagerEntry entry)
    {
        string newPath = "";
        string directory = Path.GetDirectoryName(entry.Path);
        string ext = entry.Extension ?? "";

        //Concat extension
        newPath = NormalizePath(Path.Combine(target, entry.Name + ext));

        try
        {
            if (entry.IsDirectory)
            {
                // Get a list of the current folder's objects
                var originalContentsResponse = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = entry.Path
                });

                // Iterate over the items and copy them into the new destination
                originalContentsResponse.S3Objects.ForEach(async (s3Object) =>
                {
                    newPath = NormalizePath(Path.Combine(target, s3Object.Key));

                    await s3Client.CopyObjectAsync(new CopyObjectRequest
                    {
                        SourceBucket = BucketName,
                        SourceKey = s3Object.Key,
                        DestinationBucket = BucketName,
                        DestinationKey = newPath
                    });

                });

                //pass item which is copied
                return new FileManagerEntry
                {
                    Name = entry.Name,
                    Path = newPath,
                    Extension = entry.Extension,
                    IsDirectory = false,
                    HasDirectories = false,
                    Created = DateTime.Now,
                    CreatedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc),
                    Modified = DateTime.Now,
                    ModifiedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc),
                    Size = entry.Size
                };

            }
            else
            {

                var request = new CopyObjectRequest
                {
                    SourceBucket = BucketName,
                    SourceKey = entry.Path, //key
                    DestinationBucket = BucketName,
                    DestinationKey = newPath,  //where it is being saved
                };

                var response = await s3Client.CopyObjectAsync(request);

                if (response.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Error copying object... HttpStatusCode != HttpStatusCode.OK");
                }

                //pass item which is copied
                return new FileManagerEntry
                {
                    Name = entry.Name,
                    Path = newPath,
                    Extension = entry.Extension,
                    IsDirectory = false,
                    HasDirectories = false,
                    Created = DateTime.Now,
                    CreatedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc),
                    Modified = DateTime.Now,
                    ModifiedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc),
                    Size = entry.Size
                };
            }

        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"Error copying object: '{ex.Message}'");
            throw;
        }
    }

    private async Task S3DeleteAsync(FileManagerEntry entry)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = entry.Path
        };

        try
        {
            ListObjectsV2Response response;

            do
            {
                response = await s3Client.ListObjectsV2Async(request);

                response.S3Objects.ForEach(async (s3Object) =>
                {
                    try
                    {
                        await s3Client.DeleteObjectAsync(BucketName, s3Object.Key);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Could not delete {s3Object.Key}. Exception {e.Message}");
                    }
                });

                // for do-while continuation
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);


        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"Error deleting objects: {ex.Message}");
            throw new Exception("File Not Found");
        }
    }

    #endregion

    #region General Helpers

    private void UpdateSessionDir()
    {
        var sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory).ToList();

        foreach (var item in sessionDir.Where(d => d.IsDirectory))
        {
            item.HasDirectories = HasSubDirectories(item);
        }

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
    }

    private bool HasSubDirectories(FileManagerEntry entry)
    {
        var sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory)
            .Where(d => d.IsDirectory && d.Path != entry.Path).ToList();

        return sessionDir.Any(item => item.IsDirectory && item.Path.Contains(entry.Path));
    }

    private async Task<bool> CheckForSubdirectories(string keyPrefix)
    {
        var response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = keyPrefix,
            Delimiter = "/"
        });

        return response.CommonPrefixes.Count > 0;
    }

    protected virtual string NormalizePath(string path)
    {
        var newString = path.Replace('\\', '/');
        return newString;
    }
    #endregion
}