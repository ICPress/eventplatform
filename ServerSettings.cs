public class ServerSettings
{
    public string MysqlConnectionGorse { get; set; } = "";
    public string MysqlConnectionStoryPop { get; set; } = "";

    public string JWTSecret { get; set; } = "";

    public string IgniteEndpoint { get; set; } = "";
    public string SpacyEndpoint { get; set; } = "";

    public string GorseAPIEndpoint { get; set; } = "";

    public string SwiftStorageEndpoint { get; set; } = "";

    public OpenStackAuth SwiftAuth { get; set; } = new OpenStackAuth();

    public string CDNSmallName { get; set; } = "";

    public string CDNLargeName { get; set; } = "";
    
    public string FirebaseSDKCredentialsJson { get; set; } = "";

    public string GroqAPIEndpoint { get; set; } = "";
    public string GroqAPIKey { get; set; } = "";

    public string ArticleClassificationPromptTemplate { get; set; } = "";
    
}