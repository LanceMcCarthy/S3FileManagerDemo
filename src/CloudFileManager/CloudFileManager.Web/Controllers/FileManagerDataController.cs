using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using CloudFileManager.Web.Helpers;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace CloudFileManager.Web.Controllers;

public class FileManagerDataController : Controller
{
    private IAmazonS3 client;
    private const string BUCKET_NAME = "bkt-for-deployment";
    private const string KEY_NAME = "aws";

    protected readonly IWebHostEnvironment HostingEnvironment;
    private readonly FileContentBrowser directoryBrowser;
    private const string contentFolderRoot = "shared";
    private const string prettyName = "Folders";
    private static readonly string[] foldersToCopy = new[] { "shared/filemanager" };
    private const string SessionDirectory = "Dir";
    private const string DefaultTarget = "Folders";

    public string ContentPath => Path.Combine(HostingEnvironment.WebRootPath, contentFolderRoot, "filemanager");

    public FileManagerDataController(IWebHostEnvironment hostingEnvironment)
    {
        HostingEnvironment = hostingEnvironment;
        directoryBrowser = new FileContentBrowser();

        var sharedFile = new SharedCredentialsFile();

        if (sharedFile.TryGetProfile("Default", out var basicProfile) && AWSCredentialsFactory.TryGetAWSCredentials(basicProfile, sharedFile, out var awsCredentials))
        {
            client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
        }
    }

    #region Direct Controller Actions

    // This is for creating a new folder in the s3 bucket
    public virtual ActionResult Create(string target, FileManagerEntry entry)
    {
        FileManagerEntry newEntry;

        if (!Authorize(NormalizePath(target)))
        {
            throw new Exception("Forbidden");
        }

        newEntry = String.IsNullOrEmpty(entry.Path)
            ? CreateNewFolder(target, entry)
            : CopyEntry(target, entry);

        UpdateSessionDir();

        return Json(VirtualizePath(newEntry));
    }

    // Return a list of files and folders at the desired path
    public virtual JsonResult Read(string target)
    {
        var path = NormalizePath(target);
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);

        if (Authorize(path))
        {
            try
            {
                if (sessionDir == null)
                {
                    sessionDir = directoryBrowser.GetAll(ContentPath);

                    HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
                }

                var result = sessionDir.Where(d => TargetMatch(target, d.Path)).Select(VirtualizePath).ToList();

                return Json(result.ToArray());
            }
            catch (DirectoryNotFoundException)
            {
                throw new Exception("File Not Found");
            }
        }

        throw new Exception("Forbidden");
    }

    // rename a folder path or a filename
    public virtual ActionResult Update(string target, FileManagerEntry entry)
    {
        FileManagerEntry newEntry;
        var path = Path.Combine(ContentPath, NormalizePath(entry.Path));

        if (!Authorize(NormalizePath(entry.Path)) && !Authorize(NormalizePath(target)))
        {
            throw new Exception("Forbidden");
        }

        newEntry = RenameEntry(entry);

        return Json(VirtualizePath(newEntry));
    }

    // Deletes item at the desired path
    public virtual ActionResult Destroy(FileManagerEntry entry)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var path = Path.Combine(ContentPath, NormalizePath(entry.Path));
        var currentEntry = sessionDir.FirstOrDefault(x => x.Path == path);

        if (currentEntry != null)
        {
            sessionDir.Remove(currentEntry);
            HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
            return Json(new object[0]);
        }

        throw new Exception("File Not Found");
    }

    // Uploads actual file data
    [AcceptVerbs("POST")]
    public virtual async Task<ActionResult> Upload(string path, IFormFile file)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        FileManagerEntry newEntry = new FileManagerEntry();
        path = NormalizePath(path);
        var fileName = Path.GetFileNameWithoutExtension(file.FileName);

        // Saving a temporary file (because AWS SDK doesnt support stream)
        var tempFilePath = Path.Combine(Path.GetTempPath(), file.FileName);
        await using var fileStream = System.IO.File.OpenWrite(tempFilePath);
        await file.CopyToAsync(fileStream);
        fileStream.Close();

        // Take the temp file and upload it to the s3 bucket
        var success = await S3BucketExtensions.UploadFileAsync(client, BUCKET_NAME, KEY_NAME, tempFilePath);

        if (success)
        {
            newEntry.Path = Path.Combine(ContentPath, path, file.FileName);
            newEntry.Name = fileName;
            newEntry.Modified = DateTime.Now;
            newEntry.ModifiedUtc = DateTime.Now;
            newEntry.Created = DateTime.Now;
            newEntry.CreatedUtc = DateTime.UtcNow;
            newEntry.Size = file.Length;
            newEntry.Extension = Path.GetExtension(file.FileName);
            sessionDir.Add(newEntry);

            // ? dont know what we should return to the FileManager in this case.
            return Json(VirtualizePath(newEntry));
        }
        else
        {
            throw new Exception("Forbidden");
        }
        
        //if (AuthorizeUpload(path, file))
        //{
        //    newEntry.Path = Path.Combine(ContentPath, path, file.FileName);
        //    newEntry.Name = fileName;
        //    newEntry.Modified = DateTime.Now;
        //    newEntry.ModifiedUtc = DateTime.Now;
        //    newEntry.Created = DateTime.Now;
        //    newEntry.CreatedUtc = DateTime.UtcNow;
        //    newEntry.Size = file.Length;
        //    newEntry.Extension = Path.GetExtension(file.FileName);
        //    sessionDir.Add(newEntry);

        //    HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        //    return Json(VirtualizePath(newEntry));
        //}

        //throw new Exception("Forbidden");
    }

    #endregion
    
    #region Original code that uses the web server's file system

    public string Filter => "*.*";

    private string CreateUserFolder()
    {
        var virtualPath = Path.Combine(contentFolderRoot, "UserFiles", prettyName);
        var path = HostingEnvironment.WebRootFileProvider.GetFileInfo(virtualPath).PhysicalPath;

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            foreach (var sourceFolder in foldersToCopy)
            {
                CopyFolder(HostingEnvironment.WebRootFileProvider.GetFileInfo(sourceFolder).PhysicalPath, path);
            }
        }
        return virtualPath;
    }

    private void CopyFolder(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(destination);
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var dest = Path.Combine(destination, Path.GetFileName(file));
            System.IO.File.Copy(file, dest);
        }

        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            var dest = Path.Combine(destination, Path.GetFileName(folder));
            CopyFolder(folder, dest);
        }
    }

    public virtual bool Authorize(string path)
    {
        return CanAccess(path);
    }

    protected virtual bool CanAccess(string path)
    {
        var rootPath = Path.GetFullPath(ContentPath);
        return path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
    }

    protected virtual string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ContentPath;
        }
        else
        {
            return CombinePaths(ContentPath, path.Split("/"));
        }
    }

    protected virtual FileManagerEntry VirtualizePath(FileManagerEntry entry)
    {
        return new FileManagerEntry
        {
            Created = entry.Created,
            CreatedUtc = entry.CreatedUtc,
            Extension = entry.Extension,
            HasDirectories = entry.HasDirectories,
            IsDirectory = entry.IsDirectory,
            Modified = entry.Modified,
            ModifiedUtc = entry.ModifiedUtc,
            Name = entry.Name,
            Path = entry.Path.Replace(ContentPath + Path.DirectorySeparatorChar, "").Replace(@"\", "/"),
            Size = entry.Size
        };
    }

    protected virtual FileManagerEntry CopyEntry(string target, FileManagerEntry entry)
    {
        var path = NormalizePath(entry.Path);
        var physicalPath = path;
        var physicalTarget = EnsureUniqueName(NormalizePath(target), entry);

        FileManagerEntry newEntry;

        newEntry = entry.IsDirectory
            ? CopyDirectory(new DirectoryInfo(physicalPath), Directory.CreateDirectory(physicalTarget))
            : CopyFile(physicalPath, physicalTarget);

        return newEntry;
    }

    private FileManagerEntry CopyFile(string source, string target)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var entry = sessionDir.FirstOrDefault(x => x.Path == source);

        entry.Path = target;

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        return entry;
    }

    public FileManagerEntry CopyDirectory(DirectoryInfo source, DirectoryInfo target)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var currentEntry = sessionDir.FirstOrDefault(x => x.Path == source.FullName);

        currentEntry.Path = target.FullName;

        foreach (FileInfo fi in source.GetFiles())
        {
            Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
            CopyFile(fi.FullName, Path.Combine(target.FullName, fi.Name));
        }

        // Copy each subdirectory using recursion.
        foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
        {
            DirectoryInfo nextTargetSubDir =
                target.CreateSubdirectory(diSourceSubDir.Name);
            CopyDirectory(diSourceSubDir, nextTargetSubDir);
        }

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        return currentEntry;
    }

    public FileManagerEntry CreateNewFolder(string target, FileManagerEntry entry)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        var path = NormalizePath(target);
        string physicalPath = EnsureUniqueName(path, entry);

        entry.Path = physicalPath;
        entry.Created = DateTime.Now;
        entry.CreatedUtc = DateTime.UtcNow;
        entry.Modified = DateTime.Now;
        entry.ModifiedUtc = DateTime.UtcNow;
        sessionDir.Add(entry);

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        return entry;
    }

    protected virtual string EnsureUniqueName(string target, FileManagerEntry entry)
    {
        var tempName = entry.Name + entry.Extension;
        int sequence = 0;
        var physicalTarget = Path.Combine(NormalizePath(target), tempName);

        if (!Authorize(NormalizePath(physicalTarget)))
        {
            throw new Exception("Forbidden");
        }

        if (entry.IsDirectory)
        {
            while (Directory.Exists(physicalTarget))
            {
                tempName = entry.Name + $"({++sequence})";
                physicalTarget = Path.Combine(NormalizePath(target), tempName);
            }
        }
        else
        {
            while (System.IO.File.Exists(physicalTarget))
            {
                tempName = entry.Name + $"({++sequence})" + entry.Extension;
                physicalTarget = Path.Combine(NormalizePath(target), tempName);
            }
        }

        return physicalTarget;
    }

    protected virtual void DeleteFile(string path)
    {
        if (!Authorize(path))
        {
            throw new Exception("Forbidden");
        }

        var physicalPath = NormalizePath(path);

        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }

    protected virtual void DeleteDirectory(string path)
    {
        if (!Authorize(path))
        {
            throw new Exception("Forbidden");
        }

        var physicalPath = NormalizePath(path);

        if (Directory.Exists(physicalPath))
        {
            Directory.Delete(physicalPath, true);
        }
    }

    protected virtual FileManagerEntry RenameEntry(FileManagerEntry entry)
    {
        var path = NormalizePath(entry.Path);
        var physicalPath = path;
        var physicalTarget = EnsureUniqueName(Path.GetDirectoryName(path), entry);
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);
        FileManagerEntry currentEntry = sessionDir.FirstOrDefault(x => x.Path == physicalPath);

        currentEntry.Name = entry.Name;
        currentEntry.Path = physicalTarget;
        currentEntry.Extension = entry.Extension ?? "";

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);

        return currentEntry;
    }

    public virtual bool AuthorizeUpload(string path, IFormFile file)
    {
        if (!CanAccess(path))
        {
            throw new DirectoryNotFoundException(String.Format("The specified path cannot be found - {0}", path));
        }

        if (!IsValidFile(GetFileName(file)))
        {
            throw new InvalidDataException(String.Format("The type of file is not allowed. Only {0} extensions are allowed.", Filter));
        }

        return true;
    }

    private bool IsValidFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var allowedExtensions = Filter.Split(',');

        return allowedExtensions.Any(e => e.Equals("*.*") || e.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }


    protected virtual void SaveFile(IFormFile file, string pathToSave)
    {
        try
        {
            var path = Path.Combine(pathToSave, GetFileName(file));
            using var stream = System.IO.File.Create(path);
            file.CopyTo(stream);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    public virtual string GetFileName(IFormFile file)
    {
        var fileContent = ContentDispositionHeaderValue.Parse(file.ContentDisposition);
        return Path.GetFileName(fileContent.FileName.ToString().Trim('"'));
    }

    private bool TargetMatch(string target, string path)
    {
        var targetFullPath = Path.Combine(ContentPath, NormalizePath(target));
        var parentPath = Directory.GetParent(path).FullName;

        return targetFullPath.Trim('\\') == parentPath.Trim('\\');
    }

    private void UpdateSessionDir()
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory);

        foreach (var item in sessionDir.Where(d => d.IsDirectory).ToList())
        {
            item.HasDirectories = HasSubDirectories(item);
        }

        HttpContext.Session.SetObjectAsJson(SessionDirectory, sessionDir);
    }

    private bool HasSubDirectories(FileManagerEntry entry)
    {
        ICollection<FileManagerEntry> sessionDir = HttpContext.Session.GetObjectFromJson<ICollection<FileManagerEntry>>(SessionDirectory)
            .Where(d => d.IsDirectory && d.Path != entry.Path).ToList();

        foreach (var item in sessionDir)
        {
            if (item.IsDirectory && item.Path.Contains(entry.Path))
            {
                return true;
            }
        }

        return false;
    }

    private string CombinePaths(string path, params string[] paths)
    {
        if (path == null)
        {
            throw new ArgumentNullException("path1");
        }
        if (paths == null)
        {
            throw new ArgumentNullException("paths");
        }
        return paths.Aggregate(path, (acc, p) => Path.Combine(acc, p));
    }
    #endregion
}