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

        public string PriceId { get => _priceId; set => _priceId = value; }
        public string ProductName { get => _productName; set => _productName = value; }
    }
}