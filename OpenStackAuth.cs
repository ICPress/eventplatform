public class ApplicationCredential
{
    public string id { get; set; } = "";
    public string secret { get; set; } = "";
}

public class Auth
{
    public Identity identity { get; set; } = new Identity();
}

public class Identity
{
    public List<string> methods { get; set; } = new List<string>();
    public ApplicationCredential application_credential { get; set; } = new ApplicationCredential();
}

public class OpenStackAuth
{
    public Auth auth { get; set; } = new Auth();
}