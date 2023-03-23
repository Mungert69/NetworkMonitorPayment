using NetworkMonitor.Payment.Models;
using System.Collections.Generic;
public class PaymentOptions
{
    public string PublishableKey { get; set; }
    public string SecretKey { get; set; }
    public string WebhookSecret { get; set; }
    public string Domain { get; set; }
    public string InstanceName{get;set;}
    public string HostName{get;set;}
    public List<ProductObj> Products { get => _products; set => _products = value; }

    private List<ProductObj> _products;

}
