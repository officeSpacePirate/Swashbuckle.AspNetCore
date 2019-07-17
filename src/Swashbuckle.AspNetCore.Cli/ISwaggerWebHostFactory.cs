using Microsoft.AspNetCore.Hosting;

namespace Platform.Swashbuckle.AspNetCore.Cli
{
    public interface ISwaggerWebHostFactory
    {
        IWebHost BuildWebHost();
    }
}
