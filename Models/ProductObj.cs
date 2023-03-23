using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Payment.Models
{
    public class ProductObj
    {
        private  string _priceId ;
        private string _productName;
        private int _hostLimit;

        public string PriceId { get => _priceId; set => _priceId = value; }
        public string ProductName { get => _productName; set => _productName = value; }
        public int HostLimit { get => _hostLimit; set => _hostLimit = value; }
    }
}