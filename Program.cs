using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;


namespace NetworkMonitor.Payment
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IConfigurationRoot config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .Build();
            IWebHost host = CreateWebHostBuilder(args).Build();
            host.Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
         WebHost.CreateDefaultBuilder(args).UseKestrel(options =>
            {
                options.Listen(IPAddress.Any, 2058);
                options.Listen(IPAddress.Any, 2059, listenOptions =>
                {
                    listenOptions.UseHttps("https.pfx", "Ac£0462110");
                });
            }).UseStartup<Startup>();
    }
}
