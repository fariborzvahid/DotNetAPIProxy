using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace APIProxy.Controllers;

[ApiController]
[Route("[controller]")]
public class ProxyController : ControllerBase
{
    

    private readonly ILogger<ProxyController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ProxyController(ILogger<ProxyController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

        [HttpGet("{*url}")]
        public async Task<IActionResult> Get(string url)
        {
            var client = _httpClientFactory.CreateClient("ProxyClient");
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }

            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [HttpPost("{*url}")]
        public async Task<IActionResult> Post(string url, [FromBody] object data)
        {
            var client = _httpClientFactory.CreateClient("ProxyClient");
            var response = await client.PostAsJsonAsync(url, data);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }

            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    


}
