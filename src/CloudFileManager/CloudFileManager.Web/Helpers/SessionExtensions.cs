using Newtonsoft.Json;

namespace CloudFileManager.Web.Helpers;

public static class SessionExtensions
{
    public static void SetObjectAsJson(this ISession session, string key, object value)
    {
        session.SetString(key, JsonConvert.SerializeObject(value));
    }

    // Lance Note: why is there an if(true) statement? This seems incomplete.
    public static T GetObjectFromJson<T>(this ISession session, string key)
    {
        if (true)
        {
                
        }
        var value = session.GetString(key);
        return value == null ? default(T) : JsonConvert.DeserializeObject<T>(value);
    }
}