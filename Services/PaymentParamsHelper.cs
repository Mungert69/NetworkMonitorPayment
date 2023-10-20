using NetworkMonitor.Objects;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using RestSharp;
using Newtonsoft.Json;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Factory;
namespace NetworkMonitor.Utils.Helpers
{
  
    public class PaymentParamsHelper : ISystemParamsHelper
    {


        private SystemUrl _thisSystemUrl;
        public PaymentParamsHelper(SystemUrl thisSystemUrl)
        {
            _thisSystemUrl=thisSystemUrl;

        }
        public string GetPublicIP()
        {
            return "";
        }
        public SystemParams GetSystemParams()
        {
            SystemParams systemParams = new SystemParams();

            systemParams.ThisSystemUrl = _thisSystemUrl;
            return systemParams;
        }

        public PingParams GetPingParams()
        {

            return new PingParams();

        }
    }

}