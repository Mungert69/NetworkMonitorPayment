using Newtonsoft.Json;

namespace NetworkMonitor.Payment.Models;

public class ErrorMessage
{
    [JsonProperty("message")]
    public string Message { get; set; }
}

public class ErrorResponse
{
    [JsonProperty("error")]
    public ErrorMessage ErrorMessage { get; set; }
}