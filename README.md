# S3FileManagerDemo
A demo app on how to connect Telerik UI for ASP.NET Core FileManager control to an Amazon S3 service.

## Setup
### Prerequisite: AWS SDK Credentials file

You must have a credentials file present when using the S3 SDK. For your convenience, here are the steps:

- Phase 1. Create user credentials (if you already have AWS credentials, skip to phase 2)
    1. Sign in to the AWS Management Console and open the IAM console at [https://console.aws.amazon.com/iam/](https://console.aws.amazon.com/iam/).
    1. Create a new user with permissions limited to the services and actions that you want your code to have access to. For more information about creating a new user, see [Creating IAM users (Console)](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_users_create.html#id_users_create_console), and follow the instructions **through step 8**.
    1. Choose "Download .csv" to save a local copy of your AWS credentials.
- Phase 2. Set dotnet user-secrets 
    1. Open a terminal (powershell, bash, etc) and navigate to the `src/CloudFileManager/CloudFileManager.Web/` directory
    1. Run the following commands to save two secrets (used by [FileManagerController.cs:21](https://github.com/LanceMcCarthy/S3FileManagerDemo/blob/2782d900a930002d786643d0e72cfc452b6a7a71/src/CloudFileManager/CloudFileManager.Web/Controllers/FileManagerController.cs#L19))
        - `dotnet user-secrets set "AWS_ACCESS_KEY_ID" "value-from-cvs-file"`
        - `dotnet user-secrets set "AWS_SECRET_ACCESS_KEY" "value-from-cvs-file"`
- Phase 3. Build and Deploy
    1. Open the solution in Visual Studio 2022 and open the `FileManagerController.cs` file
    1. On Line 14, change the `BucketName` value from **bkt-for-deployment** to the name of your S3 bucket. 
    1. Right-click on the solution and select **Rebuild**
        - If you get a "missing Telerik NuGet package" error, confirm you have Telerik NuGet server as a package source ([instructions](https://docs.telerik.com/aspnet-core/installation/nuget-install#setup-with-the-nuget-package-manager)).
    1. Start debugging to launch the application
    
Observe the S3 bucket's contents in the FileManager component:

![image](https://github.com/user-attachments/assets/151d841d-079e-4bd2-9419-0241861278da)


### Technical Support

This is a conceptual demo project and carries no guarantees. There may be some functionality that can be further refined (e.g., renaming nested folders). It is the responsibility of the implementer of this demo to both A) adjust to their cloud API's requirements, and B) meet the Telerik FileManager's requirements.

For technical assistance, choose the relevant option:

- Telerik or Kendo API questions
    - [Support Resources (live demos, docs, forums etc)](https://www.telerik.com/support/aspnet-core)
    - [Open a Technical Support Ticket](https://www.telerik.com/account/support-center/contact-us/)
- ASP.NET Core questions:
    - [Live chat with the .NET Discord](http://aka.ms/dotnet-discord)
    - find a lot more resources [here](https://dotnet.microsoft.com/en-us/platform/community)
- AWS S3 questions:
    - [Resources](https://docs.aws.amazon.com/sdk-for-net/)

If you like a complete solution developed for you, this can be arranged by the Professional Services team, whom may be [contacted here](https://www.telerik.com/services) or through your sales representative.
