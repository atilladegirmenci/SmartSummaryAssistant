using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace SmartVoiceNotes.UI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");

            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri("https://smartsummary-dvhfh9bucqbnbydm.switzerlandnorth-01.azurewebsites.net/"),
                Timeout = TimeSpan.FromMinutes(5)
            });

            await builder.Build().RunAsync();
        }
    }
}
