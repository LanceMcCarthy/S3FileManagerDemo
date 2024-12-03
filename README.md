# S3FileManagerDemo
A demo app on how to connect Telerik UI for ASP.NET Core FileManager control to an Amazon S3 service.

## Setup
### Prerequisite: AWS SDK Credentials file

You must have a credentials file present when using the S3 SDK. For your convenience, here are the steps:

- Phase 1. Create user credentials
    1. Sign in to the AWS Management Console and open the IAM console at [https://console.aws.amazon.com/iam/](https://console.aws.amazon.com/iam/).
    2. Create a new user with permissions limited to the services and actions that you want your code to have access to. For more information about creating a new user, see [Creating IAM users (Console)](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_users_create.html#id_users_create_console), and follow the instructions **through step 8**.
    3. Choose Download .csv to save a local copy of your AWS credentials.
- Phase 2. Create a local AWS SDK credentials file
    1. Open File Explorer and navigate to `%appdata%`
    2. Create a new folder named `.aws`
    3. Create a new file named `credentials` (no extension)
    4. Open the file and enter the following content

    ```xml
    [default]
    aws_access_key_id = your_access_key_id
    aws_secret_access_key = your_secret_access_key
    ```
    
    5. Update the values using the CSV file you downloaded in phase 1, save and close the file

### Build and Deploy the Project

 1. Now, you can do a Rebuild of the project and deploy it
 2. You should see the following appearance (will vary according to your bucket's contents).

![image](https://github.com/user-attachments/assets/c8e647d9-283b-490d-950f-9c6fc0a1b2e1)
