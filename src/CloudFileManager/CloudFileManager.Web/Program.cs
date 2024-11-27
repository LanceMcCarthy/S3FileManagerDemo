using Amazon.Runtime.CredentialManagement;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMvc().AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);
builder.Services.AddKendo();
builder.Services.AddSession();

var app = builder.Build();


// First time scope to write the credentials file needed by the AWS SDK
using (var serviceScope = app.Services.CreateScope())
{
    var config = serviceScope.ServiceProvider.GetRequiredService<IConfiguration>();

    Console.WriteLine($"Create the Default AWS profile...");
    var options = new CredentialProfileOptions
    {
        AccessKey = config["AWS_ACCESS_KEY"],
        SecretKey = config["AWS_SECRET_ACCESS_KEY"],
    };
    var profile = new CredentialProfile("Default", options);
    var sharedFile = new SharedCredentialsFile();
    sharedFile.RegisterProfile(profile);
    
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseSession();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();