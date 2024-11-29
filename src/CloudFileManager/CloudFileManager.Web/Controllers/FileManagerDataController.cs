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

public class FileManagerDataController : Controller
{
    protected readonly IWebHostEnvironment HostingEnvironment;
    private readonly IAmazonS3 s3Client;
    private const string BucketName = "bkt-for-deployment";
    private const string SessionDirectory = "Dir";

    public FileManagerDataController(IWebHostEnvironment hostingEnvironment)
    {
        HostingEnvironment = hostingEnvironment;

        s3Client = AuthorizeAmazonS3Client();
    }

    #region FileManager Actions

    // This is for creating a new folder in the s3 bucket
    public virtual async Task<ActionResult> CreateDirectory(string target, FileManagerEntry entry)
    {
        FileManagerEntry newEntry;

        // If the path is empty, we're creating a new folder
        if (string.IsNullOrEmpty(entry.Path))
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = target,
                ContentBody = string.Empty
            };

            var response = await s3Client.PutObjectAsync(putRequest);

            if (response.HttpStatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Error creating directory... HttpStatusCode != HttpStatusCode.OK");
            }

            newEntry = new FileManagerEntry
            {
                Name = entry.Name,
                Path = target,
                IsDirectory = true,
                HasDirectories = false,
                Created = DateTime.Now,
                CreatedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc),
                Modified = DateTime.Now,
                ModifiedUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc)
            };
        }
        // Otherwise, we're copying an existing item
        else
        {
            try
            {
                var request = new CopyObjectRequest
                {
                    SourceBucket = BucketName,
                    SourceKey = Path.Join(entry.Path, entry.Name, entry.Extension),
                    DestinationBucket = BucketName,
                    DestinationKey = target,
                };

                var response = await s3Client.CopyObjectAsync(request);

                if (response.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Error copying object... HttpStatusCode != HttpStatusCode.OK");
                }

                newEntry = new FileManagerEntry
                {
                    Name = entry.Name,
                    Path = target,
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
            catch (AmazonS3Exception ex)
            {
                Console.WriteLine($"Error copying object: '{ex.Message}'");
                throw;
            }
        }

        UpdateSessionDir();

        return Json(newEntry);
    }

    // Return a list of files and folders at the desired path
    public virtual async Task<JsonResult> Read(string target)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);

        try
        {
            if (sessionDir == null)
            {
                sessionDir = new List<FileManagerEntry>();

                var request = new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = "",
                    Delimiter = "/"
                };

                ListObjectsV2Response response;

                do
                {
                    response = await s3Client.ListObjectsV2Async(request);

                    // List folders
                    foreach (string commonPrefix in response.CommonPrefixes)
                    {
                        Debug.WriteLine("Folder: " + commonPrefix);

                        // Add folder to list for the FileManager
                        sessionDir.Add(new FileManagerEntry
                        {
                            Name = commonPrefix,
                            Path = commonPrefix,
                            IsDirectory = true,
                            //HasDirectories = ? not sure how to implement this yet, do we check for delimiter?
                        });
                    }

                    // List files
                    foreach (var s3Object in response.S3Objects)
                    {
                        Debug.WriteLine("File: " + s3Object.Key);

                        // Add file to the list for FileManager
                        sessionDir.Add(new FileManagerEntry
                        {
                            Name = s3Object.Key,
                            Path = s3Object.Key,
                            Extension = Path.GetExtension(s3Object.Key),
                            IsDirectory = false,
                            HasDirectories = false,
                            Created = s3Object.LastModified,
                            CreatedUtc = DateTime.SpecifyKind(s3Object.LastModified, DateTimeKind.Utc),
                            Modified = s3Object.LastModified,
                            ModifiedUtc = DateTime.SpecifyKind(s3Object.LastModified, DateTimeKind.Utc),
                            Size = s3Object.Size
                        });
                    }

                    // If the response is truncated, set the request ContinuationToken from the NextContinuationToken property of the response.
                    request.ContinuationToken = response.NextContinuationToken;
                }
                while (response.IsTruncated);

                HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
            }

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
        try
        {
            // Phase 1. Copy the object to a new key
            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = BucketName,
                SourceKey = entry.Path,
                DestinationBucket = BucketName,
                DestinationKey = target
            };

            var copyResponse = await s3Client.CopyObjectAsync(copyRequest);

            if (copyResponse.HttpStatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Error copying object in Update method.");
            }

            // Phase 2. Delete the original object
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = entry.Path
            };

            var deleteResponse = await s3Client.DeleteObjectAsync(deleteRequest);

            if (deleteResponse.HttpStatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Error deleting original object in Update method.");
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


        // Phase 3. Renaming FileManager Item

        var sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var currentEntry = sessionDir.FirstOrDefault(x => x.Path == entry.Path);

        currentEntry.Name = entry.Name;
        currentEntry.Path = entry.Path;
        currentEntry.Extension = entry.Extension ?? "";

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        return Json(currentEntry);
    }

    // Deletes item at the desired path
    public virtual async Task<ActionResult> Destroy(FileManagerEntry entry)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var currentEntry = sessionDir.FirstOrDefault(x => x.Path == entry.Path);

        var request = new ListObjectsV2Request
        {
            BucketName = BucketName,
        };

        try
        {
            ListObjectsV2Response response;

            do
            {
                response = await s3Client.ListObjectsV2Async(request);

                response.S3Objects.ForEach(async obj => await s3Client.DeleteObjectAsync(BucketName, obj.Key));

                // If the response is truncated, set the request ContinuationToken from the NextContinuationToken property of the response.
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);

            sessionDir.Remove(currentEntry);
            HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
            return Json(new object[0]);
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"Error deleting objects: {ex.Message}");
            throw new Exception("File Not Found");
        }
    }

    // Uploads actual file data
    [AcceptVerbs("POST")]
    public virtual async Task<ActionResult> Upload(string path, IFormFile file)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        FileManagerEntry newEntry = new FileManagerEntry();

        // this was using an extensionless file name
        //var fileName = Path.GetFileNameWithoutExtension(file.FileName);

        try
        {
            // Saving a temporary file (because AWS SDK doesnt support stream)
            var tempFilePath = Path.Combine(Path.GetTempPath(), file.FileName);
            await using var fileStream = System.IO.File.OpenWrite(tempFilePath);
            await file.CopyToAsync(fileStream);
            fileStream.Close();

            // Take the temp file and upload it to the s3 bucket
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = path,
                FilePath = tempFilePath,
            };

            var response = await s3Client.PutObjectAsync(request);

            if (response.HttpStatusCode == HttpStatusCode.OK)
            {
                newEntry.Path = Path.Combine(path, file.FileName);
                newEntry.Name = file.FileName;
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

    #endregion


    #region Helpers

    private AmazonS3Client AuthorizeAmazonS3Client()
    {
        var sharedFile = new SharedCredentialsFile();

        if (sharedFile.TryGetProfile("Default", out var basicProfile) && AWSCredentialsFactory.TryGetAWSCredentials(basicProfile, sharedFile, out var awsCredentials))
        {
            return new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
        }

        throw new Exception("Could not authorize Amazon S3 client");
    }

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

    #endregion
}