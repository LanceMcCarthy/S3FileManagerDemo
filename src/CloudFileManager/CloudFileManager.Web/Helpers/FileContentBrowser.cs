using Kendo.Mvc.UI;

namespace CloudFileManager.Web.Helpers;

public class FileContentBrowser
{
    public virtual IWebHostEnvironment HostingEnvironment { get; set; }
    public ICollection<FileManagerEntry> GetFiles(string path, string filter)
    {
        var directory = new DirectoryInfo(path);

        var extensions = (filter ?? "*").Split([", ", ",", "; ", ";"], System.StringSplitOptions.RemoveEmptyEntries);

        return extensions.SelectMany(directory.GetFiles)
            .Select(file => new FileManagerEntry
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                Size = file.Length,
                Path = file.FullName,
                Extension = file.Extension,
                IsDirectory = false,
                HasDirectories = false,
                Created = file.CreationTime,
                CreatedUtc = file.CreationTimeUtc,
                Modified = file.LastWriteTime,
                ModifiedUtc = file.LastWriteTimeUtc
            }).ToList();
    }

    public ICollection<FileManagerEntry> GetAll(string path)
    {
        var directory = new DirectoryInfo(path);
        var directories = directory.GetDirectories();
        var files = directory.GetFiles();
        var result = files.Select(item => GetFile(item.FullName)).ToList();

        foreach (var item in directories)
        {
            result.Add(GetDirectory(item.FullName));

            if (item.GetDirectories().Length > 0 || item.GetFiles().Length > 0)
            {
                result.AddRange(GetAll(item.FullName));
            }
        }

        return result;
    }

    public ICollection<FileManagerEntry> GetDirectories(string path)
    {
        var directory = new DirectoryInfo(path);

        return directory.GetDirectories()
            .Select(subDirectory => new FileManagerEntry
            {
                Name = subDirectory.Name,
                Path = subDirectory.FullName,
                Extension = subDirectory.Extension,
                IsDirectory = true,
                HasDirectories = subDirectory.GetDirectories().Length > 0,
                Created = subDirectory.CreationTime,
                CreatedUtc = subDirectory.CreationTimeUtc,
                Modified = subDirectory.LastWriteTime,
                ModifiedUtc = subDirectory.LastWriteTimeUtc
            }).ToList();
    }

    public FileManagerEntry GetDirectory(string path)
    {
        var directory = new DirectoryInfo(path);

        return new FileManagerEntry
        {
            Name = directory.Name,
            Path = directory.FullName,
            Extension = directory.Extension,
            IsDirectory = true,
            HasDirectories = directory.GetDirectories().Length > 0,
            Created = directory.CreationTime,
            CreatedUtc = directory.CreationTimeUtc,
            Modified = directory.LastWriteTime,
            ModifiedUtc = directory.LastWriteTimeUtc
        };
    }

    public FileManagerEntry GetFile(string path)
    {
        var file = new FileInfo(path);

        return new FileManagerEntry
        {
            Name = Path.GetFileNameWithoutExtension(file.Name),
            Path = file.FullName,
            Size = file.Length,
            Extension = file.Extension,
            IsDirectory = false,
            HasDirectories = false,
            Created = file.CreationTime,
            CreatedUtc = file.CreationTimeUtc,
            Modified = file.LastWriteTime,
            ModifiedUtc = file.LastWriteTimeUtc
        };
    }
}