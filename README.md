# S3FileManagerDemo
A demo app on how to connect Telerik UI for ASP.NET Core FileManager control to an Amazon S3 service.

## Setup
### Prerequisite: AWS SDK Credentials file

You must have a credentials file present when using the S3 SDK. For your convenience, here are the steps:

- Phase 1. Create user credentials (if you already have AWS credentials, skip to phase 2)
    1. Sign in to the AWS Management Console and open the IAM console at [https://console.aws.amazon.com/iam/](https://console.aws.amazon.com/iam/).
    1. Create a new user with permissions limited to the services and actions that you want your code to have access to. For more information about creating a new user, see [Creating IAM users (Console)](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_users_create.html#id_users_create_console), and follow the instructions **through step 8**.
    1. Choose Download .csv to save a local copy of your AWS credentials.
- Phase 2. Set dotnet user-secrets 
    1. Open a terminal (powershell, bash, etc) and navigate to the `src/CloudFileManager/CloudFileManager.Web/` directory
    1. Run the following commands
        - `dotnet user-secrets set "AWS_ACCESS_KEY_ID" "value-from-cvs-file"`
        - `dotnet user-secrets set "AWS_SECRET_ACCESS_KEY" "value-from-cvs-file"`
    1. Done. Those secrets are used by **FileManagerController.cs Line 21** to authenticate the **AmazonS3Client**.

### Build and Deploy the Project

 1. Now, you can do a Rebuild of the project and deploy it
 2. You should see the following appearance (will vary according to your bucket's contents).

![image](https://github.com/user-attachments/assets/151d841d-079e-4bd2-9419-0241861278da)


### Additional Support

This is a conceptual project and carries no guarantee. There may be some functionality that needs to be further refined (nested folder renaming, etc). This is the responsibility of the implementer to adjust to the cloud API, as well as what is returned to the FileManager. For example, using the FileManagerEntry obejct with the expected Path value (contains path delimiter) and the Name value (only name, no path delimiter).

For technical assistance, choose the relevant option

Telerik or Kendo components
ASP.NET Core - [Live chat with the .NET Discord](http://aka.ms/dotnet-discord) (or other resources [here](https://dotnet.microsoft.com/en-us/platform/community)).
AWS S3 - [resources](https://docs.aws.amazon.com/sdk-for-net/)

If you like a complete solution developed for you, this can be arranged by the Professional Services team, whom may be [contacted here](https://www.telerik.com/services) or through your sales representative.
