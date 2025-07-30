
using Newtonsoft.Json;
namespace NetworkMonitor.Payment.Models;
public class ConfigResponse
{
    [JsonProperty("publishableKey")]
    public string PublishableKey { get; set; } = string.Empty;

    [JsonProperty("proPrice")]
    public string ProPrice { get; set; } = string.Empty;

    [JsonProperty("basicPrice")]
    public string BasicPrice { get; set; } = string.Empty;
}
