using NetworkMonitor.Payment.Models;
using NetworkMonitor.Objects;
using System.Collections.Generic;
public class PaymentOptions
{
    public string PublishableKey { get; set; }
    public string SecretKey { get; set; }
    public string WebhookSecret { get; set; }
    public string Domain { get; set; }
    public SystemUrl SystemUrl { get; set; }
    public List<ProductObj> Products { get => _products; set => _products = value; }

    private List<ProductObj> _products;

}
