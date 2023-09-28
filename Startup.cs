using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;   
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Serialization;
using Stripe;
using NetworkMonitor.Payment.Services;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Utils.Helpers;
using HostInitActions;
namespace NetworkMonitor.Payment
{
    public class Startup
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        public Startup(IConfiguration configuration)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _config = configuration;
        }
        public IConfiguration _config { get; }
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            StripeConfiguration.AppInfo = new AppInfo
            {
                Name = "stripe-samples/checkout-single-subscription",
                Url = "https://github.com/stripe-samples/checkout-single-subscription",
                Version = "0.0.1",
            };
            services.Configure<PaymentOptions>(options =>
            {
                options.StripePublishableKey = _config.GetValue<string>("StripePublishableKey");
                options.StripeSecretKey = _config.GetValue<string>("StripeSecretKey");
                options.StripeWebhookSecret = _config.GetValue<string>("StripeWebhookSecret");
                options.StripeDomain = _config.GetValue<string>("Domain");
               options.SystemUrls = _config.GetSection("SystemUrls").Get<List<SystemUrl>>() ?? throw new ArgumentNullException("SystemParams.SystemUrls");
               options.LocalSystemUrl = _config.GetSection("LocalSystemUrl").Get<SystemUrl>() ?? throw new ArgumentNullException("LocalSystemUrl");
               
                options.LoadServer = _config.GetValue<string>("LoadServer");
                options.StripeProducts = new List<ProductObj>();
                _config.GetSection("Products").Bind(options.StripeProducts);
            });
            services.AddCors(options =>
           {
               options.AddPolicy("AllowAnyOrigin",
                   builder => builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
           });
            services.AddSingleton<IStripeService, StripeService>();
            services.AddSingleton<IFileRepo, FileRepo>();
            services.AddSingleton<INetLoggerFactory, NetLoggerFactory>();
            services.AddSingleton<IRabbitListener, RabbitListener>();
            services.AddSingleton<ISystemParamsHelper, SystemParamsHelper>();
            services.AddSingleton(_cancellationTokenSource);
            services.AddAsyncServiceInitialization()
        .AddInitAction<IStripeService>(async (stripeService) =>
        {
            await stripeService.Init();
        })
         .AddInitAction<IRabbitListener>((rabbitListener) =>
                    {
                        return Task.CompletedTask;
                    });

            services.AddControllersWithViews().AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy(),
                };
            });
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime)
        {
            appLifetime.ApplicationStopping.Register(() =>
            {
                _cancellationTokenSource.Cancel();
            });
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseCors("AllowAnyOrigin");
            //app.UseHttpsRedirection();
            app.UseFileServer();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
