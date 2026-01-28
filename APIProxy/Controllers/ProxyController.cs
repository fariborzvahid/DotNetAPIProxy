using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace APIProxy.Controllers;

[ApiController]
[Route("api/proxy")]
public class ProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProxyController> _logger;
    private readonly IConfiguration _configuration;

    public ProxyController(IHttpClientFactory httpClientFactory,
                          ILogger<ProxyController> logger,
                          IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet("{*url}")]
    public async Task<IActionResult> Get(string url)
    {
        try
        {
            // Validate and process URL
            if (!TryProcessUrl(url, out var targetUri, out var errorResult))
                return errorResult;

            var client = _httpClientFactory.CreateClient("ProxyClient");
            var proxyRequest = CreateProxyRequest(HttpContext.Request, targetUri.ToString(), HttpMethod.Get);

            var response = await client.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead);

            // Copy response headers
            foreach (var header in response.Headers)
            {
                if (!Response.Headers.ContainsKey(header.Key))
                    Response.Headers[header.Key] = header.Value.ToArray();
            }

            // Handle non-success status
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, errorContent);
            }

            // Stream response
            var stream = await response.Content.ReadAsStreamAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            return File(stream, contentType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Proxy request failed for URL: {Url}", url);
            return StatusCode(502, "Bad Gateway");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in proxy");
            return StatusCode(500, "Internal Server Error");
        }
    }

    private bool TryProcessUrl(string url, out Uri targetUri, out IActionResult errorResult)
    {
        errorResult = null;
        targetUri = null;

        // Check if URL is provided
        if (string.IsNullOrWhiteSpace(url))
        {
            errorResult = BadRequest("URL is required");
            return false;
        }

        // Ensure URL has proper scheme
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        // Parse URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out targetUri))
        {
            errorResult = BadRequest("Invalid URL format");
            return false;
        }

        // Security checks
        if (!IsUrlAllowed(targetUri))
        {
            errorResult = BadRequest("URL not allowed");
            return false;
        }

        return true;
    }

    private bool IsUrlAllowed(Uri uri)
    {
        var allowedDomains = _configuration.GetSection("Proxy:AllowedDomains").Get<string[]>() ?? Array.Empty<string>();
        var blockedDomains = _configuration.GetSection("Proxy:BlockedDomains").Get<string[]>() ?? Array.Empty<string>();

        // Check blocked domains first
        if (blockedDomains.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return false;

        // If whitelist is empty, allow all except blocked
        if (!allowedDomains.Any())
            return true;

        return allowedDomains.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private HttpRequestMessage CreateProxyRequest(HttpRequest originalRequest, string url, HttpMethod method)
    {
        var request = new HttpRequestMessage(method, url);

        // Copy headers
        foreach (var header in originalRequest.Headers)
        {
            var key = header.Key;

            // Skip headers that shouldn't be forwarded
            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!request.Headers.TryAddWithoutValidation(key, header.Value.ToArray()))
            {
                request.Content?.Headers.TryAddWithoutValidation(key, header.Value.ToArray());
            }
        }

        return request;
    }
}