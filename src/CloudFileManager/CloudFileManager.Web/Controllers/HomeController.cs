using CloudFileManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;


namespace CloudFileManager.Web.Controllers;

public class HomeController(IConfiguration config) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!string.IsNullOrEmpty(context.HttpContext.Request.Query["culture"]))
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(context.HttpContext.Request.Query["culture"]);
        }
        base.OnActionExecuting(context);
    }

    public IActionResult Index()
    {
        var s3Config = new S3Config
        {
            Uuid = Guid.NewGuid(),
            ExpirationTime = TimeSpan.FromHours(1),
            AccessKey = config["AWS_ACCESS_KEY"],
            SecretAccessKey = config["AWS_SECRET_ACCESS_KEY"],

            // Bucket name and key prefix (folder)
            Bucket = "bkt-for-deployment/Aws/",
            BucketUrl = "https://bkt-for-deployment.s3.us-east-1.amazonaws.com/",
            KeyPrefix = "Aws/",
            Acl = "private",

            // This might need to be adjusted
            ContentTypePrefix = "application/octet-stream",

            SuccessUrl = "http://localhost:18223/home/success"
        };

        ViewBag.Policy = Policy(s3Config);
        ViewBag.PolicySignature = Sign(ViewBag.Policy, s3Config.SecretAccessKey);
        ViewBag.S3Config = s3Config;

        return View();
    }

    private string Policy(S3Config config)
    {
        var policyJson = JsonConvert.SerializeObject(new
        {
            expiration = DateTime.UtcNow.Add(config.ExpirationTime).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            conditions = new object[] {
                new { bucket = config.Bucket },
                new [] { "starts-with", "$key", config.KeyPrefix },
                new { acl = config.Acl },
                new [] { "starts-with", "$success_action_redirect", "" },
                new [] { "starts-with", "$Content-Type", config.ContentTypePrefix },
                new Dictionary<string, string>  {{ "x-amz-meta-uuid", config.Uuid.ToString() }} 
            }
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(policyJson));
    }

    private static string Sign(string text, string key)
    {
        var signer = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(signer.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }





    public IActionResult About()
    {
        ViewData["Message"] = "Your application description page.";

        return View();
    }

    public IActionResult Contact()
    {
        ViewData["Message"] = "Your contact page.";

        return View();
    }

    public IActionResult Error()
    {
        return View();
    }
}