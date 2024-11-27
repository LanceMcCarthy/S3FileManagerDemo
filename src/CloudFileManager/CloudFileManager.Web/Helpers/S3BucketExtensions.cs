using Amazon.S3.Model;
using Amazon.S3;

namespace CloudFileManager.Web.Helpers;

public class S3BucketExtensions
{
    public static async Task<bool> CreateBucketAsync(IAmazonS3 client, string bucketName)
    {
        try
        {
            var request = new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true,
            };

            var response = await client.PutBucketAsync(request);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"Error creating bucket: '{ex.Message}'");
            return false;
        }
    }

    // Moved to controller
    //public static async Task<bool> UploadFileAsync(
    //    IAmazonS3 client,
    //    string bucketName,
    //    string objectName,
    //    string filePath)
    //{
    //    var request = new PutObjectRequest
    //    {
    //        BucketName = bucketName,
    //        Key = objectName,
    //        FilePath = filePath
    //    };

    //    var response = await client.PutObjectAsync(request);

    //    if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
    //    {
    //        Console.WriteLine($"Successfully uploaded {objectName} to {bucketName}.");
    //        return true;
    //    }

    //    return false;
    //}

    public static async Task<bool> CopyObjectInBucketAsync(
        IAmazonS3 client,
        string bucketName,
        string objectName,
        string folderName)
    {
        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = objectName,
                DestinationBucket = bucketName,
                DestinationKey = $"{folderName}\\{objectName}",
            };
            var response = await client.CopyObjectAsync(request);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"Error copying object: '{ex.Message}'");
            return false;
        }
    }

    // Momved to controller
    //public static async Task<bool> ListBucketContentsAsync(IAmazonS3 client, string bucketName)
    //{
    //    try
    //    {
    //        var request = new ListObjectsV2Request
    //        {
    //            BucketName = bucketName,
    //            MaxKeys = 5,
    //        };

    //        Console.WriteLine("--------------------------------------");
    //        Console.WriteLine($"Listing the contents of {bucketName}:");
    //        Console.WriteLine("--------------------------------------");

    //        ListObjectsV2Response response;

    //        do
    //        {
    //            response = await client.ListObjectsV2Async(request);

    //            response.S3Objects
    //                .ForEach(obj => Console.WriteLine($"{obj.Key,-35}{obj.LastModified.ToShortDateString(),10}{obj.Size,10}"));

    //            // If the response is truncated, set the request ContinuationToken
    //            // from the NextContinuationToken property of the response.
    //            request.ContinuationToken = response.NextContinuationToken;
    //        }
    //        while (response.IsTruncated);

    //        return true;
    //    }
    //    catch (AmazonS3Exception ex)
    //    {
    //        Console.WriteLine($"Error encountered on server. Message:'{ex.Message}' getting list of objects.");
    //        return false;
    //    }
    //}

    public static async Task<bool> DeleteBucketContentsAsync(IAmazonS3 client, string bucketName)
    {
        // Iterate over the contents of the bucket and delete all objects.
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
        };

        try
        {
            ListObjectsV2Response response;

            do
            {
                response = await client.ListObjectsV2Async(request);
                response.S3Objects
                    .ForEach(async obj => await client.DeleteObjectAsync(bucketName, obj.Key));

                // If the response is truncated, set the request ContinuationToken
                // from the NextContinuationToken property of the response.
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);

            return true;
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"Error deleting objects: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> DeleteBucketAsync(IAmazonS3 client, string bucketName)
    {
        var request = new DeleteBucketRequest
        {
            BucketName = bucketName,
        };

        var response = await client.DeleteBucketAsync(request);
        return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
    }
}
