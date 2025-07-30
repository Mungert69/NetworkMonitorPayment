
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using System.Collections.Generic;
public class PaymentOptions
{
    public string StripePublishableKey { get; set; } = string.Empty;
    public string StripeSecretKey { get; set; } = string.Empty;
    public string StripeWebhookSecret { get; set; } = string.Empty;
    public string StripeDomain { get; set; } = string.Empty;
    public string PayPalClientID { get; set; } = string.Empty;
    public string PayPalSecret { get; set; } = string.Empty;
    public string PaymentServerUrl { get; set; } = string.Empty;
    public List<SystemUrl> SystemUrls { get; set; } = new List<SystemUrl>();
    public SystemUrl LocalSystemUrl { get; set; } = new SystemUrl();
    public string LoadServer { get; set; } = string.Empty;
    public List<ProductObj> StripeProducts { get => _stripeProducts; set => _stripeProducts = value; }

    private List<ProductObj> _stripeProducts = new List<ProductObj>();
}
