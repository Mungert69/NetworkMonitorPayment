using Newtonsoft.Json;

namespace NetworkMonitor.Payment.Models;

public class ErrorMessage
{
    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}

public class ErrorResponse
{
    [JsonProperty("error")]
    public ErrorMessage ErrorMessage { get; set; } = new ErrorMessage();
}