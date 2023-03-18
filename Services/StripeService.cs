using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Payment.Services
{
    public interface IStripeService
    {
        Dictionary<string, string> SessionList { get; set; }
    }

    public class StripeService : IStripeService
    {
        private Dictionary<string, string> _sessionList = new Dictionary<string, string>();

        public Dictionary<string, string> SessionList { get => _sessionList; set => _sessionList = value; }
    }
}