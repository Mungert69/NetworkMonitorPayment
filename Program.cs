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
        private static string _pfxKey="";
        public static void Main(string[] args)
        {
            IConfigurationRoot config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .Build();
        _pfxKey=config["Pfxkey"] ?? "";
            IWebHost host = CreateWebHostBuilder(args).Build();
            host.Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
         WebHost.CreateDefaultBuilder(args).UseKestrel(options =>
            {
                options.Listen(IPAddress.Any, 2082);
                options.Listen(IPAddress.Any, 2083, listenOptions =>
                {
                    listenOptions.UseHttps("https-freenetworkmonitor.pfx", _pfxKey);
                });
            }).UseStartup<Startup>();
    }
}
