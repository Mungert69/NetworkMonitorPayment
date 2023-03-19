public class PaymentOptions
{
    public string PublishableKey { get; set; }
    public string SecretKey { get; set; }
    public string WebhookSecret { get; set; }

    public string BasicPrice { get; set; }
    public string ProPrice { get; set; }
    public string Domain { get; set; }
    public string InstanceName{get;set;}
    public string HostName{get;set;}
}
