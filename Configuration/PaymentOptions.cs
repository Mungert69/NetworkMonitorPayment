
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using System.Collections.Generic;
public class PaymentOptions
{
    public string StripePublishableKey { get; set; }
    public string StripeSecretKey { get; set; }
    public string StripeWebhookSecret { get; set; }
    public string StripeDomain { get; set; }
    public string PayPalClientID { get; set; }
    public string PayPalSecret { get; set; }
    public string PaymentServerUrl { get; set; }
    public List<SystemUrl> SystemUrls { get; set; }
    public SystemUrl LocalSystemUrl {get;set;}
    public string LoadServer{get;set;}
    public List<ProductObj> StripeProducts { get => _stripeProducts; set => _stripeProducts = value; }

    private List<ProductObj> _stripeProducts;

}
