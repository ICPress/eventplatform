using System.Security.Claims;
using Apache.Ignite.Core.Client;

public static class ConfigUtil
{
    public static IgniteClientConfiguration GetIgniteConfiguration(ServerSettings settings)
    {
        return new IgniteClientConfiguration
        {
            Endpoints = new[] { settings.IgniteEndpoint }
        };
    }
}