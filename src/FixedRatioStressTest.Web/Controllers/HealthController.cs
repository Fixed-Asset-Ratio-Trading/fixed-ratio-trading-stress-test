using Microsoft.AspNetCore.Mvc;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<HealthController> _logger;

    public HealthController(ISolanaClientService solanaClient, ILogger<HealthController> logger)
    {
        _solanaClient = solanaClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> GetHealth()
    {
        try
        {
            var isHealthy = await _solanaClient.IsHealthyAsync();
            var slot = await _solanaClient.GetSlotAsync();

            var health = new
            {
                Status = isHealthy ? "healthy" : "unhealthy",
                Solana = new
                {
                    Healthy = isHealthy,
                    CurrentSlot = slot,
                    Timestamp = DateTime.UtcNow
                }
            };

            if (isHealthy)
            {
                return Ok(health);
            }
            else
            {
                return StatusCode(503, health);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health");
            return StatusCode(500, new
            {
                Status = "error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    [HttpGet("solana")]
    public async Task<ActionResult> GetSolanaHealth()
    {
        try
        {
            var isHealthy = await _solanaClient.IsHealthyAsync();
            var slot = await _solanaClient.GetSlotAsync();

            return Ok(new
            {
                Healthy = isHealthy,
                CurrentSlot = slot,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Solana health");
            return StatusCode(500, new
            {
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}


